using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public sealed class WhatsAppAtendimentoService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    WhatsAppMessagingService messagingService)
{
    public async Task<PagedResult<WhatsAppConversationResponse>> ListAsync(WhatsAppConversationQuery query, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var page = Math.Clamp(query.Page, 1, 1_000_000);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var messages = dbContext.WhatsAppMessages.AsNoTracking().Where(x => x.ClinicId == clinicId && x.DeletedAt == null);
        var patients = dbContext.Patients.AsNoTracking().Where(x => x.ClinicId == clinicId && x.DeletedAt == null && x.Phone != "");
        var conversations = messages.Where(x => x.Id == messages.Where(m => m.Phone == x.Phone)
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Id).First())
            .Select(x => new { x.Phone,
                PatientName = patients.Count(p => p.Phone == x.Phone || "55" + p.Phone == x.Phone) == 1
                    ? patients.Where(p => p.Phone == x.Phone || "55" + p.Phone == x.Phone).Select(p => p.Name).FirstOrDefault() : null,
                LastMessage = x.Message, LastMessageAt = x.CreatedAt, LastDirection = x.Direction,
                LastInboundAt = messages.Where(m => m.Phone == x.Phone && m.Direction == MessageDirection.Inbound)
                    .Max(m => m.ProviderTimestamp) });
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            conversations = conversations.Where(x => x.Phone.Contains(search) || (x.PatientName != null && x.PatientName.ToLower().Contains(search)));
        }
        var total = await conversations.CountAsync(cancellationToken);
        var items = await conversations.OrderByDescending(x => x.LastMessageAt).ThenBy(x => x.Phone)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new WhatsAppConversationResponse(x.Phone, x.PatientName, x.LastMessage, x.LastMessageAt, x.LastDirection, x.LastInboundAt))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, total);
    }

    public async Task<PagedResult<WhatsAppMessageResponse>> MessagesAsync(string phone, CancellationToken cancellationToken, int page = 1, int pageSize = 50)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var normalized = AppHelpers.NormalizePhone(phone);
        page = Math.Clamp(page, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.WhatsAppMessages.AsNoTracking().Where(x => x.ClinicId == clinicId && x.DeletedAt == null && x.Phone == normalized);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new WhatsAppMessageResponse(x.Id, x.Phone, x.Message, x.Status, x.Direction, x.CreatedAt, x.ProviderTimestamp)).ToListAsync(cancellationToken);
        return new(items, page, pageSize, total);
    }

    public async Task<WhatsAppMessageResponse> SendAsync(string phone, SendWhatsAppMessageRequest request, CancellationToken cancellationToken)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var normalized = AppHelpers.NormalizePhone(phone);
        var text = request.Message?.Trim() ?? "";
        if (normalized.Length is < 12 or > 15) throw new InvalidOperationException("Telefone de WhatsApp invalido.");
        if (text.Length is < 1 or > 4096) throw new InvalidOperationException("Informe uma mensagem de ate 4096 caracteres.");
        if (request.ClientRequestId == Guid.Empty) throw new InvalidOperationException("Identificador de envio invalido.");
        if (request.ClientRequestId.HasValue)
        {
            var existing = await dbContext.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(x => x.ClinicId == clinicId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (existing is not null)
            {
                if (existing.Phone != normalized || existing.Message != text) throw new InvalidOperationException("Identificador de envio ja utilizado para outra mensagem.");
                return WhatsAppMessagingService.ToResponse(existing);
            }
        }
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        if (!await dbContext.WhatsAppMessages.AnyAsync(x => x.ClinicId == clinicId && x.Phone == normalized
            && x.Direction == MessageDirection.Inbound && x.ProviderTimestamp > since, cancellationToken))
            throw new InvalidOperationException("Janela de resposta encerrada. Aguarde uma nova mensagem do paciente.");
        var result = await messagingService.SendTextAsync(clinicId, normalized, text, cancellationToken, request.ClientRequestId);
        return result.StoredMessage ?? throw new InvalidOperationException(result.Error ?? "Nao foi possivel enviar a mensagem.");
    }
}
