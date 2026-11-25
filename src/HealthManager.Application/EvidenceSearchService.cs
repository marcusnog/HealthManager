using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public sealed record EvidenceRequest(string Terms, string Source, Guid? After = null);
public sealed record EvidenceHit(Guid PatientId, string Patient, Guid RecordId, string Origin, string Excerpt);
public sealed record EvidenceDocument(Guid PatientId, string Patient, Guid RecordId, string FileName);
public sealed record EvidencePage(int Inspected, int Manual, Guid? Next, List<EvidenceHit> Hits, List<EvidenceDocument> ManualDocuments);

public sealed class EvidenceSearchService(IApplicationDbContext db, ITenantProvider tenant, IStorageService storage)
{
    public async Task<EvidencePage> SearchAsync(EvidenceRequest input, CancellationToken ct)
    {
        var clinic = TenantGuard.RequireClinicId(tenant);
        var words = (input.Terms ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length is < 1 or > 4 || words.Any(x => x.Length is < 3 or > 64))
            throw new InvalidOperationException("Informe até quatro termos de 3 a 64 caracteres separados por vírgula.");
        var search = new Matcher(words);
        if (input.Source == "documentos") return await Documents(clinic, input.After, search, ct);
        if (input.Source != "fichas") throw new InvalidOperationException("Fonte de busca inválida.");
        const int size = 100;
        var patients = await db.Patients.AsNoTracking().Where(x => x.ClinicId == clinic && (input.After == null || x.Id.CompareTo(input.After.Value) > 0))
            .OrderBy(x => x.Id).Take(size + 1).Select(x => new { x.Id, x.Name, x.Notes, x.Tags }).ToListAsync(ct);
        var batch = patients.Take(size).ToArray();
        var names = batch.ToDictionary(x => x.Id, x => x.Name);
        var ids = names.Keys.ToArray();
        List<EvidenceHit> hits = [];
        void Find(Guid patient, Guid record, string source, string? content)
        {
            if (search.Excerpt(content) is { } text) hits.Add(new(patient, names[patient], record, source, text));
        }
        foreach (var x in batch) Find(x.Id, x.Id, "Cadastro", x.Notes + " " + x.Tags);
        foreach (var x in await db.Appointments.Where(x => x.ClinicId == clinic && ids.Contains(x.PatientId))
            .Select(x => new { x.PatientId, x.Id, x.Notes, x.StartAt }).ToListAsync(ct))
            Find(x.PatientId, x.Id, "Agenda", x.Notes);
        foreach (var x in await db.ClinicalRecords.Where(x => x.ClinicId == clinic && ids.Contains(x.PatientId))
            .Select(x => new { x.Id, x.PatientId, x.Content, x.Plan, x.Assessment, x.Prescription, x.History, x.ChiefComplaint, x.PhysicalExam }).ToListAsync(ct))
            Find(x.PatientId, x.Id, "Prontuário", string.Join(" ", x.Content, x.Plan, x.Assessment, x.Prescription, x.History, x.ChiefComplaint, x.PhysicalExam));
        foreach (var x in await db.ClinicalRecordAddendums.Where(x => x.ClinicId == clinic && x.ClinicalRecord!.ClinicId == clinic && ids.Contains(x.ClinicalRecord.PatientId))
            .Select(x => new { x.Id, x.ClinicalRecord!.PatientId, x.Content }).ToListAsync(ct))
            Find(x.PatientId, x.Id, "Adendo", x.Content);
        foreach (var x in await db.ClinicalEntries.Where(x => x.ClinicId == clinic && ids.Contains(x.PatientId))
            .Select(x => new { x.Id, x.PatientId, x.Title, x.Content, x.ValuesJson }).ToListAsync(ct))
            Find(x.PatientId, x.Id, "Documento clínico", string.Join(" ", x.Title, x.Content, x.ValuesJson));
        foreach (var x in await db.PatientCareRecords.Where(x => x.ClinicId == clinic && ids.Contains(x.PatientId))
            .Select(x => new { x.Id, x.PatientId, x.FieldsJson }).ToListAsync(ct))
            Find(x.PatientId, x.Id, "Acompanhamento", x.FieldsJson);
        var titles = db.Receivables.Where(x => x.ClinicId == clinic)
            .Select(x => new { x.Id, x.Description, PatientId = x.AppointmentId == null ? x.PatientId : x.Appointment!.PatientId });
        foreach (var x in await titles.Where(x => x.PatientId.HasValue && ids.Contains(x.PatientId.Value)).ToListAsync(ct))
            Find(x.PatientId!.Value, x.Id, "Conta a receber", x.Description);
        foreach (var x in await (from payment in db.Payments join bill in titles on payment.ReceivableId equals bill.Id
            where payment.ClinicId == clinic && bill.PatientId.HasValue && ids.Contains(bill.PatientId.Value)
            select new { payment.Id, bill.PatientId, payment.Notes }).ToListAsync(ct))
            Find(x.PatientId!.Value, x.Id, "Pagamento", x.Notes);
        foreach (var x in await db.PatientApplications.Where(x => x.ClinicId == clinic && ids.Contains(x.PatientId))
            .Select(x => new { x.PatientId, x.Id, x.Notes, x.Product!.Name }).ToListAsync(ct))
            Find(x.PatientId, x.Id, "Aplicação", x.Name + " " + x.Notes);
        foreach (var x in await db.PatientProductBalances.Where(x => x.ClinicId == clinic && ids.Contains(x.PatientId))
            .Select(x => new { x.PatientId, x.Id, Product = x.Product!.Name, Package = x.Package == null ? null : x.Package.Name }).ToListAsync(ct))
            Find(x.PatientId, x.Id, "Saldo contratado", x.Package + " " + x.Product);
        return new(batch.Length, 0, patients.Count > size ? batch[^1].Id : null, hits, []);
    }

    private async Task<EvidencePage> Documents(Guid clinic, Guid? after, Matcher search, CancellationToken ct)
    {
        const int size = 40;
        var files = await db.PatientDocuments.AsNoTracking().Where(x => x.ClinicId == clinic && x.Patient!.ClinicId == clinic &&
                (after == null || x.Id.CompareTo(after.Value) > 0))
            .OrderBy(x => x.Id).Take(size + 1).Select(x => new { x.Id, x.PatientId, x.Patient!.Name, x.FileName, x.SizeInBytes, x.StoragePath, x.ContentType }).ToListAsync(ct);
        List<EvidenceHit> hits = [];
        List<EvidenceDocument> manualDocuments = [];
        int inspected = 0;
        var timer = Stopwatch.StartNew();
        foreach (var file in files.Take(size))
        {
            ct.ThrowIfCancellationRequested();
            string? content = null;
            var pdf = file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) || file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            var image = file.ContentType is "image/png" or "image/jpeg" || Path.GetExtension(file.FileName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
            if ((pdf || image) && file.SizeInBytes is >= 0 and <= 16_777_216)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    await using var stream = await storage.DownloadPatientDocumentAsync(file.StoragePath, timeout.Token);
                    if (pdf) content = await RunTool("pdftotext", ["-q", "-enc", "UTF-8", "-", "-"], stream, timeout.Token);
                    if (string.IsNullOrWhiteSpace(content) && stream.CanSeek)
                    {
                        stream.Position = 0;
                        content = await Recognize(stream, pdf, timeout.Token);
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is IOException or KeyNotFoundException or OperationCanceledException or UnauthorizedAccessException) { }
            }
            if (string.IsNullOrWhiteSpace(content))
                manualDocuments.Add(new(file.PatientId, file.Name, file.Id, file.FileName));
            if ((search.Excerpt(content) ?? search.Excerpt(file.FileName)) is { } excerpt)
                hits.Add(new(file.PatientId, file.Name, file.Id, file.FileName, excerpt));
            inspected++;
            if (timer.Elapsed.TotalSeconds >= 3) break;
        }
        return new(inspected, manualDocuments.Count, inspected < files.Count ? files[inspected - 1].Id : null, hits, manualDocuments);
    }

    private static readonly SemaphoreSlim OcrSlot = new(1);
    private static async Task<string?> Recognize(Stream input, bool pdf, CancellationToken ct)
    {
        await OcrSlot.WaitAsync(ct);
        try
        {
            if (!pdf) return await RunTool("tesseract", ["stdin", "stdout", "-l", "por"], input, ct);
            var dir = Directory.CreateTempSubdirectory("hm-ocr-");
            try
            {
                var prefix = Path.Combine(dir.FullName, "page");
                var rendered = await RunTool("pdftoppm", ["-q", "-f", "1", "-l", "9", "-scale-to", "1600", "-png", "-", prefix], input, ct);
                var pages = dir.GetFiles("page-*.png").OrderBy(x => x.Name).ToArray();
                if (rendered == null || pages.Length is 0 or > 8) return null;
                var result = new StringBuilder();
                foreach (var page in pages)
                {
                    await using var file = page.OpenRead();
                    var text = await RunTool("tesseract", ["stdin", "stdout", "-l", "por"], file, ct);
                    if (string.IsNullOrWhiteSpace(text)) return null;
                    result.AppendLine(text);
                }
                return result.ToString();
            }
            finally { dir.Delete(recursive: true); }
        }
        finally { OcrSlot.Release(); }
    }

    private static async Task<string?> RunTool(string executable, string[] args, Stream input, CancellationToken ct)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["OMP_THREAD_LIMIT"] = "1";
        using var proc = Process.Start(info) ?? throw new InvalidOperationException("Extrator de PDF indisponível.");
        using var onCancel = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(); } catch (InvalidOperationException) { } });
        try
        {
            async Task Feed()
            {
                try { await input.CopyToAsync(proc.StandardInput.BaseStream, ct); }
                finally { proc.StandardInput.Close(); }
            }
            async Task<string?> Read()
            {
                var buffer = new char[8192];
                var result = new StringBuilder();
                int read;
                while ((read = await proc.StandardOutput.ReadAsync(buffer, ct)) > 0)
                {
                    if (result.Length + read > 2_000_000) { proc.Kill(); return null; }
                    result.Append(buffer, 0, read);
                }
                return result.ToString();
            }
            var readText = Read();
            await Task.WhenAll(Feed(), readText, proc.StandardError.ReadToEndAsync(ct), proc.WaitForExitAsync(ct));
            return proc.ExitCode == 0 ? await readText : null;
        }
        finally { if (!proc.HasExited) proc.Kill(); }
    }

    private sealed class Matcher(string[] words)
    {
        private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
        public string? Excerpt(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            foreach (var word in words)
            {
                var index = Compare.IndexOf(content, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                if (index < 0) continue;
                int start = Math.Max(0, index - 90), end = Math.Min(content.Length, index + word.Length + 150);
                return Regex.Replace(content[start..end], @"\s+", " ").Trim();
            }
            return null;
        }
    }
}
