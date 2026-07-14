using System.Linq.Expressions;
using System.Text.Json;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public sealed class AuthService(
    IApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService)
{
    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? userAgent, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .Include(x => x.Clinic)
            .FirstOrDefaultAsync(x => x.Email == request.Email && x.DeletedAt == null && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Credenciais invalidas.");

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new InvalidOperationException("Credenciais invalidas.");
        }

        return await IssueTokensAsync(user, userAgent, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string? userAgent, CancellationToken cancellationToken)
    {
        var activeTokens = await dbContext.RefreshTokens
            .Include(x => x.User)
            .Where(x => x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        var refreshToken = activeTokens.FirstOrDefault(x => passwordHasher.Verify(request.RefreshToken, x.TokenHash))
            ?? throw new InvalidOperationException("Refresh token invalido.");

        refreshToken.RevokedAt = DateTimeOffset.UtcNow;
        refreshToken.UpdatedAt = DateTimeOffset.UtcNow;

        return await IssueTokensAsync(refreshToken.User!, userAgent, cancellationToken);
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var activeTokens = await dbContext.RefreshTokens
            .Where(x => x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        var refreshToken = activeTokens.FirstOrDefault(x => passwordHasher.Verify(request.RefreshToken, x.TokenHash));
        if (refreshToken is null)
        {
            return;
        }

        refreshToken.RevokedAt = DateTimeOffset.UtcNow;
        refreshToken.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, string? userAgent, CancellationToken cancellationToken)
    {
        var bundle = jwtTokenService.Generate(user);
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            ClinicId = user.ClinicId,
            UserId = user.Id,
            TokenHash = passwordHasher.Hash(bundle.RefreshToken),
            ExpiresAt = bundle.ExpiresAt.AddDays(30),
            UserAgent = userAgent
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            bundle.AccessToken,
            bundle.RefreshToken,
            bundle.ExpiresAt,
            new UserResponse(user.Id, user.ClinicId, user.Name, user.Email, user.Role));
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Usuario nao encontrado.");

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new InvalidOperationException("Senha atual incorreta.");
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ClinicProvisioningService(
    IApplicationDbContext dbContext,
    IPasswordHasher passwordHasher)
{
    public async Task<ClinicProvisioningResponse> CreateClinicAsync(CreateClinicRequest request, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Clinics.AnyAsync(x => x.Slug == request.Slug && x.DeletedAt == null, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("Ja existe uma clinica com este slug.");
        }

        var clinic = new Clinic
        {
            Name = request.Name,
            Slug = request.Slug,
            Timezone = request.Timezone,
            BusinessHoursJson = request.BusinessHoursJson,
            Cnpj = request.Cnpj,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address
        };

        var admin = new User
        {
            Clinic = clinic,
            ClinicId = clinic.Id,
            Name = request.AdminName,
            Email = request.AdminEmail,
            PasswordHash = passwordHasher.Hash(request.AdminPassword),
            Role = UserRole.Admin
        };

        dbContext.Clinics.Add(clinic);
        dbContext.Users.Add(admin);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ClinicProvisioningResponse(clinic.Id, admin.Id);
    }

    public async Task<UserResponse> CreateClinicUserAsync(Guid clinicId, CreateClinicUserRequest request, CancellationToken cancellationToken)
    {
        var clinicExists = await dbContext.Clinics.AnyAsync(x => x.Id == clinicId && x.DeletedAt == null, cancellationToken);
        if (!clinicExists)
        {
            throw new KeyNotFoundException("Clinica nao encontrada.");
        }

        var user = new User
        {
            ClinicId = clinicId,
            Name = request.Name,
            Email = request.Email,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = request.Role
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new UserResponse(user.Id, user.ClinicId, user.Name, user.Email, user.Role);
    }
}

public sealed class PatientService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IStorageService storageService)
{
    public async Task<PagedResult<PatientResponse>> ListAsync(PatientQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var normalizedSearch = query.Search?.Trim().ToLowerInvariant();
        var patientsQuery = dbContext.Patients.AsNoTracking()
            .Include(x => x.HealthInsuranceRef)
            .Where(x => x.ClinicId == clinicId && x.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            patientsQuery = patientsQuery.Where(x =>
                x.Name.ToLower().Contains(normalizedSearch) ||
                x.Cpf.Contains(normalizedSearch) ||
                x.Phone.Contains(normalizedSearch) ||
                (x.Email != null && x.Email.ToLower().Contains(normalizedSearch)));
        }

        if (!string.IsNullOrWhiteSpace(query.Email))
        {
            var normalizedEmail = query.Email.Trim().ToLowerInvariant();
            patientsQuery = patientsQuery.Where(x => x.Email != null && x.Email.ToLower().Contains(normalizedEmail));
        }

        if (!string.IsNullOrWhiteSpace(query.HealthInsurance))
        {
            var normalizedHI = query.HealthInsurance.Trim().ToLowerInvariant();
            patientsQuery = patientsQuery.Where(x => x.HealthInsurance != null && x.HealthInsurance.ToLower().Contains(normalizedHI));
        }

        var total = await patientsQuery.CountAsync(cancellationToken);

        var sortBy = (query.SortBy ?? "name").ToLowerInvariant();
        var sortDesc = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<Patient> ordered = sortBy switch
        {
            "cpf" => AppHelpers.OrderByKey(patientsQuery, sortDesc, x => x.Cpf),
            "phone" => AppHelpers.OrderByKey(patientsQuery, sortDesc, x => x.Phone),
            "email" => AppHelpers.OrderByKey(patientsQuery, sortDesc, x => x.Email ?? ""),
            "healthinsurance" => AppHelpers.OrderByKey(patientsQuery, sortDesc, x => x.HealthInsurance ?? ""),
            "createdat" => AppHelpers.OrderByKey(patientsQuery, sortDesc, x => x.CreatedAt),
            _ => AppHelpers.OrderByKey(patientsQuery, sortDesc, x => x.Name),
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new PatientResponse(
                x.Id, x.Name, x.Cpf, x.BirthDate, x.Phone, x.Email,
                x.HealthInsurance, x.HealthInsuranceId,
                x.HealthInsuranceRef != null ? x.HealthInsuranceRef.Name : null,
                x.Notes, x.PatientAccessToken))
            .ToListAsync(cancellationToken);

        return new PagedResult<PatientResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<PatientResponse> CreateAsync(CreatePatientRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var normalizedCpf = AppHelpers.NormalizeDigits(request.Cpf);
        if (!AppHelpers.ValidateCpf(normalizedCpf))
            throw new InvalidOperationException("CPF invalido.");

        var exists = await dbContext.Patients.AnyAsync(x => x.ClinicId == clinicId && x.Cpf == normalizedCpf && x.DeletedAt == null, cancellationToken);
        if (exists)
            throw new InvalidOperationException("Paciente ja cadastrado para esta clinica.");

        var patient = new Patient
        {
            ClinicId = clinicId,
            Name = request.Name,
            Cpf = normalizedCpf,
            BirthDate = request.BirthDate,
            Phone = request.Phone,
            Email = request.Email,
            HealthInsurance = request.HealthInsurance,
            HealthInsuranceId = request.HealthInsuranceId,
            Notes = request.Notes
        };

        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync(cancellationToken);

        var hiName = request.HealthInsuranceId.HasValue
            ? await dbContext.HealthInsurances.AsNoTracking()
                .Where(x => x.Id == request.HealthInsuranceId.Value)
                .Select(x => x.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new PatientResponse(patient.Id, patient.Name, patient.Cpf, patient.BirthDate, patient.Phone, patient.Email, patient.HealthInsurance, patient.HealthInsuranceId, hiName, patient.Notes, patient.PatientAccessToken);
    }

    public async Task DeleteAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var patient = await dbContext.Patients
            .FirstOrDefaultAsync(x => x.Id == patientId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Paciente nao encontrado.");

        patient.DeletedAt = DateTimeOffset.UtcNow;
        patient.UpdatedAt = DateTimeOffset.UtcNow;

        dbContext.AuditLogs.Add(new AuditLog
        {
            ClinicId = clinicId,
            UserId = tenantProvider.UserId,
            Action = "patient.deleted",
            EntityName = nameof(Patient),
            EntityId = patient.Id,
            PayloadJson = JsonSerializer.Serialize(new { patient.Name, patient.Cpf })
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PatientResponse> UpdateAsync(Guid patientId, UpdatePatientRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var patient = await dbContext.Patients.FirstOrDefaultAsync(x => x.Id == patientId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Paciente nao encontrado.");

        patient.Name = request.Name;
        patient.Phone = request.Phone;
        patient.Email = request.Email;
        patient.HealthInsurance = request.HealthInsurance;
        patient.HealthInsuranceId = request.HealthInsuranceId;
        patient.Notes = request.Notes;
        patient.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var hiName = request.HealthInsuranceId.HasValue
            ? await dbContext.HealthInsurances.AsNoTracking()
                .Where(x => x.Id == request.HealthInsuranceId.Value)
                .Select(x => x.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new PatientResponse(patient.Id, patient.Name, patient.Cpf, patient.BirthDate, patient.Phone, patient.Email, patient.HealthInsurance, patient.HealthInsuranceId, hiName, patient.Notes, patient.PatientAccessToken);
    }

    public async Task<IReadOnlyList<PatientDocumentResponse>> ListDocumentsAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var patientExists = await dbContext.Patients.AnyAsync(
            x => x.Id == patientId && x.ClinicId == clinicId && x.DeletedAt == null,
            cancellationToken);

        if (!patientExists)
        {
            throw new KeyNotFoundException("Paciente nao encontrado.");
        }

        return await dbContext.PatientDocuments.AsNoTracking()
            .Where(x => x.PatientId == patientId && x.ClinicId == clinicId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PatientDocumentResponse(
                x.Id,
                x.FileName,
                x.ContentType,
                x.SizeInBytes,
                x.StoragePath))
            .ToListAsync(cancellationToken);
    }

    public async Task<PatientDocumentResponse> UploadDocumentAsync(Guid patientId, CreatePatientDocumentRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var patientExists = await dbContext.Patients.AnyAsync(x => x.Id == patientId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken);
        if (!patientExists)
        {
            throw new KeyNotFoundException("Paciente nao encontrado.");
        }

        var storagePath = storageService.BuildPatientDocumentPath(clinicId, patientId, request.FileName);

        if (request.Content is not null)
            await storageService.UploadPatientDocumentAsync(storagePath, request.Content, request.ContentType, cancellationToken);

        var document = new PatientDocument
        {
            ClinicId = clinicId,
            PatientId = patientId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeInBytes = request.SizeInBytes,
            StoragePath = storagePath
        };

        dbContext.PatientDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PatientDocumentResponse(document.Id, document.FileName, document.ContentType, document.SizeInBytes, document.StoragePath);
    }

    public async Task<DownloadPatientDocumentResult> DownloadDocumentAsync(Guid patientId, Guid documentId, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var document = await dbContext.PatientDocuments.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == documentId &&
                     x.PatientId == patientId &&
                     x.ClinicId == clinicId &&
                     x.DeletedAt == null,
                cancellationToken)
            ?? throw new KeyNotFoundException("Documento do paciente nao encontrado.");

        var content = await storageService.DownloadPatientDocumentAsync(document.StoragePath, cancellationToken);
        return new DownloadPatientDocumentResult(content, document.ContentType, document.FileName);
    }

    public async Task DeleteDocumentAsync(Guid patientId, Guid documentId, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var document = await dbContext.PatientDocuments
            .FirstOrDefaultAsync(
                x => x.Id == documentId &&
                     x.PatientId == patientId &&
                     x.ClinicId == clinicId &&
                     x.DeletedAt == null,
                cancellationToken)
            ?? throw new KeyNotFoundException("Documento do paciente nao encontrado.");

        document.DeletedAt = DateTimeOffset.UtcNow;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        dbContext.AuditLogs.Add(new AuditLog
        {
            ClinicId = clinicId,
            UserId = tenantProvider.UserId,
            Action = "patient_document.deleted",
            EntityName = nameof(PatientDocument),
            EntityId = document.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                document.PatientId,
                document.FileName,
                document.StoragePath
            })
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

}

public sealed class DoctorService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IPasswordHasher passwordHasher)
{
    public async Task<PagedResult<DoctorResponse>> ListAsync(DoctorQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var doctorsQuery = dbContext.Doctors.AsNoTracking()
            .Include(x => x.DoctorSpecialties).ThenInclude(x => x.Specialty)
            .Where(x => x.ClinicId == clinicId && x.DeletedAt == null);

        var normalizedSearch = query.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            doctorsQuery = doctorsQuery.Where(x =>
                x.Name.ToLower().Contains(normalizedSearch) ||
                x.Crm.Contains(normalizedSearch) ||
                x.DoctorSpecialties.Any(s => s.Specialty.Name.ToLower().Contains(normalizedSearch)) ||
                (x.Email != null && x.Email.ToLower().Contains(normalizedSearch)));
        }

        var total = await doctorsQuery.CountAsync(cancellationToken);

        var sortBy = (query.SortBy ?? "name").ToLowerInvariant();
        var sortDesc = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<Doctor> ordered = sortBy switch
        {
            "crm" => AppHelpers.OrderByKey(doctorsQuery, sortDesc, x => x.Crm),
            "email" => AppHelpers.OrderByKey(doctorsQuery, sortDesc, x => x.Email ?? ""),
            "isactive" => AppHelpers.OrderByKey(doctorsQuery, sortDesc, x => x.IsActive),
            _ => AppHelpers.OrderByKey(doctorsQuery, sortDesc, x => x.Name),
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var response = items.Select(d => ToResponse(d)).ToList();
        return new PagedResult<DoctorResponse>(response, query.Page, query.PageSize, total);
    }

    public async Task DeleteAsync(Guid doctorId, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var doctor = await dbContext.Doctors
            .Include(x => x.DoctorSpecialties)
            .FirstOrDefaultAsync(x => x.Id == doctorId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Medico nao encontrado.");

        doctor.DeletedAt = DateTimeOffset.UtcNow;
        doctor.UpdatedAt = DateTimeOffset.UtcNow;

        dbContext.AuditLogs.Add(new AuditLog
        {
            ClinicId = clinicId,
            UserId = tenantProvider.UserId,
            Action = "doctor.deleted",
            EntityName = nameof(Doctor),
            EntityId = doctor.Id,
            PayloadJson = JsonSerializer.Serialize(new { doctor.Name })
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DoctorResponse> CreateAsync(CreateDoctorRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var doctor = new Doctor
        {
            ClinicId = clinicId,
            Name = request.Name,
            Crm = request.Crm,
            Phone = request.Phone,
            Email = request.Email
        };

        dbContext.Doctors.Add(doctor);

        if (request.SpecialtyIds?.Count > 0)
        {
            var specialties = await dbContext.Specialties
                .Where(x => request.SpecialtyIds.Contains(x.Id) && x.ClinicId == clinicId && x.DeletedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var s in specialties)
            {
                doctor.DoctorSpecialties.Add(new DoctorSpecialty
                {
                    ClinicId = clinicId,
                    DoctorId = doctor.Id,
                    SpecialtyId = s.Id
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var existing = await dbContext.Users.IgnoreQueryFilters().AnyAsync(x => x.Email == request.Email, cancellationToken);
            if (!existing)
            {
                dbContext.Users.Add(new User
                {
                    ClinicId = clinicId,
                    Name = request.Name,
                    Email = request.Email,
                    PasswordHash = passwordHasher.Hash("mude2026"),
                    Role = UserRole.Doctor
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(doctor);
    }

    public async Task<DoctorResponse> UpdateAsync(Guid doctorId, UpdateDoctorRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var doctor = await dbContext.Doctors
            .Include(x => x.DoctorSpecialties)
            .FirstOrDefaultAsync(x => x.Id == doctorId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Medico nao encontrado.");

        doctor.Name = request.Name;
        doctor.Phone = request.Phone;
        doctor.Email = request.Email;
        doctor.IsActive = request.IsActive;
        doctor.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.SpecialtyIds is not null)
        {
            var existingIds = doctor.DoctorSpecialties.Select(x => x.SpecialtyId).ToHashSet();
            var requested = request.SpecialtyIds.ToHashSet();

            var toRemove = doctor.DoctorSpecialties.Where(x => !requested.Contains(x.SpecialtyId)).ToList();
            foreach (var r in toRemove)
                dbContext.DoctorSpecialties.Remove(r);

            var toAdd = requested.Where(id => !existingIds.Contains(id)).ToList();
            if (toAdd.Count > 0)
            {
                var specialties = await dbContext.Specialties
                    .Where(x => toAdd.Contains(x.Id) && x.ClinicId == clinicId && x.DeletedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var s in specialties)
                {
                    doctor.DoctorSpecialties.Add(new DoctorSpecialty
                    {
                        ClinicId = clinicId,
                        DoctorId = doctor.Id,
                        SpecialtyId = s.Id
                    });
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(doctor);
    }

    private static DoctorResponse ToResponse(Doctor doctor)
    {
        var specialties = doctor.DoctorSpecialties?
            .Where(ds => ds.Specialty != null)
            .Select(ds => new SpecialtyItem(ds.Specialty.Id, ds.Specialty.Name))
            .ToList() ?? [];

        return new DoctorResponse(doctor.Id, doctor.Name, doctor.Crm, doctor.Phone, doctor.Email, doctor.IsActive, specialties);
    }
}

public sealed class AppointmentService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IOutboxService outboxService)
{
    public async Task<PagedResult<AppointmentResponse>> ListAsync(AppointmentQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var appointmentsQuery = dbContext.Appointments.AsNoTracking()
            .Where(x => x.ClinicId == clinicId && x.DeletedAt == null);

        if (query.Date is not null)
        {
            var bounds = await GetClinicDayBoundsAsync(clinicId, query.Date.Value, cancellationToken);
            appointmentsQuery = appointmentsQuery.Where(x => x.StartAt >= bounds.StartUtc && x.StartAt < bounds.EndUtc);
        }
        else if (query.DateFrom is not null)
        {
            var fromBounds = await GetClinicDayBoundsAsync(clinicId, query.DateFrom.Value, cancellationToken);
            appointmentsQuery = appointmentsQuery.Where(x => x.StartAt >= fromBounds.StartUtc);
            if (query.DateTo is not null)
            {
                var toBounds = await GetClinicDayBoundsAsync(clinicId, query.DateTo.Value, cancellationToken);
                appointmentsQuery = appointmentsQuery.Where(x => x.StartAt < toBounds.EndUtc);
            }
        }

        if (query.DoctorId is not null)
        {
            appointmentsQuery = appointmentsQuery.Where(x => x.DoctorId == query.DoctorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<AppointmentStatus>(query.Status, ignoreCase: true, out var parsedStatus))
        {
            appointmentsQuery = appointmentsQuery.Where(x => x.Status == parsedStatus);
        }

        var total = await appointmentsQuery.CountAsync(cancellationToken);
        var appointments = await appointmentsQuery
            .Include(x => x.Patient)
            .Include(x => x.Doctor)
            .ThenInclude(x => x.DoctorSpecialties)
            .ThenInclude(x => x.Specialty)
            .OrderBy(x => x.StartAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = appointments.Select(x => ToResponse(x, x.Patient, x.Doctor)).ToList();

        return new PagedResult<AppointmentResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<AppointmentResponse> CreateAsync(CreateAppointmentRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var clinic = await dbContext.Clinics.FirstOrDefaultAsync(x => x.Id == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Clinica nao encontrada.");

        var patient = await dbContext.Patients.FirstOrDefaultAsync(x => x.Id == request.PatientId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken);
        var doctor = await dbContext.Doctors.FirstOrDefaultAsync(x => x.Id == request.DoctorId && x.ClinicId == clinicId && x.DeletedAt == null && x.IsActive, cancellationToken);

        if (patient is null || doctor is null)
        {
            throw new InvalidOperationException("Paciente ou medico invalido.");
        }

        var endAt = request.StartAt.AddMinutes(request.DurationMinutes == 0 ? 30 : request.DurationMinutes);
        ValidateBusinessHours(clinic, request.StartAt, endAt);

        var conflict = await dbContext.Appointments.AnyAsync(x =>
            x.ClinicId == clinicId &&
            x.DoctorId == request.DoctorId &&
            x.DeletedAt == null &&
            x.Status != AppointmentStatus.Cancelled &&
            request.StartAt < x.EndAt &&
            endAt > x.StartAt, cancellationToken);

        if (conflict)
        {
            throw new InvalidOperationException("Conflito de horario para o medico selecionado.");
        }

        var appointment = new Appointment
        {
            ClinicId = clinicId,
            PatientId = request.PatientId,
            DoctorId = request.DoctorId,
            StartAt = request.StartAt,
            EndAt = endAt,
            Notes = request.Notes,
            Type = request.Type,
            Amount = request.Amount
        };

        dbContext.Appointments.Add(appointment);

        if (request.Amount > 0)
        {
            dbContext.Receivables.Add(new Receivable
            {
                ClinicId = clinicId,
                Appointment = appointment,
                AppointmentId = appointment.Id,
                OriginalAmount = request.Amount,
                ReceivedAmount = 0,
                Status = ReceivableStatus.Pending,
                DueDate = request.StartAt,
                Description = $"Consulta {request.Type}"
            });
        }

        await outboxService.EnqueueAsync(clinicId, "appointment.created", new
        {
            appointment.Id,
            appointment.PatientId,
            appointment.DoctorId,
            appointment.StartAt
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(appointment, patient, doctor);
    }

    public async Task<AppointmentResponse> UpdateAsync(Guid appointmentId, UpdateAppointmentRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var appointment = await dbContext.Appointments
            .Include(x => x.Patient)
            .Include(x => x.Doctor)
            .ThenInclude(x => x.DoctorSpecialties)
            .ThenInclude(x => x.Specialty)
            .FirstOrDefaultAsync(x => x.Id == appointmentId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Consulta nao encontrada.");

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            throw new InvalidOperationException("Nao e possivel editar uma consulta cancelada.");
        }

        if (request.DoctorId.HasValue || request.StartAt.HasValue || request.DurationMinutes.HasValue)
        {
            var targetDoctorId = request.DoctorId ?? appointment.DoctorId;
            var targetStartAt = request.StartAt ?? appointment.StartAt;
            var targetDuration = request.DurationMinutes ?? (int)(appointment.EndAt - appointment.StartAt).TotalMinutes;
            var targetEndAt = targetStartAt.AddMinutes(targetDuration);

            if (targetDoctorId != appointment.DoctorId || targetStartAt != appointment.StartAt || targetDuration != (int)(appointment.EndAt - appointment.StartAt).TotalMinutes)
            {
                var clinic = await dbContext.Clinics.FirstOrDefaultAsync(x => x.Id == clinicId && x.DeletedAt == null, cancellationToken)
                    ?? throw new KeyNotFoundException("Clinica nao encontrada.");

                ValidateBusinessHours(clinic, targetStartAt, targetEndAt);

                var conflict = await dbContext.Appointments.AnyAsync(x =>
                    x.ClinicId == clinicId &&
                    x.DoctorId == targetDoctorId &&
                    x.Id != appointmentId &&
                    x.DeletedAt == null &&
                    x.Status != AppointmentStatus.Cancelled &&
                    targetStartAt < x.EndAt &&
                    targetEndAt > x.StartAt, cancellationToken);

                if (conflict)
                {
                    throw new InvalidOperationException("Conflito de horario para o medico selecionado.");
                }

                appointment.DoctorId = targetDoctorId;
                appointment.StartAt = targetStartAt;
                appointment.EndAt = targetEndAt;

                var receivableDue = await dbContext.Receivables
                    .FirstOrDefaultAsync(x => x.AppointmentId == appointmentId && x.DeletedAt == null, cancellationToken);
                if (receivableDue is not null)
                {
                    receivableDue.DueDate = targetStartAt;
                    receivableDue.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        if (request.Notes is not null)
            appointment.Notes = request.Notes;

        if (request.Type is not null)
            appointment.Type = request.Type;

        if (request.Amount.HasValue && request.Amount.Value != appointment.Amount)
        {
            var receivable = await dbContext.Receivables
                .FirstOrDefaultAsync(x => x.AppointmentId == appointmentId && x.DeletedAt == null, cancellationToken);

            if (receivable is not null)
            {
                var diff = request.Amount.Value - appointment.Amount;
                if (receivable.ReceivedAmount > 0 && receivable.OriginalAmount + diff < receivable.ReceivedAmount)
                {
                    throw new InvalidOperationException("Valor nao pode ser reduzido abaixo do valor ja recebido.");
                }
                receivable.OriginalAmount += diff;
                receivable.UpdatedAt = DateTimeOffset.UtcNow;
            }

            appointment.Amount = request.Amount.Value;
        }

        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        await outboxService.EnqueueAsync(clinicId, "appointment.updated", new
        {
            appointment.Id,
            appointment.PatientId,
            appointment.DoctorId,
            appointment.StartAt
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(appointment, appointment.Patient, appointment.Doctor);
    }

    public async Task<AppointmentResponse> ConfirmAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var (appointment, patient, doctor) = await FindAppointmentWithDetailsAsync(appointmentId, cancellationToken);
        appointment.ConfirmationStatus = ConfirmationStatus.Confirmed;
        appointment.Status = AppointmentStatus.Confirmed;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(appointment, patient, doctor);
    }

    public async Task<AppointmentResponse> CancelAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var (appointment, patient, doctor) = await FindAppointmentWithDetailsAsync(appointmentId, cancellationToken);
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        var receivable = await dbContext.Receivables
            .FirstOrDefaultAsync(x => x.AppointmentId == appointmentId && x.DeletedAt == null, cancellationToken);
        if (receivable is not null && receivable.Status != ReceivableStatus.Paid)
        {
            receivable.Status = ReceivableStatus.Cancelled;
            receivable.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await outboxService.EnqueueAsync(appointment.ClinicId, "appointment.cancelled", new
        {
            appointment.Id,
            appointment.PatientId,
            appointment.StartAt
        }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(appointment, patient, doctor);
    }

    public async Task<AppointmentResponse> MarkInProgressAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var (appointment, patient, doctor) = await FindAppointmentWithDetailsAsync(appointmentId, cancellationToken);
        appointment.Status = AppointmentStatus.InProgress;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(appointment, patient, doctor);
    }

    public async Task<AppointmentResponse> CompleteAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var (appointment, patient, doctor) = await FindAppointmentWithDetailsAsync(appointmentId, cancellationToken);
        appointment.Status = AppointmentStatus.Completed;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(appointment, patient, doctor);
    }

    public async Task<AppointmentResponse> MarkNoShowAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var (appointment, patient, doctor) = await FindAppointmentWithDetailsAsync(appointmentId, cancellationToken);
        appointment.Status = AppointmentStatus.NoShow;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(appointment, patient, doctor);
    }

    private async Task<(Appointment, Patient?, Doctor?)> FindAppointmentWithDetailsAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var appointment = await dbContext.Appointments
            .Include(x => x.Patient)
            .Include(x => x.Doctor)
            .ThenInclude(x => x.DoctorSpecialties)
            .ThenInclude(x => x.Specialty)
            .FirstOrDefaultAsync(x => x.Id == appointmentId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Consulta nao encontrada.");
        return (appointment, appointment.Patient, appointment.Doctor);
    }

    private async Task<(DateTimeOffset StartUtc, DateTimeOffset EndUtc)> GetClinicDayBoundsAsync(Guid clinicId, DateOnly date, CancellationToken cancellationToken)
    {
        var clinic = await dbContext.Clinics.FirstAsync(x => x.Id == clinicId, cancellationToken);
        var zone = AppHelpers.ResolveTimeZone(clinic.Timezone);
        var localStart = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);
        return (
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, zone)));
    }

    private static void ValidateBusinessHours(Clinic clinic, DateTimeOffset startAt, DateTimeOffset endAt)
    {
        var zone = AppHelpers.ResolveTimeZone(clinic.Timezone);
        var localStart = TimeZoneInfo.ConvertTime(startAt, zone);
        var localEnd = TimeZoneInfo.ConvertTime(endAt, zone);
        var hours = JsonSerializer.Deserialize<BusinessHoursWindow>(
                        clinic.BusinessHoursJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                    new BusinessHoursWindow("08:00", "18:00");
        var startOfBusiness = TimeOnly.Parse(hours.Start ?? "08:00");
        var endOfBusiness = TimeOnly.Parse(hours.End ?? "18:00");

        if (localStart.TimeOfDay < startOfBusiness.ToTimeSpan() || localEnd.TimeOfDay > endOfBusiness.ToTimeSpan())
        {
            throw new InvalidOperationException("Consulta fora do expediente da clinica.");
        }
    }

    private static AppointmentResponse ToResponse(Appointment appointment, Patient? patient = null, Doctor? doctor = null)
    {
        var specialties = doctor?.DoctorSpecialties?
            .Where(ds => ds.Specialty != null)
            .Select(ds => new SpecialtyItem(ds.Specialty.Id, ds.Specialty.Name))
            .ToList();

        return new(
            appointment.Id,
            appointment.PatientId,
            appointment.DoctorId,
            appointment.StartAt,
            appointment.EndAt,
            appointment.Status,
            appointment.ConfirmationStatus,
            appointment.Type,
            appointment.Amount,
            appointment.Notes,
            patient?.Name,
            patient?.Phone,
            doctor?.Name,
            specialties);
    }

    private sealed record BusinessHoursWindow(string? Start, string? End);
}

public sealed class FinancialService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
{
    public async Task<PagedResult<ReceivableResponse>> ListReceivablesAsync(FinancialQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var receivablesQuery = dbContext.Receivables.AsNoTracking()
            .Where(x => x.ClinicId == clinicId && x.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<ReceivableStatus>(query.Status, ignoreCase: true, out var parsedStatus))
        {
            receivablesQuery = receivablesQuery.Where(x => x.Status == parsedStatus);
        }

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            receivablesQuery = receivablesQuery.Where(x => x.DueDate >= from);
        }

        if (query.DateTo.HasValue)
        {
            var to = query.DateTo.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            receivablesQuery = receivablesQuery.Where(x => x.DueDate <= to);
        }

        var total = await receivablesQuery.CountAsync(cancellationToken);
        var items = await receivablesQuery
            .OrderByDescending(x => x.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new ReceivableResponse(
                x.Id,
                x.AppointmentId,
                x.OriginalAmount,
                x.ReceivedAmount,
                x.OriginalAmount - x.ReceivedAmount,
                x.Status,
                x.DueDate,
                x.Appointment != null ? x.Appointment.PatientId : null,
                x.Appointment != null && x.Appointment.Patient != null ? x.Appointment.Patient.Name : null))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReceivableResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<PagedResult<PaymentResponse>> ListPaymentsAsync(PaymentQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var paymentsQuery = dbContext.Payments.AsNoTracking()
            .Where(x => x.ClinicId == clinicId && x.DeletedAt == null);

        if (query.ReceivableId.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(x => x.ReceivableId == query.ReceivableId.Value);
        }

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            paymentsQuery = paymentsQuery.Where(x => x.PaidAt >= from);
        }

        if (query.DateTo.HasValue)
        {
            var to = query.DateTo.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            paymentsQuery = paymentsQuery.Where(x => x.PaidAt <= to);
        }

        var total = await paymentsQuery.CountAsync(cancellationToken);
        var items = await paymentsQuery
            .OrderByDescending(x => x.PaidAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new PaymentResponse(
                x.Id,
                x.ReceivableId,
                x.Amount,
                x.PaymentMethod,
                x.PaidAt,
                x.Status,
                x.Receivable != null && x.Receivable.Appointment != null && x.Receivable.Appointment.Patient != null ? x.Receivable.Appointment.Patient.Name : null))
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<ReceivableResponse> CreateManualReceivableAsync(CreateManualReceivableRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var receivable = new Receivable
        {
            ClinicId = clinicId,
            AppointmentId = null,
            OriginalAmount = request.Amount,
            ReceivedAmount = request.Amount,
            Status = ReceivableStatus.Paid,
            DueDate = request.DueDate ?? DateTimeOffset.UtcNow,
            Description = request.Description
        };

        dbContext.Receivables.Add(receivable);

        var payment = new Payment
        {
            ClinicId = clinicId,
            ReceivableId = receivable.Id,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            PaidAt = request.PaidAt ?? DateTimeOffset.UtcNow,
            Notes = request.Notes
        };

        dbContext.Payments.Add(payment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReceivableResponse(
            receivable.Id,
            receivable.AppointmentId,
            receivable.OriginalAmount,
            receivable.ReceivedAmount,
            receivable.OriginalAmount - receivable.ReceivedAmount,
            receivable.Status,
            receivable.DueDate);
    }

    public async Task<PaymentResponse> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var receivable = await dbContext.Receivables
            .FirstOrDefaultAsync(x => x.Id == request.ReceivableId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Conta a receber nao encontrada.");

        var newReceivedAmount = receivable.ReceivedAmount + request.Amount;
        if (newReceivedAmount > receivable.OriginalAmount)
        {
            throw new InvalidOperationException("Pagamento excede o saldo em aberto.");
        }

        receivable.ReceivedAmount = newReceivedAmount;
        receivable.Status = newReceivedAmount == receivable.OriginalAmount ? ReceivableStatus.Paid : ReceivableStatus.Partial;
        receivable.UpdatedAt = DateTimeOffset.UtcNow;

        var payment = new Payment
        {
            ClinicId = clinicId,
            ReceivableId = receivable.Id,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            PaidAt = request.PaidAt ?? DateTimeOffset.UtcNow,
            Notes = request.Notes
        };

        dbContext.Payments.Add(payment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PaymentResponse(payment.Id, payment.ReceivableId, payment.Amount, payment.PaymentMethod, payment.PaidAt, payment.Status);
    }
}

public sealed class ExpenseService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
{
    public async Task<PagedResult<ExpenseResponse>> ListAsync(ExpenseQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var queryable = dbContext.Expenses.AsNoTracking()
            .Where(x => x.ClinicId == clinicId && x.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(query.Category) &&
            Enum.TryParse<ExpenseCategory>(query.Category, ignoreCase: true, out var parsedCategory))
            queryable = queryable.Where(x => x.Category == parsedCategory);

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<ExpenseStatus>(query.Status, ignoreCase: true, out var parsedStatus))
            queryable = queryable.Where(x => x.Status == parsedStatus);

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            queryable = queryable.Where(x => x.PaidAt >= from);
        }
        if (query.DateTo.HasValue)
        {
            var to = query.DateTo.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            queryable = queryable.Where(x => x.PaidAt <= to);
        }

        var total = await queryable.CountAsync(cancellationToken);
        var items = await queryable
            .OrderByDescending(x => x.PaidAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new ExpenseResponse(x.Id, x.Description, x.Amount, x.Category, x.PaymentMethod, x.PaidAt, x.Status, x.Notes))
            .ToListAsync(cancellationToken);

        return new PagedResult<ExpenseResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<ExpenseResponse> CreateAsync(ExpenseRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var expense = Map(request, clinicId);
        dbContext.Expenses.Add(expense);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(expense);
    }

    public async Task<ExpenseResponse> UpdateAsync(Guid id, ExpenseRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var expense = await dbContext.Expenses
            .FirstOrDefaultAsync(x => x.Id == id && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Despesa nao encontrada.");

        Map(request, expense);
        expense.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(expense);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var expense = await dbContext.Expenses
            .FirstOrDefaultAsync(x => x.Id == id && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Despesa nao encontrada.");

        expense.DeletedAt = DateTimeOffset.UtcNow;
        expense.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ponytail: month-boundary calc duplicates DashboardService; 5 lines, not worth extracting
    public async Task<FinancialSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var clinic = await dbContext.Clinics.FirstAsync(x => x.Id == clinicId, cancellationToken);
        var zone = AppHelpers.ResolveTimeZone(clinic.Timezone);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var monthStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), zone));
        var monthEnd = monthStart.AddMonths(1);

        var totalReceived = await dbContext.Payments
            .Where(x => x.ClinicId == clinicId && x.PaidAt >= monthStart && x.PaidAt < monthEnd && x.DeletedAt == null)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var totalExpenses = await dbContext.Expenses
            .Where(x => x.ClinicId == clinicId && x.PaidAt >= monthStart && x.PaidAt < monthEnd && x.DeletedAt == null && x.Status == ExpenseStatus.Paid)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        return new FinancialSummaryResponse(totalReceived, totalExpenses, totalReceived - totalExpenses);
    }

    private static ExpenseResponse ToResponse(Expense e) =>
        new(e.Id, e.Description, e.Amount, e.Category, e.PaymentMethod, e.PaidAt, e.Status, e.Notes);

    private static Expense Map(ExpenseRequest request, Guid clinicId) => new()
    {
        ClinicId = clinicId,
        Description = request.Description,
        Amount = request.Amount,
        Category = request.Category,
        PaymentMethod = request.PaymentMethod,
        PaidAt = request.PaidAt ?? DateTimeOffset.UtcNow,
        Status = request.Status ?? ExpenseStatus.Paid,
        Notes = request.Notes
    };

    private static void Map(ExpenseRequest request, Expense expense)
    {
        expense.Description = request.Description;
        expense.Amount = request.Amount;
        expense.Category = request.Category;
        expense.PaymentMethod = request.PaymentMethod;
        expense.PaidAt = request.PaidAt ?? expense.PaidAt;
        expense.Status = request.Status ?? expense.Status;
        expense.Notes = request.Notes;
    }
}

public sealed class DashboardService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
{
    public async Task<DashboardSummaryResponse> GetSummaryAsync(Guid? doctorId = null, CancellationToken cancellationToken = default)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var clinic = await dbContext.Clinics.FirstAsync(x => x.Id == clinicId, cancellationToken);
        var zone = AppHelpers.ResolveTimeZone(clinic.Timezone);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var localDayStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localDayStart, zone));
        var dayEnd = dayStart.AddDays(1);
        var monthStartLocal = new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var monthStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(monthStartLocal, zone));
        var monthEnd = monthStart.AddMonths(1);

        var appointmentsToday = await dbContext.Appointments.CountAsync(x => x.ClinicId == clinicId && x.StartAt >= dayStart && x.StartAt < dayEnd && x.DeletedAt == null
            && (!doctorId.HasValue || x.DoctorId == doctorId.Value), cancellationToken);
        var confirmedToday = await dbContext.Appointments.CountAsync(x => x.ClinicId == clinicId && x.StartAt >= dayStart && x.StartAt < dayEnd && x.ConfirmationStatus == ConfirmationStatus.Confirmed && x.DeletedAt == null
            && (!doctorId.HasValue || x.DoctorId == doctorId.Value), cancellationToken);
        var cancelledToday = await dbContext.Appointments.CountAsync(x => x.ClinicId == clinicId && x.StartAt >= dayStart && x.StartAt < dayEnd && x.Status == AppointmentStatus.Cancelled && x.DeletedAt == null
            && (!doctorId.HasValue || x.DoctorId == doctorId.Value), cancellationToken);
        var noShowCount = await dbContext.Appointments.CountAsync(x => x.ClinicId == clinicId && x.StartAt >= monthStart && x.StartAt < monthEnd && x.Status == AppointmentStatus.NoShow && x.DeletedAt == null
            && (!doctorId.HasValue || x.DoctorId == doctorId.Value), cancellationToken);
        var monthlyAppointments = await dbContext.Appointments.CountAsync(x => x.ClinicId == clinicId && x.StartAt >= monthStart && x.StartAt < monthEnd && x.DeletedAt == null
            && (!doctorId.HasValue || x.DoctorId == doctorId.Value), cancellationToken);
        var monthlyRevenue = await dbContext.Payments.Where(x => x.ClinicId == clinicId && x.PaidAt >= monthStart && x.PaidAt < monthEnd && x.DeletedAt == null)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var monthlyExpenses = await dbContext.Expenses.Where(x => x.ClinicId == clinicId && x.PaidAt >= monthStart && x.PaidAt < monthEnd && x.DeletedAt == null && x.Status == ExpenseStatus.Paid)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var confirmationRate = appointmentsToday == 0 ? 0d : confirmedToday / (double)appointmentsToday;
        var noShowRate = monthlyAppointments == 0 ? 0d : noShowCount / (double)monthlyAppointments;

        return new DashboardSummaryResponse(appointmentsToday, confirmedToday, cancelledToday, monthlyRevenue, noShowRate, confirmationRate, monthlyExpenses, monthlyRevenue - monthlyExpenses);
    }

}

public sealed class WhatsAppWebhookService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IOutboxService outboxService)
{
    public async Task ProcessAsync(WhatsAppWebhookRequest request, CancellationToken cancellationToken)
    {
        dbContext.WebhookEvents.Add(new WebhookEvent
        {
            ClinicId = request.ClinicId ?? tenantProvider.ClinicId,
            PayloadJson = JsonSerializer.Serialize(request),
            Processed = false
        });

        dbContext.WhatsAppMessages.Add(new WhatsAppMessage
        {
            ClinicId = request.ClinicId ?? tenantProvider.ClinicId,
            Phone = request.Phone,
            Message = request.Message,
            Direction = MessageDirection.Inbound,
            Status = WhatsAppMessageStatus.Delivered,
            ProviderMessageId = request.ProviderMessageId
        });

        if (request.AppointmentId.HasValue)
        {
            var appointment = await dbContext.Appointments.FirstOrDefaultAsync(x => x.Id == request.AppointmentId.Value && x.DeletedAt == null, cancellationToken);
            if (appointment is not null)
            {
                var normalizedMessage = request.Message.Trim().ToUpperInvariant();
                if (normalizedMessage.Contains("CONFIRM"))
                {
                    appointment.ConfirmationStatus = ConfirmationStatus.Confirmed;
                    appointment.Status = AppointmentStatus.Confirmed;
                }
                else if (normalizedMessage.Contains("CANCEL"))
                {
                    appointment.Status = AppointmentStatus.Cancelled;
                    appointment.ConfirmationStatus = ConfirmationStatus.Declined;
                }

                await outboxService.EnqueueAsync(appointment.ClinicId, "whatsapp.webhook.processed", new
                {
                    appointment.Id,
                    request.Message
                }, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class PatientPortalService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IJwtTokenService jwtTokenService)
{
    public async Task<PatientPortalAuthResponse> LoginAsync(PatientPortalLoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedCpf = AppHelpers.NormalizeDigits(request.Cpf);
        if (!Guid.TryParse(request.AccessToken, out var tokenGuid))
        {
            throw new UnauthorizedAccessException("Credenciais invalidas.");
        }

        var patient = await dbContext.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Cpf == normalizedCpf && x.PatientAccessToken == tokenGuid && x.DeletedAt == null, cancellationToken)
            ?? throw new UnauthorizedAccessException("CPF ou token invalido.");

        var accessToken = jwtTokenService.GeneratePatientToken(patient);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var profile = ToProfile(patient);
        return new PatientPortalAuthResponse(accessToken, expiresAt, profile);
    }

    public async Task<PatientPortalProfileResponse> GetProfileAsync(CancellationToken cancellationToken)
    {
        var patientId = RequirePatientId();
        var patient = await dbContext.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == patientId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Paciente nao encontrado.");
        return ToProfile(patient);
    }

    public async Task<IReadOnlyList<PatientPortalAppointmentResponse>> GetAppointmentsAsync(CancellationToken cancellationToken)
    {
        var patientId = RequirePatientId();
        return await dbContext.Appointments
            .AsNoTracking()
            .Include(x => x.Doctor).ThenInclude(x => x.DoctorSpecialties).ThenInclude(x => x.Specialty)
            .Where(x => x.PatientId == patientId && x.DeletedAt == null)
            .OrderByDescending(x => x.StartAt)
            .Select(x => new PatientPortalAppointmentResponse(
                x.Id,
                x.Doctor != null ? x.Doctor.Name : "Medico",
                x.Doctor != null && x.Doctor.DoctorSpecialties.Any()
                    ? x.Doctor.DoctorSpecialties.First().Specialty.Name
                    : "",
                x.StartAt,
                x.EndAt,
                x.Status,
                x.Type,
                x.Notes,
                x.Amount))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PatientPortalReceivableResponse>> GetReceivablesAsync(CancellationToken cancellationToken)
    {
        var patientId = RequirePatientId();
        return await dbContext.Receivables
            .AsNoTracking()
            .Where(x => x.DeletedAt == null)
            .Join(dbContext.Appointments.Where(a => a.PatientId == patientId && a.DeletedAt == null),
                r => r.AppointmentId,
                a => a.Id,
                (r, _) => new PatientPortalReceivableResponse(
                    r.Id,
                    r.OriginalAmount,
                    r.ReceivedAmount,
                    r.OriginalAmount - r.ReceivedAmount,
                    r.Status,
                    r.DueDate))
            .OrderByDescending(x => x.DueDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PatientDocumentResponse>> GetDocumentsAsync(CancellationToken cancellationToken)
    {
        var patientId = RequirePatientId();
        return await dbContext.PatientDocuments
            .AsNoTracking()
            .Where(x => x.PatientId == patientId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PatientDocumentResponse(x.Id, x.FileName, x.ContentType, x.SizeInBytes, x.StoragePath))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> RegenerateAccessTokenAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var patient = await dbContext.Patients
            .FirstOrDefaultAsync(x => x.Id == patientId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Paciente nao encontrado.");
        patient.PatientAccessToken = Guid.NewGuid();
        patient.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return patient.PatientAccessToken;
    }

    private Guid RequirePatientId() =>
        tenantProvider.UserId ?? throw new UnauthorizedAccessException("Sessao de paciente invalida.");

    private static PatientPortalProfileResponse ToProfile(Patient patient) =>
        new(patient.Id, patient.Name, patient.Cpf, patient.BirthDate, patient.Phone, patient.Email, patient.HealthInsurance);
}

// ── HealthInsurance ──

public sealed class HealthInsuranceService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
{
    public async Task<PagedResult<HealthInsuranceResponse>> ListAsync(HealthInsuranceQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var queryable = dbContext.HealthInsurances.AsNoTracking()
            .Where(x => x.ClinicId == clinicId && x.DeletedAt == null);

        var search = query.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(search))
            queryable = queryable.Where(x => x.Name.ToLower().Contains(search));

        var total = await queryable.CountAsync(cancellationToken);
        var items = await queryable
            .OrderBy(x => x.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new HealthInsuranceResponse(x.Id, x.Name, x.Phone, x.ContactName))
            .ToListAsync(cancellationToken);

        return new PagedResult<HealthInsuranceResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<HealthInsuranceResponse> CreateAsync(CreateHealthInsuranceRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var exists = await dbContext.HealthInsurances.AnyAsync(
            x => x.ClinicId == clinicId && x.Name == request.Name && x.DeletedAt == null, cancellationToken);
        if (exists) throw new InvalidOperationException("Convenio ja cadastrado.");

        var hi = new HealthInsurance
        {
            ClinicId = clinicId,
            Name = request.Name,
            Phone = request.Phone,
            ContactName = request.ContactName
        };
        dbContext.HealthInsurances.Add(hi);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new HealthInsuranceResponse(hi.Id, hi.Name, hi.Phone, hi.ContactName);
    }

    public async Task<HealthInsuranceResponse> UpdateAsync(Guid id, UpdateHealthInsuranceRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var hi = await dbContext.HealthInsurances
            .FirstOrDefaultAsync(x => x.Id == id && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Convenio nao encontrado.");

        hi.Name = request.Name;
        hi.Phone = request.Phone;
        hi.ContactName = request.ContactName;
        hi.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return new HealthInsuranceResponse(hi.Id, hi.Name, hi.Phone, hi.ContactName);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var hi = await dbContext.HealthInsurances
            .FirstOrDefaultAsync(x => x.Id == id && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Convenio nao encontrado.");

        hi.DeletedAt = DateTimeOffset.UtcNow;
        hi.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

// ── Specialty ──

public sealed class SpecialtyService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
{
    public async Task<PagedResult<SpecialtyResponse>> ListAsync(SpecialtyQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var queryable = dbContext.Specialties.AsNoTracking()
            .Where(x => x.ClinicId == clinicId && x.DeletedAt == null);

        var search = query.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(search))
            queryable = queryable.Where(x => x.Name.ToLower().Contains(search));

        var total = await queryable.CountAsync(cancellationToken);
        var items = await queryable
            .OrderBy(x => x.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new SpecialtyResponse(
                s.Id, s.Name,
                s.DoctorSpecialties
                    .Where(ds => ds.Doctor != null)
                    .Select(ds => new SpecialtyDoctorItem(ds.Doctor.Id, ds.Doctor.Name, ds.Doctor.Crm))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new PagedResult<SpecialtyResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<SpecialtyResponse> CreateAsync(CreateSpecialtyRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var exists = await dbContext.Specialties.AnyAsync(
            x => x.ClinicId == clinicId && x.Name == request.Name && x.DeletedAt == null, cancellationToken);
        if (exists) throw new InvalidOperationException("Especialidade ja cadastrada.");

        var specialty = new Specialty { ClinicId = clinicId, Name = request.Name };
        dbContext.Specialties.Add(specialty);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SpecialtyResponse(specialty.Id, specialty.Name, []);
    }

    public async Task<SpecialtyResponse> UpdateAsync(Guid id, UpdateSpecialtyRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var specialty = await dbContext.Specialties
            .Include(x => x.DoctorSpecialties).ThenInclude(x => x.Doctor)
            .FirstOrDefaultAsync(x => x.Id == id && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Especialidade nao encontrada.");

        specialty.Name = request.Name;
        specialty.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var doctors = specialty.DoctorSpecialties?
            .Where(ds => ds.Doctor != null)
            .Select(ds => new SpecialtyDoctorItem(ds.Doctor.Id, ds.Doctor.Name, ds.Doctor.Crm))
            .ToList() ?? [];

        return new SpecialtyResponse(specialty.Id, specialty.Name, doctors);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var specialty = await dbContext.Specialties
            .FirstOrDefaultAsync(x => x.Id == id && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Especialidade nao encontrada.");

        specialty.DeletedAt = DateTimeOffset.UtcNow;
        specialty.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

// ── DoctorAvailability ──

public sealed class DoctorAvailabilityService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
{
    public async Task<PagedResult<DoctorAvailabilityResponse>> ListAsync(AvailabilityQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var queryable = dbContext.DoctorAvailabilities.AsNoTracking()
            .Include(x => x.Doctor)
            .Where(x => x.ClinicId == clinicId && x.DeletedAt == null);

        if (query.DoctorId.HasValue)
            queryable = queryable.Where(x => x.DoctorId == query.DoctorId.Value);

        var total = await queryable.CountAsync(cancellationToken);
        var items = await queryable
            .OrderBy(x => x.Doctor.Name).ThenBy(x => x.DayOfWeek).ThenBy(x => x.StartTime)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new DoctorAvailabilityResponse(
                x.Id, x.DoctorId, x.Doctor.Name,
                (int)x.DayOfWeek,
                x.StartTime.ToString(@"hh\:mm"),
                x.EndTime.ToString(@"hh\:mm"),
                x.IsAvailable))
            .ToListAsync(cancellationToken);

        return new PagedResult<DoctorAvailabilityResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<DoctorAvailabilityResponse> CreateAsync(CreateAvailabilityRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);

        var doctor = await dbContext.Doctors
            .FirstOrDefaultAsync(x => x.Id == request.DoctorId && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Medico nao encontrado.");

        var start = TimeSpan.Parse(request.StartTime);
        var end = TimeSpan.Parse(request.EndTime);

        var overlap = await dbContext.DoctorAvailabilities.AnyAsync(
            x => x.DoctorId == request.DoctorId && x.DayOfWeek == (DayOfWeek)request.DayOfWeek
              && x.DeletedAt == null && x.StartTime < end && x.EndTime > start, cancellationToken);
        if (overlap)
            throw new InvalidOperationException("Horario conflitante com outro periodo ja cadastrado.");

        var av = new DoctorAvailability
        {
            ClinicId = clinicId,
            DoctorId = request.DoctorId,
            DayOfWeek = (DayOfWeek)request.DayOfWeek,
            StartTime = start,
            EndTime = end,
            IsAvailable = request.IsAvailable
        };

        dbContext.DoctorAvailabilities.Add(av);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DoctorAvailabilityResponse(av.Id, av.DoctorId, doctor.Name, request.DayOfWeek, request.StartTime, request.EndTime, av.IsAvailable);
    }

    public async Task<DoctorAvailabilityResponse> UpdateAsync(Guid id, UpdateAvailabilityRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var av = await dbContext.DoctorAvailabilities
            .Include(x => x.Doctor)
            .FirstOrDefaultAsync(x => x.Id == id && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Disponibilidade nao encontrada.");

        var start = TimeSpan.Parse(request.StartTime);
        var end = TimeSpan.Parse(request.EndTime);

        av.DayOfWeek = (DayOfWeek)request.DayOfWeek;
        av.StartTime = start;
        av.EndTime = end;
        av.IsAvailable = request.IsAvailable;
        av.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return new DoctorAvailabilityResponse(av.Id, av.DoctorId, av.Doctor.Name, request.DayOfWeek, request.StartTime, request.EndTime, av.IsAvailable);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var av = await dbContext.DoctorAvailabilities
            .FirstOrDefaultAsync(x => x.Id == id && x.ClinicId == clinicId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("Disponibilidade nao encontrada.");

        av.DeletedAt = DateTimeOffset.UtcNow;
        av.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

internal static class AppHelpers
{
    internal static string NormalizeDigits(string value) => new(value.Where(char.IsDigit).ToArray());

    internal static bool ValidateCpf(string cpf)
    {
        var digits = NormalizeDigits(cpf);
        if (digits.Length != 11) return false;
        if (digits.All(c => c == digits[0])) return false;

        var sum1 = 0;
        for (var i = 0; i < 9; i++)
            sum1 += (digits[i] - '0') * (10 - i);
        var rest1 = sum1 % 11;
        var dig1 = rest1 < 2 ? 0 : 11 - rest1;
        if (digits[9] - '0' != dig1) return false;

        var sum2 = 0;
        for (var i = 0; i < 10; i++)
            sum2 += (digits[i] - '0') * (11 - i);
        var rest2 = sum2 % 11;
        var dig2 = rest2 < 2 ? 0 : 11 - rest2;
        return digits[10] - '0' == dig2;
    }

    internal static TimeZoneInfo ResolveTimeZone(string timezone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }

    internal static IOrderedQueryable<T> OrderByKey<T, TKey>(IQueryable<T> query, bool desc, Expression<Func<T, TKey>> keySelector)
        => desc ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);
}

internal static class TenantGuard
{
    internal static Guid RequireClinicId(ITenantProvider tenantProvider) =>
        tenantProvider.ClinicId ?? throw new InvalidOperationException("Contexto de clinica nao informado.");
}
