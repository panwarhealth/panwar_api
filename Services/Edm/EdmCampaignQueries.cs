using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Edm;

/// <summary>
/// Loading and shaping a campaign, plus the state guards. Shared by the campaign, send and report
/// services so each of those only holds its own behaviour.
/// </summary>
public class EdmCampaignQueries
{
    public static readonly EdmCampaignStats EmptyStats = new(0, 0, 0, 0, 0, 0, 0, 0);

    private readonly AppDbContext _context;

    public EdmCampaignQueries(AppDbContext context)
    {
        _context = context;
    }

    public Task<EdmCampaign?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = _context.EdmCampaigns.Include(c => c.List).Include(c => c.Sender)
            .Include(c => c.Creator).Include(c => c.SentByUser).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<EdmCampaignDto> ToDtoAsync(EdmCampaign c, CancellationToken ct)
    {
        var stats = (await StatsAsync(c.Id, ct)).GetValueOrDefault(c.Id, EmptyStats);
        var content = c.HtmlBody is null ? null : EdmContentProcessor.Analyse(c.HtmlBody, c.TextBody is not null);
        return new EdmCampaignDto(
            c.Id, c.Name, c.CampaignCode, c.Status.ToString(), c.ListId, c.List?.Name, c.SenderId,
            c.Sender is null ? null : EdmMapping.ToDto(c.Sender), c.Subject, c.PreviewText, c.SourceFileName, content,
            c.ScheduledFor, c.StartedAt, c.CompletedAt, c.CancelledAt,
            c.Creator.Name ?? c.Creator.Email, c.SentByUser is null ? null : c.SentByUser.Name ?? c.SentByUser.Email,
            c.CreatedAt, c.UpdatedAt, stats);
    }

    /// <summary>Recipient funnel per campaign; all campaigns when id is null.</summary>
    public async Task<Dictionary<Guid, EdmCampaignStats>> StatsAsync(Guid? campaignId, CancellationToken ct)
    {
        var query = _context.EdmRecipients.AsNoTracking();
        if (campaignId is { } id) query = query.Where(r => r.CampaignId == id);
        var rows = await query.GroupBy(r => r.CampaignId).Select(g => new
        {
            CampaignId = g.Key,
            Total = g.Count(),
            Pending = g.Count(r => r.Status == EdmRecipientStatus.Pending),
            Sent = g.Count(r => r.Status != EdmRecipientStatus.Pending),
            Delivered = g.Count(r => r.Status == EdmRecipientStatus.Delivered),
            Bounced = g.Count(r => r.Status == EdmRecipientStatus.Bounced),
            Failed = g.Count(r => r.Status == EdmRecipientStatus.Failed),
            Opened = g.Count(r => r.FirstOpenedAt != null),
            Unsubscribed = g.Count(r => r.UnsubscribedAt != null),
        }).ToListAsync(ct);
        return rows.ToDictionary(r => r.CampaignId, r => new EdmCampaignStats(
            r.Total, r.Pending, r.Sent, r.Delivered, r.Bounced, r.Failed, r.Opened, r.Unsubscribed));
    }

    public static void RequireEditable(EdmCampaign c)
    {
        if (c.Status is not (EdmCampaignStatus.Draft or EdmCampaignStatus.Scheduled))
            throw new EdmValidationException("This campaign has already gone out. Duplicate it to send again");
    }

    public static void RequireReady(EdmCampaign c)
    {
        if (c.List is null) throw new EdmValidationException("Pick a list");
        if (c.Sender is null) throw new EdmValidationException("Pick who it's from");
        if (string.IsNullOrWhiteSpace(c.HtmlBody)) throw new EdmValidationException("Add the email content");
        if (string.IsNullOrWhiteSpace(c.Subject)) throw new EdmValidationException("Add a subject line");
    }
}
