using System.Net.Http.Json;
using System.Text.Json;
using HealthManager.Application;

namespace HealthManager.Infrastructure.Integrations;

public sealed class MetaCloudApiClient(IHttpClientFactory httpClientFactory) : IMetaCloudApiClient
{
    private const string GraphApiBase = "https://graph.facebook.com/v21.0";

    public async Task<MetaMessageResponse> SendTextAsync(string phoneNumberId, string accessToken, string to, string text, CancellationToken cancellationToken)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to,
            type = "text",
            text = new { body = text }
        };

        return await PostMessageAsync(phoneNumberId, accessToken, payload, cancellationToken);
    }

    public async Task<MetaMessageResponse> SendTemplateAsync(string phoneNumberId, string accessToken, string to, string templateName, string languageCode, Dictionary<string, string>? parameters, CancellationToken cancellationToken)
    {
        object? components = null;
        if (parameters is { Count: > 0 })
        {
            components = new[]
            {
                new
                {
                    type = "body",
                    parameters = parameters.Select(p => new { type = "text", text = p.Value }).ToArray()
                }
            };
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = languageCode },
                components
            }
        };

        return await PostMessageAsync(phoneNumberId, accessToken, payload, cancellationToken);
    }

    private async Task<MetaMessageResponse> PostMessageAsync(string phoneNumberId, string accessToken, object payload, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{GraphApiBase}/{phoneNumberId}/messages")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new("Bearer", accessToken);

            var client = httpClientFactory.CreateClient("MetaCloudApi");
            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new MetaMessageResponse { Success = false, Error = $"HTTP {(int)response.StatusCode}: {body}" };

            using var doc = JsonDocument.Parse(body);
            var messageId = doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id").GetString();

            return new MetaMessageResponse { Success = true, MessageId = messageId };
        }
        catch (Exception ex)
        {
            return new MetaMessageResponse { Success = false, Error = ex.Message };
        }
    }
}
