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
        services.AddScoped<ExpenseCategoryService>();
        services.AddScoped<AppointmentTypeService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<WhatsAppWebhookService>();
        services.AddScoped<PatientPortalService>();
        services.AddScoped<HealthInsuranceService>();
        services.AddScoped<SpecialtyService>();
        services.AddScoped<DoctorAvailabilityService>();
        return services;
    }
}
