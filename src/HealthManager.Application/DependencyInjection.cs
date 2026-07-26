using HealthManager.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HealthManager.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<ClinicProvisioningService>();
        services.AddScoped<TenantSettingsService>();
        services.AddScoped<PatientService>();
        services.AddScoped<DoctorService>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<FinancialService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<ExpenseCategoryService>();
        services.AddScoped<AppointmentTypeService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<WhatsAppWebhookService>();
        services.AddScoped<PatientPortalService>();
        services.AddScoped<HealthInsuranceService>();
        services.AddScoped<SpecialtyService>();
        services.AddScoped<DoctorAvailabilityService>();
        services.AddScoped<ClinicalRecordService>();
        services.AddScoped<PaymentIntentService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<TenantIntegrationService>();
        services.AddScoped<WhatsAppMessagingService>();

        services.AddSingleton<Dictionary<PaymentGatewayProvider, IPaymentGatewayHandler>>(_ => new()
        {
            [PaymentGatewayProvider.Asaas] = new AsaasGatewayHandler(),
            [PaymentGatewayProvider.MercadoPago] = new MercadoPagoGatewayHandler(),
            [PaymentGatewayProvider.Stripe] = new StripeGatewayHandler()
        });

        services.AddSingleton<IPaymentGatewayClient, MockPaymentGatewayClient>();

        return services;
    }
}
