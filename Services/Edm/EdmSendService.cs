using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Edm;

/// <summary>Getting a campaign out the door: audience check, test send, send now / schedule, cancel.</summary>
public interface IEdmSendService
{
    Task<EdmAudienceDto?> AudienceAsync(Guid id, CancellationToken ct = default);
    Task<EdmTestSendResult?> TestSendAsync(Guid id, IReadOnlyList<string> emails, Guid userId, CancellationToken ct = default);
    Task<EdmCampaignDto?> SendAsync(Guid id, EdmSendRequest data, Guid userId, CancellationToken ct = default);
    Task<EdmCampaignDto?> CancelAsync(Guid id, CancellationToken ct = default);

    /// <summary>Moves a campaign to Sending and snapshots its recipients. Used by Send now and the scheduler.</summary>
    Task StartAsync(EdmCampaign campaign, CancellationToken ct = default);
}

public class EdmSendService : IEdmSendService
{
    private const int MaxTestRecipients = 5;

    private readonly AppDbContext _context;
    private readonly EdmCampaignQueries _queries;
    private readonly IEdmSubscriptionService _subscriptions;
    private readonly IEdmMailSender _mail;
    private readonly IConfiguration _configuration;

    public EdmSendService(AppDbContext context, EdmCampaignQueries queries, IEdmSubscriptionService subscriptions,
        IEdmMailSender mail, IConfiguration configuration)
    {
        _context = context;
        _queries = queries;
        _subscriptions = subscriptions;
        _mail = mail;
        _configuration = configuration;
    }

    public async Task<EdmAudienceDto?> AudienceAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await _queries.LoadAsync(id, tracking: false, ct);
        if (campaign?.List is null || campaign.SenderId is not { } senderId) return null;
        var total = await _context.EdmContacts.CountAsync(c => c.ListId == campaign.List.Id, ct);
        var sendable = await _subscriptions.Sendable(campaign.List.Id, senderId).CountAsync(ct);
        return new EdmAudienceDto(campaign.List.Name, total, sendable, total - sendable);
    }

    public async Task<EdmTestSendResult?> TestSendAsync(Guid id, IReadOnlyList<string> emails, Guid userId, CancellationToken ct = default)
    {
        var campaign = await _queries.LoadAsync(id, tracking: false, ct);
        if (campaign is null) return null;
        EdmCampaignQueries.RequireReady(campaign);

        var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        var userEmail = user.Email.ToLowerInvariant();
        var targets = emails.Count == 0
            ? [userEmail]
            : emails.Select(e => EdmText.NormaliseEmail(e) ?? throw new EdmValidationException($"{e} isn't a valid email address")).Distinct().ToList();
        if (targets.Count > MaxTestRecipients) throw new EdmValidationException($"Send a test to {MaxTestRecipients} addresses at most");

        var sender = campaign.Sender!;
        foreach (var to in targets)
        {
            // Personalise with the real contact when the tester is on the list, otherwise their own name.
            var contact = await _context.EdmContacts.AsNoTracking().FirstOrDefaultAsync(c => c.ListId == campaign.ListId && c.Email == to, ct);
            var first = contact?.FirstName ?? (to == userEmail ? user.Name?.Split(' ')[0] : null);
            var built = EdmMessageBuilder.Build(campaign, sender, new EdmMessageBuilder.Personalisation(
                to, first, contact?.LastName, EdmUrls.Unsubscribe(_configuration, null), null));
            await _mail.SendAsync(new EdmOutgoingMessage(sender.FromAddress, sender.ReplyTo, to,
                $"[Test] {built.Subject}", built.Html, built.Text, null), ct);
        }
        return new EdmTestSendResult(targets);
    }

    public async Task<EdmCampaignDto?> SendAsync(Guid id, EdmSendRequest data, Guid userId, CancellationToken ct = default)
    {
        var campaign = await _queries.LoadAsync(id, tracking: true, ct);
        if (campaign is null) return null;
        EdmCampaignQueries.RequireEditable(campaign);
        EdmCampaignQueries.RequireReady(campaign);

        var sendable = await _subscriptions.Sendable(campaign.List!.Id, campaign.Sender!.Id).CountAsync(ct);
        if (sendable == 0) throw new EdmValidationException("Nobody on this list can be sent to");
        if (data.ConfirmCount != sendable)
            throw new EdmValidationException($"The list now has {sendable:N0} people to send to. Check the number and confirm again");

        campaign.SentBy = userId;
        campaign.UpdatedAt = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(data.ScheduleAt))
        {
            campaign.ScheduledFor = null;
            await StartAsync(campaign, ct);
        }
        else
        {
            var at = EdmTime.ParseSydney(data.ScheduleAt) ?? throw new EdmValidationException("Pick a valid date and time");
            if (at < DateTime.UtcNow.AddMinutes(2)) throw new EdmValidationException("Pick a time at least a couple of minutes from now");
            campaign.Status = EdmCampaignStatus.Scheduled;
            campaign.ScheduledFor = at;
            await _context.SaveChangesAsync(ct);
        }
        return await _queries.ToDtoAsync(campaign, ct);
    }

    public async Task StartAsync(EdmCampaign campaign, CancellationToken ct = default)
    {
        var listId = campaign.ListId ?? throw new EdmValidationException("The campaign has no list");
        var senderId = campaign.SenderId ?? throw new EdmValidationException("The campaign has no sender");

        var contacts = await _subscriptions.Sendable(listId, senderId)
            .Select(c => new { c.Id, c.Email, c.FirstName, c.LastName })
            .ToListAsync(ct);

        _context.EdmRecipients.AddRange(contacts.Select(c => new EdmRecipient
        {
            Id = Guid.NewGuid(),
            CampaignId = campaign.Id,
            ContactId = c.Id,
            Email = c.Email,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Status = EdmRecipientStatus.Pending,
        }));

        campaign.Status = EdmCampaignStatus.Sending;
        campaign.StartedAt = DateTime.UtcNow;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<EdmCampaignDto?> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await _queries.LoadAsync(id, tracking: true, ct);
        if (campaign is null) return null;

        switch (campaign.Status)
        {
            case EdmCampaignStatus.Scheduled:
                // Back to a draft so it can be fixed and rescheduled.
                campaign.Status = EdmCampaignStatus.Draft;
                campaign.ScheduledFor = null;
                break;
            case EdmCampaignStatus.Sending:
                // Stop mid-send: whoever hasn't been sent to yet won't be.
                await _context.EdmRecipients
                    .Where(r => r.CampaignId == id && r.Status == EdmRecipientStatus.Pending)
                    .ExecuteDeleteAsync(ct);
                campaign.Status = EdmCampaignStatus.Cancelled;
                campaign.CancelledAt = DateTime.UtcNow;
                campaign.CompletedAt = DateTime.UtcNow;
                break;
            default:
                throw new EdmValidationException("Only scheduled or sending campaigns can be cancelled");
        }

        campaign.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return await _queries.ToDtoAsync(campaign, ct);
    }
}
