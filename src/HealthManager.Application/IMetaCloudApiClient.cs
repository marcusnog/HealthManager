namespace HealthManager.Application;

public sealed class MetaMessageResponse
{
    public bool Success { get; init; }
    public string? MessageId { get; init; }
    public string? Error { get; init; }
}

public interface IMetaCloudApiClient
{
    Task<MetaMessageResponse> SendTextAsync(string phoneNumberId, string accessToken, string to, string text, CancellationToken cancellationToken);
    Task<MetaMessageResponse> SendTemplateAsync(string phoneNumberId, string accessToken, string to, string templateName, string languageCode, Dictionary<string, string>? parameters, CancellationToken cancellationToken);
}
