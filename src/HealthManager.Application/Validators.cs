using FluentValidation;
using HealthManager.Domain;

namespace HealthManager.Application;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class CreateClinicRequestValidator : AbstractValidator<CreateClinicRequest>
{
    public CreateClinicRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Slug).NotEmpty().Matches("^[a-z0-9-]+$");
        RuleFor(x => x.Timezone).NotEmpty();
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.AdminPassword).NotEmpty().MinimumLength(8);
    }
}

public sealed class CreateClinicUserRequestValidator : AbstractValidator<CreateClinicUserRequest>
{
    public CreateClinicUserRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class CreatePatientRequestValidator : AbstractValidator<CreatePatientRequest>
{
    public CreatePatientRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Cpf).NotEmpty().Length(11, 14);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20);
    }
}

public sealed class UpdatePatientRequestValidator : AbstractValidator<UpdatePatientRequest>
{
    public UpdatePatientRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty();
    }
}

public sealed class CreatePatientDocumentRequestValidator : AbstractValidator<CreatePatientDocumentRequest>
{
    public CreatePatientDocumentRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.ContentType).Must(x => x is "application/pdf" or "image/jpeg" or "image/png");
        RuleFor(x => x.SizeInBytes).GreaterThan(0).LessThanOrEqualTo(10 * 1024 * 1024);
    }
}

public sealed class CreateDoctorRequestValidator : AbstractValidator<CreateDoctorRequest>
{
    public CreateDoctorRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Specialty).NotEmpty();
        RuleFor(x => x.Crm).NotEmpty();
    }
}

public sealed class UpdateDoctorRequestValidator : AbstractValidator<UpdateDoctorRequest>
{
    public UpdateDoctorRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Specialty).NotEmpty();
    }
}

public sealed class CreateAppointmentRequestValidator : AbstractValidator<CreateAppointmentRequest>
{
    public CreateAppointmentRequestValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.DoctorId).NotEmpty();
        RuleFor(x => x.DurationMinutes).InclusiveBetween(15, 240);
        RuleFor(x => x.Type).NotEmpty();
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateAppointmentRequestValidator : AbstractValidator<UpdateAppointmentRequest>
{
    public UpdateAppointmentRequestValidator()
    {
        When(x => x.DurationMinutes.HasValue, () =>
        {
            RuleFor(x => x.DurationMinutes!.Value).InclusiveBetween(15, 240);
        });
        When(x => x.Amount.HasValue, () =>
        {
            RuleFor(x => x.Amount!.Value).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.ReceivableId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.PaymentMethod).IsInEnum();
    }
}

public sealed class WhatsAppWebhookRequestValidator : AbstractValidator<WhatsAppWebhookRequest>
{
    public WhatsAppWebhookRequestValidator()
    {
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Message).NotEmpty();
    }
}

