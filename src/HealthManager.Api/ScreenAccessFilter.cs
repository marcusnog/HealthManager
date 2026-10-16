using HealthManager.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HealthManager.Api;

// Screen grants restrict existing action policies; shared read lookups support their consuming screens.
public sealed class ScreenAccessFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (!user.HasClaim(Permissions.ClaimType, "screen.restricted") ||
            context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;
        var request = context.HttpContext.Request;
        var path = request.Path.Value!.Trim('/').ToLowerInvariant();
        var root = path.Split('/')[0];
        var read = HttpMethods.IsGet(request.Method);
        string[] screens = root switch
        {
            "auth" => [],
            "dashboard" => ["dashboard"],
            "appointments" => ["agenda", "dashboard"],
            "patients" when read && !path.Contains('/') => ["pacientes", "agenda", "dashboard", "financeiro"],
            "patients" => ["pacientes"],
            "doctors" when read => ["medicos", "agenda", "dashboard", "agenda-medicos", "especialidades", "financeiro-repasses"],
            "doctors" => ["medicos"],
            "appointment-types" when read => ["tipos-consulta", "agenda", "dashboard"],
            "appointment-types" => ["tipos-consulta"],
            "doctor-availabilities" => ["agenda-medicos"],
            "health-insurances" when read => ["convenios", "pacientes"],
            "health-insurances" => ["convenios"],
            "specialties" when read => ["especialidades", "medicos"],
            "specialties" => ["especialidades"],
            "products" or "packages" when read => ["produtos-pacotes", "pacientes"],
            "products" or "packages" => ["produtos-pacotes"],
            "address" => ["pacientes"],
            "expense-categories" when read => ["categorias-despesa", "financeiro-pagar"],
            "expense-categories" => ["categorias-despesa"],
            "financial" when path.StartsWith("financial/expenses") => ["financeiro-pagar"],
            "financial" when path.Contains("settlements") => ["financeiro-repasses"],
            "financial" or "checkout" or "payment-intents" => ["financeiro"],
            "whatsapp" => ["atendimento"],
            "tenant" when read && path == "tenant/settings" => [],
            "tenant" => ["configuracoes"],
            "users" => ["usuarios"],
            _ => ["unavailable"]
        };
        if (screens.Length > 0 && !screens.Any(screen => user.HasClaim(Permissions.ClaimType, "screen." + screen)))
            context.Result = new ForbidResult();
    }
}
