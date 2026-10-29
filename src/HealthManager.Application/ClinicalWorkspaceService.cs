using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public sealed record ClinicalField(string Label, string Type = "Text", bool Required = false, string? Unit = null, decimal? Min = null, decimal? Max = null,
    string? DefaultValue = null, decimal? Weight = null, decimal? AlertBelow = null, decimal? AlertAbove = null);
public sealed record SaveClinicalCatalogRequest(
    [Required] string Kind, [Required, MaxLength(120)] string Folder,
    [Required, MaxLength(200)] string Title, [MaxLength(30000)] string Content,
    bool Personal, ClinicalField[]? Fields, Guid? Revision);
public sealed record ClinicalCatalogResponse(Guid Id, string Kind, string Folder, string Title, string Content,
    Guid? OwnerUserId, ClinicalField[] Fields, Guid Revision);
public sealed record CreateClinicalEntryRequest(
    Guid ClientRequestId, [Required] string Kind, Guid? CatalogItemId,
    [Required, MaxLength(200)] string Title, [MaxLength(30000)] string Content,
    Dictionary<string, string>? Values, string SignatureMode = "Paper", Guid? CorrectsEntryId = null);
public sealed record ClinicalEntryResponse(Guid Id, Guid AppointmentId, Guid PatientId, string Kind, string Title,
    string Content, ClinicalField[] Fields, Dictionary<string, string> Values, string SignatureMode,
    string PatientName, string DoctorName, string DoctorCrm, string ClinicName, DateTimeOffset CreatedAt, Guid? CorrectsEntryId);

public sealed class ClinicalWorkspaceService(IApplicationDbContext db, ITenantProvider tenant)
{
    private static readonly string[] CatalogKinds = ["Document", "Exam", "Food", "Anamnesis", "Questionnaire"];
    private static readonly string[] EntryKinds = ["Document", "ExamOrder", "ExamResult", "Anamnesis", "Questionnaire", "Nutrition", "Summary"];
    private static readonly ClinicalField[] SummaryFields = [new("Diagnósticos"), new("Antecedentes"), new("Medicamentos em uso"), new("Alergias"), new("Observações")];

    private Guid Clinic => TenantGuard.RequireClinicId(tenant);
    private Guid User => tenant.UserId ?? throw new UnauthorizedAccessException("Usuário não autenticado.");
    private void RequireCatalogAccess()
    {
        if (tenant.Role is not (UserRole.Admin or UserRole.Doctor)) throw new ForbiddenAccessException("Acesso restrito a médicos e administradores.");
    }
    private IQueryable<ClinicalCatalogItem> Catalog() => db.ClinicalCatalogItems.Where(x => x.ClinicId == Clinic && x.DeletedAt == null && (x.OwnerUserId == null || x.OwnerUserId == User));

    public async Task<ClinicalCatalogResponse[]> ListCatalogAsync(string? kind, string? search, CancellationToken ct)
    {
        RequireCatalogAccess();
        var query = Catalog().AsNoTracking();
        if (!string.IsNullOrEmpty(kind)) query = query.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); query = query.Where(x => x.Title.ToLower().Contains(term) || x.Folder.ToLower().Contains(term)); }
        return (await query.OrderBy(x => x.Folder).ThenBy(x => x.Title).Take(1000).ToListAsync(ct)).Select(CatalogResponse).ToArray();
    }

    public async Task<ClinicalCatalogResponse> SaveCatalogAsync(Guid? id, SaveClinicalCatalogRequest request, CancellationToken ct)
    {
        RequireCatalogAccess();
        if (!CatalogKinds.Contains(request.Kind)) throw new InvalidOperationException("Tipo de cadastro inválido.");
        Text(request.Title, 200, true); Text(request.Folder, 120, true); Text(request.Content, 30000);
        if (request.Personal && tenant.Role != UserRole.Doctor) throw new ForbiddenAccessException("Somente médicos possuem modelos pessoais.");
        var fields = request.Fields ?? [];
        ValidateFields(fields);
        var item = id is null ? new ClinicalCatalogItem { ClinicId = Clinic, Kind = request.Kind, OwnerUserId = request.Personal ? User : null }
            : await Catalog().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Modelo não encontrado.");
        if (id is not null)
        {
            if (request.Revision != item.Revision) throw new DbUpdateConcurrencyException("Modelo alterado por outro usuário. Recarregue antes de salvar.");
            if (request.Kind != item.Kind || request.Personal != (item.OwnerUserId is not null)) throw new InvalidOperationException("Tipo e proprietário não podem ser alterados. Crie uma cópia.");
        }
        item.Title = request.Title.Trim(); item.Folder = request.Folder.Trim(); item.Content = request.Content ?? "";
        item.FieldsJson = JsonSerializer.Serialize(fields); item.Revision = Guid.NewGuid();
        if (id is null) db.ClinicalCatalogItems.Add(item);
        Audit("clinical_catalog.saved", item.Id);
        await db.SaveChangesAsync(ct);
        return CatalogResponse(item);
    }

    public async Task DeleteCatalogAsync(Guid id, Guid revision, CancellationToken ct)
    {
        RequireCatalogAccess();
        var item = await Catalog().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Modelo não encontrado.");
        if (revision != item.Revision) throw new DbUpdateConcurrencyException("Modelo alterado. Recarregue antes de excluir.");
        item.DeletedAt = DateTimeOffset.UtcNow; item.Revision = Guid.NewGuid(); Audit("clinical_catalog.deleted", id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ClinicalEntryResponse[]> ListAppointmentAsync(Guid appointmentId, CancellationToken ct)
    {
        await AppointmentAsync(appointmentId, false, ct);
        return (await db.ClinicalEntries.AsNoTracking().Where(x => x.ClinicId == Clinic && x.AppointmentId == appointmentId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync(ct)).Select(EntryResponse).ToArray();
    }

    public async Task<PagedResult<ClinicalEntryResponse>> ListPatientAsync(Guid patientId, int page, int pageSize, string? kind, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(x => x.ClinicId == Clinic && x.Id == patientId, ct)) throw new KeyNotFoundException("Paciente não encontrado.");
        var query = db.ClinicalEntries.AsNoTracking().Where(x => x.ClinicId == Clinic && x.PatientId == patientId);
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(x => x.Kind == kind);
        if (tenant.Role == UserRole.Doctor) { var doctor = await DoctorIdAsync(ct); query = query.Where(x => x.DoctorId == doctor); }
        page = Math.Clamp(page, 1, 1000000); pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items.Select(EntryResponse).ToArray(), page, pageSize, total);
    }

    public async Task<ClinicalEntryResponse> CreateEntryAsync(Guid appointmentId, CreateClinicalEntryRequest request, CancellationToken ct)
    {
        var appointment = await AppointmentAsync(appointmentId, true, ct);
        if (request.ClientRequestId == Guid.Empty) throw new InvalidOperationException("Identificador da requisição obrigatório.");
        var existing = await db.ClinicalEntries.AsNoTracking().FirstOrDefaultAsync(x => x.ClinicId == Clinic && x.AppointmentId == appointmentId && x.ClientRequestId == request.ClientRequestId, ct);
        if (existing is not null) { if (existing.AuthorId != User) throw new ForbiddenAccessException(); return EntryResponse(existing); }
        var record = await db.ClinicalRecords.FirstOrDefaultAsync(x => x.ClinicId == Clinic && x.AppointmentId == appointmentId, ct);
        if (record is null || record.Status != ClinicalRecordStatus.Draft)
            throw new InvalidOperationException("Crie um prontuário em rascunho antes de registrar. Prontuários finalizados aceitam somente adendos.");
        if (!EntryKinds.Contains(request.Kind)) throw new InvalidOperationException("Tipo de registro inválido.");
        Text(request.Title, 200, true); Text(request.Content, 30000);
        if (request.SignatureMode is not ("Paper" or "DigitalMock") || request.Kind != "Document" && request.SignatureMode != "Paper") throw new InvalidOperationException("Forma de assinatura inválida.");
        ClinicalEntry? original = null;
        if (request.CorrectsEntryId is { } correction)
            original = await db.ClinicalEntries.AsNoTracking().FirstOrDefaultAsync(x => x.ClinicId == Clinic && x.AppointmentId == appointmentId && x.Id == correction && x.Kind == request.Kind, ct) ?? throw new InvalidOperationException("Registro original inválido.");
        ClinicalCatalogItem? template = null;
        if (request.CatalogItemId is { } templateId)
        {
            template = await Catalog().AsNoTracking().FirstOrDefaultAsync(x => x.Id == templateId, ct) ?? throw new KeyNotFoundException("Modelo não encontrado.");
            var expectedKind = request.Kind switch { "ExamOrder" or "ExamResult" => "Exam", "Nutrition" => "Food", _ => request.Kind };
            if (template.Kind != expectedKind) throw new InvalidOperationException("Modelo incompatível com o registro.");
        }
        if (request.Kind is "Anamnesis" or "Questionnaire" && template is null && original is null) throw new InvalidOperationException("Selecione um formulário.");
        var fields = request.Kind == "Summary" ? SummaryFields : JsonSerializer.Deserialize<ClinicalField[]>(template?.FieldsJson ?? original?.FieldsJson ?? "[]")!;
        var values = request.Values?.ToDictionary(pair => pair.Key, pair => pair.Value ?? "") ?? new();
        ValidateValues(fields, values);
        var content = request.Content ?? "";
        if (string.IsNullOrWhiteSpace(content) && !values.Values.Any(v => !string.IsNullOrWhiteSpace(v))) throw new InvalidOperationException("Preencha o conteúdo ou os campos do registro.");
        var clinic = await db.Clinics.AsNoTracking().FirstAsync(x => x.Id == Clinic, ct);
        var localTime = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(clinic.Timezone));
        content = content.Replace("{{paciente}}", appointment.Patient!.Name).Replace("{{medico}}", appointment.Doctor!.Name)
            .Replace("{{crm}}", appointment.Doctor.Crm).Replace("{{clinica}}", clinic.Name).Replace("{{data}}", localTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        var entry = new ClinicalEntry { ClinicId = Clinic, AppointmentId = appointment.Id, PatientId = appointment.PatientId,
            DoctorId = appointment.DoctorId, AuthorId = User, ClientRequestId = request.ClientRequestId, Kind = request.Kind,
            Title = request.Title.Trim(), Content = content, FieldsJson = JsonSerializer.Serialize(fields), ValuesJson = JsonSerializer.Serialize(values),
            SignatureMode = request.SignatureMode, CorrectsEntryId = request.CorrectsEntryId, PatientName = appointment.Patient.Name,
            DoctorName = appointment.Doctor.Name, DoctorCrm = appointment.Doctor.Crm, ClinicName = clinic.Name };
        record.UpdatedAt = DateTimeOffset.UtcNow;
        db.ClinicalEntries.Add(entry); Audit("clinical_entry.created", entry.Id);
        await db.SaveChangesAsync(ct);
        return EntryResponse(entry);
    }

    private async Task<Guid> DoctorIdAsync(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.ClinicId == Clinic && x.Id == User && x.IsActive, ct) ?? throw new UnauthorizedAccessException();
        return await db.Doctors.Where(x => x.ClinicId == Clinic && x.Email == user.Email && x.IsActive).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct) ?? throw new ForbiddenAccessException("Usuario medico sem profissional vinculado. Solicite ao administrador um cadastro em Medicos com o mesmo e-mail do usuario.");
    }
    private async Task<Appointment> AppointmentAsync(Guid id, bool write, CancellationToken ct)
    {
        if (write && tenant.Role is not (UserRole.Admin or UserRole.Doctor)) throw new ForbiddenAccessException("Somente o administrador ou o médico responsável pode registrar conteúdo clínico.");
        var appointment = await db.Appointments.Include(x => x.Patient).Include(x => x.Doctor).FirstOrDefaultAsync(x => x.ClinicId == Clinic && x.Id == id, ct) ?? throw new KeyNotFoundException("Consulta não encontrada.");
        if (tenant.Role == UserRole.Doctor && await DoctorIdAsync(ct) != appointment.DoctorId) throw new ForbiddenAccessException("Consulta de outro profissional.");
        return appointment;
    }
    private static void Text(string? value, int max, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(value) || value?.Length > max) throw new InvalidOperationException("Conteúdo vazio ou maior que o limite permitido.");
    }
    private static void ValidateFields(ClinicalField[] fields)
    {
        if (fields.Length > 100 || fields.Any(f => f is null) || fields.Select(f => f.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length) throw new InvalidOperationException("Campos repetidos ou em excesso.");
        foreach (var f in fields)
        {
            Text(f.Label, 120, true); Text(f.Unit, 30); Text(f.DefaultValue, 10000);
            if (f.Type is not ("Text" or "Number" or "Boolean") || f.Min > f.Max || f.AlertBelow > f.AlertAbove || f.Weight is < -1000 or > 1000
                || f.Type != "Number" && (f.Min is not null || f.Max is not null || f.AlertBelow is not null || f.AlertAbove is not null)
                || f.Type == "Text" && f.Weight is not null) throw new InvalidOperationException("Definição de campo inválida.");
            if (!string.IsNullOrEmpty(f.DefaultValue)) ValidateValues([f], new() { [f.Label] = f.DefaultValue });
        }
    }
    private static void ValidateValues(ClinicalField[] fields, Dictionary<string, string> values)
    {
        if (values.Count > 100 || values.Keys.Except(fields.Select(f => f.Label)).Any()) throw new InvalidOperationException("Campos desconhecidos.");
        foreach (var field in fields)
        {
            values.TryGetValue(field.Label, out var value); Text(value, 10000, field.Required);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (field.Type == "Number" && (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || number < field.Min || number > field.Max)) throw new InvalidOperationException($"Valor numérico inválido: {field.Label}.");
            if (field.Type == "Boolean" && value is not ("true" or "false")) throw new InvalidOperationException($"Resposta inválida: {field.Label}.");
        }
    }
    private void Audit(string action, Guid id) => db.AuditLogs.Add(new AuditLog { ClinicId = Clinic, UserId = User, Action = action, EntityName = "ClinicalWorkspace", EntityId = id });
    private static ClinicalCatalogResponse CatalogResponse(ClinicalCatalogItem x) => new(x.Id, x.Kind, x.Folder, x.Title, x.Content, x.OwnerUserId, JsonSerializer.Deserialize<ClinicalField[]>(x.FieldsJson)!, x.Revision);
    private static ClinicalEntryResponse EntryResponse(ClinicalEntry x) => new(x.Id, x.AppointmentId, x.PatientId, x.Kind, x.Title, x.Content, JsonSerializer.Deserialize<ClinicalField[]>(x.FieldsJson)!, JsonSerializer.Deserialize<Dictionary<string,string>>(x.ValuesJson)!, x.SignatureMode, x.PatientName, x.DoctorName, x.DoctorCrm, x.ClinicName, x.CreatedAt, x.CorrectsEntryId);
}
