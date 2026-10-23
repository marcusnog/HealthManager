using System.Text.Json;

namespace HealthManager.Domain;

public static class ScreenAccess
{
    public static readonly string[] All = ["dashboard", "atendimento", "agenda", "tipos-consulta", "pacientes", "produtos-pacotes", "financeiro", "financeiro-pagar", "financeiro-repasses", "categorias-despesa", "medicos", "convenios", "especialidades", "agenda-medicos", "configuracoes", "usuarios", "clinical-settings"];

    public static string[] Available(UserRole role) => role switch
    {
        UserRole.Admin or UserRole.PlatformAdmin => All,
        UserRole.Secretary => All.Except(["financeiro-repasses", "configuracoes", "usuarios", "clinical-settings"]).ToArray(),
        UserRole.Doctor => ["dashboard", "agenda", "pacientes", "agenda-medicos", "financeiro", "clinical-settings"],
        _ => []
    };

    public static string[] Defaults(UserRole role) => role == UserRole.Secretary
        ? ["dashboard", "atendimento", "agenda", "pacientes", "produtos-pacotes", "financeiro", "financeiro-pagar", "categorias-despesa"]
        : Available(role);

    public static string[] ForUser(User user) => user.Role is UserRole.Admin or UserRole.PlatformAdmin
        ? All
        : user.ScreenAccessJson is null ? Defaults(user.Role)
        : (JsonSerializer.Deserialize<string[]>(user.ScreenAccessJson) ?? []).Intersect(Available(user.Role)).ToArray();
}
