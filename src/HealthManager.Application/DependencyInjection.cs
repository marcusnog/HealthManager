using Microsoft.Extensions.DependencyInjection;

namespace HealthManager.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<ClinicProvisioningService>();
        services.AddScoped<PatientService>();
        services.AddScoped<DoctorService>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<FinancialService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<WhatsAppWebhookService>();
        services.AddScoped<PatientPortalService>();
        return services;
    }
}
