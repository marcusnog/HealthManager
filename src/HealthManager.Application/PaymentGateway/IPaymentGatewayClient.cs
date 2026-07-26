using HealthManager.Domain;

namespace HealthManager.Application;

public sealed record GatewayPaymentStatusResponse(PaymentIntentStatus Status, string? FailureReason);

public sealed record CreateChargeRequest(
    string IdempotencyKey,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string Description,
    string? PatientName,
    string? PatientCpf,
    string? ReturnUrl = null);

public sealed record CreateChargeResponse(
    PaymentIntentStatus Status,
    string? GatewayReference,
    string? PixQrCode,
    string? PixCopyPaste,
    string? CheckoutUrl,
    DateTimeOffset? ExpiresAt);

public interface IPaymentGatewayClient
{
    Task<GatewayPaymentStatusResponse> GetPaymentStatusAsync(string paymentId, PaymentGatewayProvider provider, Guid clinicId, CancellationToken cancellationToken);
    Task<CreateChargeResponse> CreateChargeAsync(CreateChargeRequest request, PaymentGatewayProvider provider, Guid clinicId, CancellationToken cancellationToken);
}
