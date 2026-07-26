using System.Text.Json;
using HealthManager.Domain;

namespace HealthManager.Application;

public sealed class AsaasGatewayHandler : IPaymentGatewayHandler
{
    public PaymentGatewayProvider Provider => PaymentGatewayProvider.Asaas;

    public string? Validate(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (!root.TryGetProperty("status", out _)) return "Campo 'status' obrigatorio.";
        if (!root.TryGetProperty("payment", out _)) return "Campo 'payment' obrigatorio.";
        return null;
    }

    public Task<WebhookPaymentResult> ParseAsync(string payload, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var status = root.GetProperty("status").GetString() switch
        {
            "CONFIRMED" => PaymentIntentStatus.Confirmed,
            "RECEIVED" => PaymentIntentStatus.Processing,
            "OVERDUE" or "REFUNDED" => PaymentIntentStatus.Failed,
            _ => PaymentIntentStatus.Processing
        };

        return Task.FromResult(new WebhookPaymentResult
        {
            GatewayReference = root.TryGetProperty("payment", out var p) ? p.GetString() : null,
            Status = status,
            PaymentIntentId = root.TryGetProperty("paymentExternalReference", out var refId) ? refId.GetString() : null,
            FailureReason = status == PaymentIntentStatus.Failed ? root.GetProperty("status").GetString() : null
        });
    }
}

public sealed class MercadoPagoGatewayHandler : IPaymentGatewayHandler
{
    public PaymentGatewayProvider Provider => PaymentGatewayProvider.MercadoPago;

    public string? Validate(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var data)) return "Campo 'data' obrigatorio.";
        if (!data.TryGetProperty("status", out _)) return "Campo 'data.status' obrigatorio.";
        if (!data.TryGetProperty("id", out _)) return "Campo 'data.id' obrigatorio.";
        return null;
    }

    public Task<WebhookPaymentResult> ParseAsync(string payload, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var status = root.TryGetProperty("data", out var data) && data.TryGetProperty("status", out var st)
            ? st.GetString() : null;

        var intentStatus = status switch
        {
            "approved" => PaymentIntentStatus.Confirmed,
            "pending" => PaymentIntentStatus.Processing,
            "cancelled" or "refunded" => PaymentIntentStatus.Failed,
            _ => PaymentIntentStatus.Processing
        };

        return Task.FromResult(new WebhookPaymentResult
        {
            Status = intentStatus,
            GatewayReference = root.TryGetProperty("data", out var d) && d.TryGetProperty("id", out var id) ? id.GetString() : null,
            FailureReason = intentStatus == PaymentIntentStatus.Failed ? status : null
        });
    }
}

public sealed class StripeGatewayHandler : IPaymentGatewayHandler
{
    public PaymentGatewayProvider Provider => PaymentGatewayProvider.Stripe;

    public string? Validate(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out _)) return "Campo 'type' obrigatorio.";
        if (!root.TryGetProperty("data", out var data)) return "Campo 'data' obrigatorio.";
        if (!data.TryGetProperty("object", out var obj)) return "Campo 'data.object' obrigatorio.";
        if (!obj.TryGetProperty("id", out _)) return "Campo 'data.object.id' obrigatorio.";
        return null;
    }

    public Task<WebhookPaymentResult> ParseAsync(string payload, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        var intentStatus = type switch
        {
            "payment_intent.succeeded" => PaymentIntentStatus.Confirmed,
            "payment_intent.processing" => PaymentIntentStatus.Processing,
            "payment_intent.payment_failed" => PaymentIntentStatus.Failed,
            _ => PaymentIntentStatus.Processing
        };

        return Task.FromResult(new WebhookPaymentResult
        {
            Status = intentStatus,
            GatewayReference = root.TryGetProperty("data", out var d) && d.TryGetProperty("object", out var obj)
                && obj.TryGetProperty("id", out var id) ? id.GetString() : null,
            FailureReason = intentStatus == PaymentIntentStatus.Failed ? type : null
        });
    }
}
