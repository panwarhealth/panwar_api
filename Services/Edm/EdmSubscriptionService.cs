using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Edm;

/// <summary>
/// The suppression rules, and the only place they live:
/// - an unsubscribe applies to every list of the same sender (leaving PharmaChat leaves all PharmaChat lists);
/// - a hard bounce applies to every list, because the address itself is dead;
/// - synced contacts that change get WritebackPending so the sync pushes the change back to the platform.
/// </summary>
public interface IEdmSubscriptionService
{
    /// <summary>Contacts on the list who may be emailed as this sender right now.</summary>
    IQueryable<EdmContact> Sendable(Guid listId, Guid senderId);

    /// <summary>For addresses not yet on a list: the status they should arrive with if they're suppressed.</summary>
    Task<Dictionary<string, EdmContactStatus>> SuppressedStatusesAsync(Guid senderId, IReadOnlyCollection<string> emails, CancellationToken ct = default);

    Task UnsubscribeAsync(Guid senderId, string email, CancellationToken ct = default);
    Task ResubscribeAsync(Guid senderId, string email, CancellationToken ct = default);
    Task ApplyBounceAsync(string email, CancellationToken ct = default);
}

public class EdmSubscriptionService : IEdmSubscriptionService
{
    private readonly AppDbContext _context;

    public EdmSubscriptionService(AppDbContext context)
    {
        _context = context;
    }

    public IQueryable<EdmContact> Sendable(Guid listId, Guid senderId)
    {
        var suppressed = Suppressing(senderId).Select(c => c.Email);
        return _context.EdmContacts.AsNoTracking().Where(c =>
            c.ListId == listId && c.Status == EdmContactStatus.Subscribed && !suppressed.Contains(c.Email));
    }

    public async Task<Dictionary<string, EdmContactStatus>> SuppressedStatusesAsync(Guid senderId, IReadOnlyCollection<string> emails, CancellationToken ct = default)
    {
        if (emails.Count == 0) return [];
        var rows = await Suppressing(senderId).Where(c => emails.Contains(c.Email))
            .Select(c => new { c.Email, c.Status })
            .ToListAsync(ct);
        // Bounced beats unsubscribed: it's the stronger reason not to send.
        return rows.GroupBy(r => r.Email).ToDictionary(g => g.Key, g => g.Max(r => r.Status));
    }

    public Task UnsubscribeAsync(Guid senderId, string email, CancellationToken ct = default) =>
        SetStatusAsync(OfSender(senderId, email).Where(c => c.Status == EdmContactStatus.Subscribed), EdmContactStatus.Unsubscribed, ct);

    public Task ResubscribeAsync(Guid senderId, string email, CancellationToken ct = default) =>
        SetStatusAsync(OfSender(senderId, email).Where(c => c.Status == EdmContactStatus.Unsubscribed), EdmContactStatus.Subscribed, ct);

    public Task ApplyBounceAsync(string email, CancellationToken ct = default) =>
        SetStatusAsync(_context.EdmContacts.Where(c => c.Email == email && c.Status != EdmContactStatus.Bounced), EdmContactStatus.Bounced, ct);

    /// <summary>Contact rows that stop an address being emailed as this sender.</summary>
    private IQueryable<EdmContact> Suppressing(Guid senderId) =>
        _context.EdmContacts.AsNoTracking().Where(c =>
            c.Status == EdmContactStatus.Bounced
            || (c.Status == EdmContactStatus.Unsubscribed && c.List.SenderId == senderId));

    private IQueryable<EdmContact> OfSender(Guid senderId, string email) =>
        _context.EdmContacts.Where(c => c.Email == email && c.List.SenderId == senderId);

    private Task SetStatusAsync(IQueryable<EdmContact> contacts, EdmContactStatus status, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return contacts.ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Status, status)
            .SetProperty(c => c.StatusChangedAt, now)
            .SetProperty(c => c.UpdatedAt, now)
            // ExecuteUpdate can't follow c.List in a value, hence the correlated subquery.
            .SetProperty(c => c.WritebackPending, c => _context.EdmLists.Any(l => l.Id == c.ListId && l.Kind == EdmListKind.Synced)), ct);
    }
}
