using System.Globalization;
using System.Text.Json;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public sealed class WhatsAppWebhookService(IApplicationDbContext dbContext)
{
    // Only called after verification of the raw body by the API boundary.
    public async Task ProcessAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (Text(payload, "object") != "whatsapp_business_account" || !IsArray(payload, "entry"))
            throw new InvalidOperationException("Envelope Meta invalido.");

        foreach (var entry in payload.GetProperty("entry").EnumerateArray())
        foreach (var change in Array(entry, "changes"))
        {
            if (Text(change, "field") != "messages" || !change.TryGetProperty("value", out var value)) continue;
            if (value.ValueKind != JsonValueKind.Object) continue;
            if (!value.TryGetProperty("metadata", out var metadata)) continue;
            var phoneNumberId = Text(metadata, "phone_number_id");
            if (string.IsNullOrWhiteSpace(phoneNumberId)) continue;

            // Cross-tenant lookup is restricted to the verified provider number, never headers or payload clinicId.
            var configs = await dbContext.ClinicWhatsAppConfigs.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.PhoneNumberOfId == phoneNumberId && x.IsEnabled && x.DeletedAt == null)
                .Take(2).ToListAsync(cancellationToken);
            if (configs.Count != 1) continue;
            var clinicId = configs[0].ClinicId;
            if (!await dbContext.Clinics.IgnoreQueryFilters().AnyAsync(x => x.Id == clinicId && x.DeletedAt == null && x.Status == ClinicStatus.Active, cancellationToken)) continue;

            foreach (var message in Array(value, "messages"))
            {
                var providerId = Text(message, "id");
                var phone = AppHelpers.NormalizePhone(Text(message, "from") ?? "");
                if (string.IsNullOrWhiteSpace(providerId) || phone.Length is < 12 or > 15) continue;
                if (await dbContext.WhatsAppMessages.IgnoreQueryFilters().AnyAsync(x => x.ClinicId == clinicId
                    && x.Direction == MessageDirection.Inbound && x.ProviderMessageId == providerId, cancellationToken)) continue;
                var timestamp = Timestamp(message);
                if (timestamp is null || timestamp > DateTimeOffset.UtcNow.AddMinutes(5)) continue;
                dbContext.WhatsAppMessages.Add(new WhatsAppMessage
                {
                    ClinicId = clinicId, Phone = phone, ProviderMessageId = providerId,
                    ProviderTimestamp = timestamp, Message = Content(message),
                    Direction = MessageDirection.Inbound, Status = WhatsAppMessageStatus.Delivered
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            foreach (var status in Array(value, "statuses"))
            {
                var providerId = Text(status, "id");
                if (string.IsNullOrWhiteSpace(providerId)) continue;
                var message = await dbContext.WhatsAppMessages.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.ClinicId == clinicId && x.DeletedAt == null && x.Direction == MessageDirection.Outbound
                    && x.ProviderMessageId == providerId, cancellationToken);
                if (message is null) continue;
                var next = Text(status, "status") switch
                {
                    "sent" => WhatsAppMessageStatus.Sent,
                    "delivered" => WhatsAppMessageStatus.Delivered,
                    "read" => WhatsAppMessageStatus.Read,
                    "failed" => WhatsAppMessageStatus.Failed,
                    _ => message.Status
                };
                if (CanAdvance(message.Status, next))
                {
                    message.Status = next;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }

    private static bool CanAdvance(WhatsAppMessageStatus current, WhatsAppMessageStatus next) =>
        current != next && current != WhatsAppMessageStatus.Read && (next switch
        {
            WhatsAppMessageStatus.Read => true,
            WhatsAppMessageStatus.Delivered => current != WhatsAppMessageStatus.Delivered,
            WhatsAppMessageStatus.Failed => current is WhatsAppMessageStatus.Pending or WhatsAppMessageStatus.Sent,
            WhatsAppMessageStatus.Sent => current == WhatsAppMessageStatus.Pending,
            _ => false
        });

    private static string Content(JsonElement message)
    {
        var type = Text(message, "type");
        if (type == "text" && message.TryGetProperty("text", out var text)) return Text(text, "body") ?? "[Texto vazio]";
        if (type == "button" && message.TryGetProperty("button", out var button)) return Text(button, "text") ?? "[Botão]";
        if (type == "interactive" && message.TryGetProperty("interactive", out var interactive) && interactive.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "button_reply", "list_reply" })
                if (interactive.TryGetProperty(key, out var reply)) return Text(reply, "title") ?? "[Resposta interativa]";
        }
        return "[Conteúdo não suportado nesta central: " + (type ?? "desconhecido") + "]";
    }

    private static DateTimeOffset? Timestamp(JsonElement message)
    {
        if (!long.TryParse(Text(message, "timestamp"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds < 0 || seconds > 253402300799) return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static string? Text(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool IsArray(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array;
    private static IEnumerable<JsonElement> Array(JsonElement item, string key) =>
        IsArray(item, key) ? item.GetProperty(key).EnumerateArray() : Enumerable.Empty<JsonElement>();
}
