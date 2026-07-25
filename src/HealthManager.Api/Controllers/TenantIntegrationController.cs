using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicAdmin")]
[Route("tenant/integration")]
public sealed class TenantIntegrationController(TenantIntegrationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TenantIntegrationResponse>> GetAll(CancellationToken ct) =>
        Ok(await service.GetAllAsync(ct));

    [HttpPut("whatsapp")]
    public async Task<ActionResult<WhatsAppConfigResponse>> UpsertWhatsApp(
        [FromBody] UpdateWhatsAppConfigRequest request, CancellationToken ct) =>
        Ok(await service.UpsertWhatsAppAsync(request, ct));

    [HttpPut("payment-gateway")]
    public async Task<ActionResult<PaymentGatewayConfigResponse>> UpsertPaymentGateway(
        [FromBody] UpdatePaymentGatewayConfigRequest request, CancellationToken ct) =>
        Ok(await service.UpsertPaymentGatewayAsync(request, ct));

    [HttpPut("notification")]
    public async Task<ActionResult<NotificationConfigResponse>> UpsertNotification(
        [FromBody] UpdateNotificationConfigRequest request, CancellationToken ct) =>
        Ok(await service.UpsertNotificationAsync(request, ct));

    [HttpPut("branding")]
    public async Task<ActionResult<BrandingResponse>> UpsertBranding(
        [FromBody] UpdateBrandingRequest request, CancellationToken ct) =>
        Ok(await service.UpsertBrandingAsync(request, ct));
}
