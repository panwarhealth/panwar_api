using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Edm;

/// <summary>Everything the public, anonymous eDM endpoints do: opens, unsubscribes, delivery reports.</summary>
public interface IEdmTrackingService
{
    Task RecordOpenAsync(Guid recipientId, CancellationToken ct = default);
    Task<(EdmRecipient Recipient, EdmSender Sender, bool Unsubscribed)?> GetUnsubscribeStateAsync(Guid recipientId, CancellationToken ct = default);
    Task<bool> UnsubscribeAsync(Guid recipientId, CancellationToken ct = default);
    Task<bool> ResubscribeAsync(Guid recipientId, CancellationToken ct = default);
    Task ApplyDeliveryReportAsync(string messageId, string status, string? detail, DateTime? at, CancellationToken ct = default);
}

public class EdmTrackingService : IEdmTrackingService
{
    private readonly AppDbContext _context;
    private readonly IEdmSubscriptionService _subscriptions;
    private readonly ILogger<EdmTrackingService> _logger;

    public EdmTrackingService(AppDbContext context, IEdmSubscriptionService subscriptions, ILogger<EdmTrackingService> logger)
    {
        _context = context;
        _subscriptions = subscriptions;
        _logger = logger;
    }

    public async Task RecordOpenAsync(Guid recipientId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _context.EdmRecipients.Where(r => r.Id == recipientId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.OpenCount, r => r.OpenCount + 1)
                .SetProperty(r => r.FirstOpenedAt, r => r.FirstOpenedAt ?? now), ct);
    }

    public async Task<(EdmRecipient Recipient, EdmSender Sender, bool Unsubscribed)?> GetUnsubscribeStateAsync(Guid recipientId, CancellationToken ct = default)
    {
        var recipient = await _context.EdmRecipients.AsNoTracking()
            .Include(r => r.Campaign).ThenInclude(c => c.Sender)
            .FirstOrDefaultAsync(r => r.Id == recipientId, ct);
        if (recipient?.Campaign.Sender is not { } sender) return null;

        var subscribed = await _context.EdmContacts.AnyAsync(c =>
            c.Email == recipient.Email && c.List.SenderId == sender.Id && c.Status == EdmContactStatus.Subscribed, ct);
        return (recipient, sender, !subscribed);
    }

    public async Task<bool> UnsubscribeAsync(Guid recipientId, CancellationToken ct = default)
    {
        var target = await TargetAsync(recipientId, ct);
        if (target is null) return false;
        await _subscriptions.UnsubscribeAsync(target.Value.SenderId, target.Value.Email, ct);
        await _context.EdmRecipients
            .Where(r => r.Id == recipientId && r.UnsubscribedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UnsubscribedAt, DateTime.UtcNow), ct);
        return true;
    }

    public async Task<bool> ResubscribeAsync(Guid recipientId, CancellationToken ct = default)
    {
        var target = await TargetAsync(recipientId, ct);
        if (target is null) return false;
        await _subscriptions.ResubscribeAsync(target.Value.SenderId, target.Value.Email, ct);
        return true;
    }

    public async Task ApplyDeliveryReportAsync(string messageId, string status, string? detail, DateTime? at, CancellationToken ct = default)
    {
        var recipient = await _context.EdmRecipients.FirstOrDefaultAsync(r => r.MessageId == messageId, ct);
        if (recipient is null)
        {
            // Test sends have no recipient row, and other apps may share the ACS resource.
            _logger.LogDebug("Delivery report for unknown message {MessageId}", messageId);
            return;
        }

        var when = at ?? DateTime.UtcNow;
        switch (status)
        {
            case "Delivered":
                recipient.Status = EdmRecipientStatus.Delivered;
                recipient.DeliveredAt = when;
                break;
            case "Bounced":
            case "Suppressed": // on ACS's own suppression list from an earlier hard bounce
                recipient.Status = EdmRecipientStatus.Bounced;
                recipient.DeliveredAt = when;
                recipient.Error = EdmText.Optional(detail ?? status, 500);
                await _subscriptions.ApplyBounceAsync(recipient.Email, ct);
                break;
            case "Failed":
            case "FilteredSpam":
            case "Quarantined":
                recipient.Status = EdmRecipientStatus.Failed;
                recipient.Error = EdmText.Optional($"{status}: {detail}".TrimEnd(' ', ':'), 500);
                break;
            default:
                // "Expanded" (distribution list) and anything new: record nothing.
                return;
        }
        await _context.SaveChangesAsync(ct);
    }

    private async Task<(Guid SenderId, string Email)?> TargetAsync(Guid recipientId, CancellationToken ct)
    {
        var row = await _context.EdmRecipients.AsNoTracking()
            .Where(r => r.Id == recipientId && r.Campaign.SenderId != null)
            .Select(r => new { r.Email, SenderId = r.Campaign.SenderId!.Value })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.SenderId, row.Email);
    }
}
