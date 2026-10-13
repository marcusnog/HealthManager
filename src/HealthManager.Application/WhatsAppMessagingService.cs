using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public sealed class WhatsAppMessagingService(
    IApplicationDbContext dbContext,
    IMetaCloudApiClient metaClient,
    IOutboxService outboxService)
{
    public Task<MetaMessageResponse> SendTextAsync(Guid clinicId, string phone, string text, CancellationToken cancellationToken, Guid? clientRequestId = null)
        => SendCoreAsync(clinicId, phone, text, (c, pid, tok, to, msg, ct) => c.SendTextAsync(pid, tok, to, msg, ct), cancellationToken, clientRequestId);

    public Task<MetaMessageResponse> SendTemplateAsync(Guid clinicId, string phone, string templateName, string languageCode, Dictionary<string, string>? parameters, CancellationToken cancellationToken)
        => SendCoreAsync(clinicId, phone, $"[template:{templateName}]", (c, pid, tok, to, _, ct) => c.SendTemplateAsync(pid, tok, to, templateName, languageCode, parameters, ct), cancellationToken);

    public async Task SendAppointmentConfirmationAsync(Appointment appointment, Patient patient, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(patient.Phone) || !appointment.ClinicId.HasValue) return;

        var phone = AppHelpers.NormalizePhone(patient.Phone);
        var date = appointment.StartAt.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy HH:mm");
        var text = $"Ola {patient.Name}! Sua consulta foi confirmada para {date}. Se precisar de ajuda ou reagendamento, responda para falar com a recepcao.";

        await SendTextAsync(appointment.ClinicId.Value, phone, text, cancellationToken);

        await outboxService.EnqueueAsync(appointment.ClinicId, "whatsapp.appointment.confirmed", new
        {
            AppointmentId = appointment.Id,
            PatientId = patient.Id,
            phone
        }, cancellationToken);
    }

    public async Task SendAppointmentCancellationAsync(Appointment appointment, Patient patient, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(patient.Phone) || !appointment.ClinicId.HasValue) return;

        var phone = AppHelpers.NormalizePhone(patient.Phone);
        var date = appointment.StartAt.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy HH:mm");
        var text = $"Ola {patient.Name}! Sua consulta do dia {date} foi cancelada. Para reagendar, entre em contato.";

        await SendTextAsync(appointment.ClinicId.Value, phone, text, cancellationToken);
    }

    private async Task<MetaMessageResponse> SendCoreAsync(
        Guid clinicId, string phone, string displayMessage,
        Func<IMetaCloudApiClient, string, string, string, string, CancellationToken, Task<MetaMessageResponse>> send,
        CancellationToken cancellationToken, Guid? clientRequestId = null)
    {
        phone = AppHelpers.NormalizePhone(phone);
        if (clientRequestId.HasValue)
        {
            var existing = await dbContext.WhatsAppMessages.SingleOrDefaultAsync(x => x.ClinicId == clinicId && x.ClientRequestId == clientRequestId, cancellationToken);
            if (existing is not null)
            {
                if (existing.Phone != phone || existing.Message != displayMessage)
                    throw new InvalidOperationException("Identificador de envio ja utilizado para outra mensagem.");
                return new MetaMessageResponse { StoredMessage = ToResponse(existing), Success = existing.Status != WhatsAppMessageStatus.Failed };
            }
        }
        var config = await dbContext.ClinicWhatsAppConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.IsEnabled, cancellationToken);
        if (config is null)
            return new MetaMessageResponse { Success = false, Error = "WhatsApp nao configurado para esta clinica." };

        var message = new WhatsAppMessage
        {
            ClinicId = clinicId,
            Phone = phone,
            Message = displayMessage,
            Direction = MessageDirection.Outbound,
            Status = WhatsAppMessageStatus.Pending,
            ClientRequestId = clientRequestId
        };
        dbContext.WhatsAppMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);
        MetaMessageResponse result;
        try
        {
            result = await send(metaClient, config.PhoneNumberOfId, config.AccessToken, phone, displayMessage, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            result = new MetaMessageResponse { DeliveryUncertain = true };
        }
        message.Status = result.DeliveryUncertain ? WhatsAppMessageStatus.Pending
            : result.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed;
        message.ProviderMessageId = result.MessageId;
        // Persist the result even if the browser disconnected after Meta accepted the request.
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new MetaMessageResponse { Success = result.Success, MessageId = result.MessageId,
            Error = result.Error, DeliveryUncertain = result.DeliveryUncertain, StoredMessage = ToResponse(message) };
    }
    internal static WhatsAppMessageResponse ToResponse(WhatsAppMessage message) =>
        new(message.Id, message.Phone, message.Message, message.Status, message.Direction, message.CreatedAt, message.ProviderTimestamp);
}

