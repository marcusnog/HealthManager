namespace HealthManager.Application;

public sealed class ForbiddenAccessException(string message = "Acesso negado.") : Exception(message);
