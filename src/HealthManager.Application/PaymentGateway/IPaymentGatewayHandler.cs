using System.Security.Cryptography;
using System.Text;
using HealthManager.Domain;

namespace HealthManager.Application;

public sealed class WebhookPaymentResult
{
    public string? PaymentIntentId { get; init; }
    public string? GatewayReference { get; init; }
    public PaymentIntentStatus Status { get; init; }
    public string? FailureReason { get; init; }
    public string? IdempotencyKey { get; init; }
    public decimal? Amount { get; init; }
}

public interface IPaymentGatewayHandler
{
    PaymentGatewayProvider Provider { get; }
    bool VerifySignature(string payload, string? signature, string? webhookSecret) =>
        WebhookSignatureVerifier.VerifyHmacSha256(payload, signature, webhookSecret);
    string? Validate(string payload);
    Task<WebhookPaymentResult> ParseAsync(string payload, CancellationToken cancellationToken);
}

internal static class WebhookSignatureVerifier
{
    internal static bool VerifyHmacSha256(string payload, string? signature, string? webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret) || string.IsNullOrWhiteSpace(signature))
            return false;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexStringLower(hash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }
}
