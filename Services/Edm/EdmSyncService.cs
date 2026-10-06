using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Edm;

public interface IEdmSyncService
{
    Task SyncAllAsync(CancellationToken ct = default);
    Task<EdmSyncResult?> SyncListAsync(Guid listId, CancellationToken ct = default);
}

/// <summary>
/// Keeps synced lists in step with PharmaChat and Clinical Studio. Each platform exposes
///   GET  {base}/api/internal/marketing-contacts?updatedSince=&amp;cursor=   (opted in and opted out, newest changes)
///   POST {base}/api/internal/marketing-contacts/consent                  { email, marketingConsent, reason }
/// behind an x-api-key header. Our changes go out first (so a platform pull can't undo a fresh
/// unsubscribe), then theirs come in. Config: EDM_SYNC_{PHARMACHAT|CLINICALSTUDIO}_URL and _KEY.
/// </summary>
public class EdmSyncService : IEdmSyncService
{
    public const string HttpClientName = "edm-sync";

    // Re-read a little before the last sync so a change committed mid-pull isn't missed.
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly IEdmSubscriptionService _subscriptions;
    private readonly ILogger<EdmSyncService> _logger;

    public EdmSyncService(AppDbContext context, IHttpClientFactory http, IConfiguration configuration,
        IEdmSubscriptionService subscriptions, ILogger<EdmSyncService> logger)
    {
        _context = context;
        _http = http;
        _configuration = configuration;
        _subscriptions = subscriptions;
        _logger = logger;
    }

    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        var ids = await _context.EdmLists.Where(l => l.Kind == EdmListKind.Synced).Select(l => l.Id).ToListAsync(ct);
        foreach (var id in ids) await SyncListAsync(id, ct);
    }

    public async Task<EdmSyncResult?> SyncListAsync(Guid listId, CancellationToken ct = default)
    {
        var list = await _context.EdmLists.FirstOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null) return null;
        if (list.SyncSource is not { } source) throw new EdmValidationException("Only synced lists can be synced");

        var key = source.ToString().ToUpperInvariant();
        var baseUrl = _configuration[$"EDM_SYNC_{key}_URL"];
        var apiKey = _configuration[$"EDM_SYNC_{key}_KEY"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            return await FailAsync(list, $"{source} sync isn't configured yet", ct);

        var client = _http.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);

        try
        {
            var writtenBack = await PushAsync(client, list, ct);
            var (added, updated, unsubscribed) = await PullAsync(client, list, ct);
            list.LastSyncError = null;
            await _context.SaveChangesAsync(ct);
            return new EdmSyncResult(added, updated, unsubscribed, writtenBack, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "eDM sync for {Source} failed", source);
            return await FailAsync(list, $"Couldn't reach {source}: {ex.Message}", ct);
        }
    }

    private async Task<int> PushAsync(HttpClient client, EdmList list, CancellationToken ct)
    {
        var pending = await _context.EdmContacts
            .Where(c => c.ListId == list.Id && c.WritebackPending)
            .ToListAsync(ct);

        foreach (var contact in pending)
        {
            var body = new
            {
                email = contact.Email,
                marketingConsent = contact.Status == EdmContactStatus.Subscribed,
                reason = contact.Status switch
                {
                    EdmContactStatus.Bounced => "bounced",
                    EdmContactStatus.Unsubscribed => "unsubscribed",
                    _ => "resubscribed",
                },
            };
            using var response = await client.PostAsJsonAsync("api/internal/marketing-contacts/consent", body, Json, ct);
            // 404 means the platform no longer has the user — nothing left to write back to.
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                throw new HttpRequestException($"consent write-back returned {(int)response.StatusCode}");
            contact.WritebackPending = false;
            await _context.SaveChangesAsync(ct);
        }
        return pending.Count;
    }

    private sealed record PlatformContact(string? ExternalId, string Email, string? FirstName, string? LastName, bool MarketingConsent, DateTime UpdatedAt);

    private sealed record PlatformPage(List<PlatformContact> Contacts, string? NextCursor);

    private async Task<(int Added, int Updated, int Unsubscribed)> PullAsync(HttpClient client, EdmList list, CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var since = list.LastSyncedAt?.Subtract(Overlap);
        string? cursor = null;
        int added = 0, updated = 0, unsubscribed = 0;

        do
        {
            var query = new List<string>();
            if (since is { } s) query.Add($"updatedSince={Uri.EscapeDataString(s.ToString("O"))}");
            if (cursor is not null) query.Add($"cursor={Uri.EscapeDataString(cursor)}");
            var url = "api/internal/marketing-contacts" + (query.Count > 0 ? "?" + string.Join('&', query) : "");

            var page = await client.GetFromJsonAsync<PlatformPage>(url, Json, ct)
                ?? throw new JsonException("Empty response");

            var emails = page.Contacts.Select(c => EdmText.NormaliseEmail(c.Email)).OfType<string>().ToList();
            var externalIds = page.Contacts.Select(c => c.ExternalId).OfType<string>().ToList();
            var existing = await _context.EdmContacts
                .Where(c => c.ListId == list.Id && (emails.Contains(c.Email) || (c.ExternalId != null && externalIds.Contains(c.ExternalId))))
                .ToListAsync(ct);
            var byExternal = existing.Where(c => c.ExternalId != null).GroupBy(c => c.ExternalId!).ToDictionary(g => g.Key, g => g.First());
            var byEmail = existing.ToDictionary(c => c.Email);
            var suppressed = await _subscriptions.SuppressedStatusesAsync(list.SenderId, emails, ct);

            foreach (var incoming in page.Contacts)
            {
                var email = EdmText.NormaliseEmail(incoming.Email);
                if (email is null) continue;

                var contact = (incoming.ExternalId is { } ext ? byExternal.GetValueOrDefault(ext) : null) ?? byEmail.GetValueOrDefault(email);
                if (contact is null)
                {
                    if (!incoming.MarketingConsent) continue;
                    var status = suppressed.GetValueOrDefault(email) == EdmContactStatus.Bounced ? EdmContactStatus.Bounced : EdmContactStatus.Subscribed;
                    contact = new EdmContact
                    {
                        Id = Guid.NewGuid(),
                        ListId = list.Id,
                        Email = email,
                        ExternalId = incoming.ExternalId,
                        FirstName = incoming.FirstName,
                        LastName = incoming.LastName,
                        Status = status,
                        StatusChangedAt = incoming.UpdatedAt,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    };
                    _context.EdmContacts.Add(contact);
                    byEmail[email] = contact;
                    added++;
                    continue;
                }

                // A platform change older than ours loses, and a pending write-back is about to tell
                // the platform our side anyway. Bounces are never undone by a sync.
                var platformIsNewer = !contact.WritebackPending && (contact.StatusChangedAt is null || incoming.UpdatedAt > contact.StatusChangedAt);
                if (!incoming.MarketingConsent && contact.Status == EdmContactStatus.Subscribed && platformIsNewer)
                {
                    contact.Status = EdmContactStatus.Unsubscribed;
                    contact.StatusChangedAt = incoming.UpdatedAt;
                    unsubscribed++;
                }
                else if (incoming.MarketingConsent && contact.Status == EdmContactStatus.Unsubscribed && platformIsNewer)
                {
                    contact.Status = EdmContactStatus.Subscribed;
                    contact.StatusChangedAt = incoming.UpdatedAt;
                }

                if (contact.Email != email && !byEmail.ContainsKey(email)) contact.Email = email;
                contact.ExternalId ??= incoming.ExternalId;
                contact.FirstName = incoming.FirstName ?? contact.FirstName;
                contact.LastName = incoming.LastName ?? contact.LastName;
                contact.UpdatedAt = DateTime.UtcNow;
                updated++;
            }

            await _context.SaveChangesAsync(ct);
            cursor = page.NextCursor;
        } while (cursor is not null);

        list.LastSyncedAt = startedAt;
        list.UpdatedAt = DateTime.UtcNow;
        return (added, updated, unsubscribed);
    }

    private async Task<EdmSyncResult> FailAsync(EdmList list, string error, CancellationToken ct)
    {
        list.LastSyncError = error.Length > 1000 ? error[..1000] : error;
        await _context.SaveChangesAsync(ct);
        return new EdmSyncResult(0, 0, 0, 0, error);
    }
}
