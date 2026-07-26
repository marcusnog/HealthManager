using HealthManager.Domain;

namespace HealthManager.Application;

// ponytail: returns fake charge + Confirmed for any payment — swap with real gateway client when provider is chosen
public sealed class MockPaymentGatewayClient : IPaymentGatewayClient
{
    public Task<GatewayPaymentStatusResponse> GetPaymentStatusAsync(string paymentId, PaymentGatewayProvider provider, Guid clinicId, CancellationToken cancellationToken)
        => Task.FromResult(new GatewayPaymentStatusResponse(PaymentIntentStatus.Confirmed, null));

    public Task<CreateChargeResponse> CreateChargeAsync(CreateChargeRequest request, PaymentGatewayProvider provider, Guid clinicId, CancellationToken cancellationToken)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        CreateChargeResponse response = request.PaymentMethod switch
        {
            PaymentMethod.Pix => new(
                PaymentIntentStatus.Processing,
                GatewayReference: $"mock-gw-{Guid.NewGuid():N}",
                PixQrCode: Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"mock-qr-code-for-{request.IdempotencyKey}")),
                PixCopyPaste: $"00020126580014BR.GOV.BCB.PIX0136mock-charge-{request.IdempotencyKey}5204000053039865404{request.Amount:F2}5802BR5913MOCK GATEWAY6009SAO PAULO62070503***6304",
                CheckoutUrl: null,
                ExpiresAt: expiresAt),
            PaymentMethod.CreditCard or PaymentMethod.DebitCard => new(
                PaymentIntentStatus.Processing,
                GatewayReference: $"mock-gw-{Guid.NewGuid():N}",
                PixQrCode: null,
                PixCopyPaste: null,
                CheckoutUrl: $"https://mock-gateway.example.com/checkout/{request.IdempotencyKey}",
                ExpiresAt: expiresAt),
            _ => new(PaymentIntentStatus.Processing, $"mock-gw-{Guid.NewGuid():N}", null, null, null, expiresAt)
        };

        return Task.FromResult(response);
    }
}
