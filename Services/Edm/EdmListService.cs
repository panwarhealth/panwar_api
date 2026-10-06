using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Edm;

public interface IEdmListService
{
    Task<IReadOnlyList<EdmSenderDto>> ListSendersAsync(CancellationToken ct = default);
    Task<EdmSenderDto?> UpdateSenderBrandingAsync(Guid id, EdmSenderBrandingRequest data, CancellationToken ct = default);

    Task<IReadOnlyList<EdmListDto>> ListListsAsync(CancellationToken ct = default);
    Task<EdmListDto?> GetListAsync(Guid id, CancellationToken ct = default);
    Task<EdmListDto> CreateListAsync(EdmListWriteRequest data, Guid userId, CancellationToken ct = default);
    Task<EdmListDto?> UpdateListAsync(Guid id, EdmListWriteRequest data, CancellationToken ct = default);
    Task<bool> DeleteListAsync(Guid id, CancellationToken ct = default);

    Task<EdmContactPage?> ListContactsAsync(Guid listId, string? search, string? status, int page, CancellationToken ct = default);
    Task<EdmContactDto?> AddContactAsync(Guid listId, EdmContactWriteRequest data, CancellationToken ct = default);
    Task<EdmContactDto?> SetContactStatusAsync(Guid listId, Guid contactId, string status, CancellationToken ct = default);
    Task<EdmImportResult?> ImportAsync(Guid listId, EdmImportRequest data, CancellationToken ct = default);
}

public class EdmListService : IEdmListService
{
    private const int PageSize = 50;
    private const int MaxImportRows = 50_000;

    private static readonly Regex HexColour = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    private readonly AppDbContext _context;
    private readonly IEdmSubscriptionService _subscriptions;

    public EdmListService(AppDbContext context, IEdmSubscriptionService subscriptions)
    {
        _context = context;
        _subscriptions = subscriptions;
    }

    // ---- Senders ----

    public async Task<IReadOnlyList<EdmSenderDto>> ListSendersAsync(CancellationToken ct = default)
    {
        var senders = await _context.EdmSenders.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        return senders.Select(EdmMapping.ToDto).ToList();
    }

    public async Task<EdmSenderDto?> UpdateSenderBrandingAsync(Guid id, EdmSenderBrandingRequest data, CancellationToken ct = default)
    {
        var sender = await _context.EdmSenders.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sender is null) return null;

        var colour = data.BrandColour.Trim();
        if (!HexColour.IsMatch(colour)) throw new EdmValidationException("Brand colour must be a hex colour like #702f8f");
        var logo = string.IsNullOrWhiteSpace(data.LogoUrl) ? null : data.LogoUrl.Trim();
        if (logo is not null && !logo.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new EdmValidationException("Logo must be an https:// image URL");

        sender.ReplyTo = string.IsNullOrWhiteSpace(data.ReplyTo)
            ? null
            : EdmText.NormaliseEmail(data.ReplyTo) ?? throw new EdmValidationException("Reply-to isn't a valid email");
        sender.BrandColour = colour.ToLowerInvariant();
        sender.LogoUrl = logo;
        sender.FooterText = EdmText.Required(data.FooterText, "Footer text", 500);
        sender.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return EdmMapping.ToDto(sender);
    }

    // ---- Lists ----

    public async Task<IReadOnlyList<EdmListDto>> ListListsAsync(CancellationToken ct = default)
    {
        var lists = await _context.EdmLists.AsNoTracking().Include(l => l.Sender).OrderBy(l => l.Name).ToListAsync(ct);
        var counts = await CountsAsync(null, ct);
        return lists.Select(l => ToDto(l, counts)).ToList();
    }

    public async Task<EdmListDto?> GetListAsync(Guid id, CancellationToken ct = default)
    {
        var list = await _context.EdmLists.AsNoTracking().Include(l => l.Sender).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (list is null) return null;
        return ToDto(list, await CountsAsync(id, ct));
    }

    public async Task<EdmListDto> CreateListAsync(EdmListWriteRequest data, Guid userId, CancellationToken ct = default)
    {
        var name = EdmText.Required(data.Name, "Name", 150);
        await RequireSenderAsync(data.SenderId, ct);

        EdmSyncSource? source = null;
        if (!string.IsNullOrWhiteSpace(data.SyncSource))
        {
            if (!Enum.TryParse<EdmSyncSource>(data.SyncSource, true, out var parsed))
                throw new EdmValidationException("Unknown sync source");
            if (await _context.EdmLists.AnyAsync(l => l.SyncSource == parsed, ct))
                throw new EdmValidationException($"There's already a {parsed} synced list");
            source = parsed;
        }

        var list = new EdmList
        {
            Id = Guid.NewGuid(),
            Name = name,
            Kind = source is null ? EdmListKind.Custom : EdmListKind.Synced,
            SyncSource = source,
            SenderId = data.SenderId,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.EdmLists.Add(list);
        await _context.SaveChangesAsync(ct);
        return (await GetListAsync(list.Id, ct))!;
    }

    public async Task<EdmListDto?> UpdateListAsync(Guid id, EdmListWriteRequest data, CancellationToken ct = default)
    {
        var list = await _context.EdmLists.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (list is null) return null;
        list.Name = EdmText.Required(data.Name, "Name", 150);
        if (data.SenderId != list.SenderId)
        {
            await RequireSenderAsync(data.SenderId, ct);
            list.SenderId = data.SenderId;
        }
        list.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return await GetListAsync(id, ct);
    }

    public async Task<bool> DeleteListAsync(Guid id, CancellationToken ct = default)
    {
        var list = await _context.EdmLists.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (list is null) return false;
        if (await _context.EdmCampaigns.AnyAsync(c => c.ListId == id && c.Status != EdmCampaignStatus.Draft, ct))
            throw new EdmValidationException("This list has campaigns sent or scheduled to it, so it can't be deleted");

        await _context.EdmCampaigns.Where(c => c.ListId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ListId, (Guid?)null), ct);
        _context.EdmLists.Remove(list);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    // ---- Contacts ----

    public async Task<EdmContactPage?> ListContactsAsync(Guid listId, string? search, string? status, int page, CancellationToken ct = default)
    {
        if (!await _context.EdmLists.AnyAsync(l => l.Id == listId, ct)) return null;

        var query = _context.EdmContacts.AsNoTracking().Where(c => c.ListId == listId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().ToLowerInvariant()}%";
            query = query.Where(c => EF.Functions.ILike(c.Email, term)
                || (c.FirstName != null && EF.Functions.ILike(c.FirstName, term))
                || (c.LastName != null && EF.Functions.ILike(c.LastName, term)));
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EdmContactStatus>(status, true, out var s))
            query = query.Where(c => c.Status == s);

        page = Math.Max(1, page);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(c => c.Email).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return new EdmContactPage(rows.Select(EdmMapping.ToDto).ToList(), total, page, PageSize);
    }

    public async Task<EdmContactDto?> AddContactAsync(Guid listId, EdmContactWriteRequest data, CancellationToken ct = default)
    {
        var list = await _context.EdmLists.FirstOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null) return null;
        RequireCustom(list);

        var email = EdmText.NormaliseEmail(data.Email) ?? throw new EdmValidationException("That isn't a valid email address");
        if (await _context.EdmContacts.AnyAsync(c => c.ListId == listId && c.Email == email, ct))
            throw new EdmValidationException("That address is already on the list");

        var suppressed = await _subscriptions.SuppressedStatusesAsync(list.SenderId, [email], ct);
        var contact = new EdmContact
        {
            Id = Guid.NewGuid(),
            ListId = listId,
            Email = email,
            FirstName = EdmText.Optional(data.FirstName, 100),
            LastName = EdmText.Optional(data.LastName, 100),
            Status = suppressed.GetValueOrDefault(email, EdmContactStatus.Subscribed),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.EdmContacts.Add(contact);
        await _context.SaveChangesAsync(ct);
        return EdmMapping.ToDto(contact);
    }

    public async Task<EdmContactDto?> SetContactStatusAsync(Guid listId, Guid contactId, string status, CancellationToken ct = default)
    {
        var contact = await _context.EdmContacts.Include(c => c.List)
            .FirstOrDefaultAsync(c => c.Id == contactId && c.ListId == listId, ct);
        if (contact is null) return null;

        switch (status.Trim().ToLowerInvariant())
        {
            case "unsubscribed":
                await _subscriptions.UnsubscribeAsync(contact.List.SenderId, contact.Email, ct);
                break;
            case "subscribed":
                if (contact.Status == EdmContactStatus.Bounced)
                    throw new EdmValidationException("This address bounced. Fix the address rather than resubscribing it");
                await _subscriptions.ResubscribeAsync(contact.List.SenderId, contact.Email, ct);
                break;
            default:
                throw new EdmValidationException("Status must be subscribed or unsubscribed");
        }

        await _context.Entry(contact).ReloadAsync(ct);
        return EdmMapping.ToDto(contact);
    }

    public async Task<EdmImportResult?> ImportAsync(Guid listId, EdmImportRequest data, CancellationToken ct = default)
    {
        var list = await _context.EdmLists.FirstOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null) return null;
        RequireCustom(list);
        if (data.Contacts.Count == 0) throw new EdmValidationException("The file has no rows");
        if (data.Contacts.Count > MaxImportRows) throw new EdmValidationException($"Import up to {MaxImportRows:N0} rows at a time");
        var replace = data.Mode.Equals("replace", StringComparison.OrdinalIgnoreCase);

        var invalid = new List<string>();
        var incoming = new Dictionary<string, EdmContactWriteRequest>();
        foreach (var row in data.Contacts)
        {
            var email = EdmText.NormaliseEmail(row.Email);
            if (email is null)
            {
                if (!string.IsNullOrWhiteSpace(row.Email)) invalid.Add(row.Email.Trim());
                else invalid.Add("(blank)");
                continue;
            }
            incoming[email] = row; // last row for a duplicated address wins
        }

        var existing = await _context.EdmContacts.Where(c => c.ListId == listId).ToDictionaryAsync(c => c.Email, ct);
        var suppressed = await _subscriptions.SuppressedStatusesAsync(list.SenderId, incoming.Keys.Where(e => !existing.ContainsKey(e)).ToList(), ct);
        var now = DateTime.UtcNow;
        int added = 0, updated = 0, removed = 0, suppressedCount = 0;

        foreach (var (email, row) in incoming)
        {
            var first = EdmText.Optional(row.FirstName, 100);
            var last = EdmText.Optional(row.LastName, 100);
            if (existing.TryGetValue(email, out var contact))
            {
                if ((first is not null && first != contact.FirstName) || (last is not null && last != contact.LastName))
                {
                    contact.FirstName = first ?? contact.FirstName;
                    contact.LastName = last ?? contact.LastName;
                    contact.UpdatedAt = now;
                    updated++;
                }
                continue;
            }

            var status = suppressed.GetValueOrDefault(email, EdmContactStatus.Subscribed);
            if (status != EdmContactStatus.Subscribed) suppressedCount++;
            _context.EdmContacts.Add(new EdmContact
            {
                Id = Guid.NewGuid(),
                ListId = listId,
                Email = email,
                FirstName = first,
                LastName = last,
                Status = status,
                StatusChangedAt = status == EdmContactStatus.Subscribed ? null : now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }

        if (replace)
        {
            // Only subscribed contacts go: unsubscribes and bounces stay as the suppression record.
            foreach (var gone in existing.Values.Where(c => c.Status == EdmContactStatus.Subscribed && !incoming.ContainsKey(c.Email)))
            {
                _context.EdmContacts.Remove(gone);
                removed++;
            }
        }

        list.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);
        return new EdmImportResult(added, updated, removed, suppressedCount, invalid.Count, invalid.Take(20).ToList());
    }

    // ---- helpers ----

    private sealed record ListCounts(int Subscribed, int Unsubscribed, int Bounced);

    private async Task<Dictionary<Guid, ListCounts>> CountsAsync(Guid? listId, CancellationToken ct)
    {
        var query = _context.EdmContacts.AsNoTracking();
        if (listId is { } id) query = query.Where(c => c.ListId == id);
        var rows = await query.GroupBy(c => new { c.ListId, c.Status })
            .Select(g => new { g.Key.ListId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.ListId).ToDictionary(g => g.Key, g => new ListCounts(
            g.Where(r => r.Status == EdmContactStatus.Subscribed).Sum(r => r.Count),
            g.Where(r => r.Status == EdmContactStatus.Unsubscribed).Sum(r => r.Count),
            g.Where(r => r.Status == EdmContactStatus.Bounced).Sum(r => r.Count)));
    }

    private static EdmListDto ToDto(EdmList l, Dictionary<Guid, ListCounts> counts)
    {
        var c = counts.GetValueOrDefault(l.Id, new ListCounts(0, 0, 0));
        return new EdmListDto(l.Id, l.Name, l.Kind.ToString(), l.SyncSource?.ToString(), EdmMapping.ToDto(l.Sender),
            c.Subscribed, c.Unsubscribed, c.Bounced, l.LastSyncedAt, l.LastSyncError, l.CreatedAt);
    }

    private async Task RequireSenderAsync(Guid senderId, CancellationToken ct)
    {
        if (!await _context.EdmSenders.AnyAsync(s => s.Id == senderId, ct))
            throw new EdmValidationException("Pick a sender for the list");
    }

    private static void RequireCustom(EdmList list)
    {
        if (list.Kind == EdmListKind.Synced)
            throw new EdmValidationException("Synced lists fill themselves from the platform, so contacts can't be added by hand");
    }



}
