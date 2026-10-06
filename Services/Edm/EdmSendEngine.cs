using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Edm;

public interface IEdmSendEngine
{
    Task RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Runs once a minute from the timer. Starts any scheduled campaigns that are due, then sends up to
/// EDM_SEND_PER_MINUTE pending recipients across all sending campaigns, oldest campaign first.
/// Each recipient is saved straight after its send so a crash never double-sends; anything still
/// Pending is picked up by the next tick. The per-minute budget matches the ACS quota (30/min on a
/// new resource until Microsoft raises it).
/// </summary>
public class EdmSendEngine : IEdmSendEngine
{
    private static readonly TimeSpan TickBudget = TimeSpan.FromSeconds(50);

    private readonly AppDbContext _context;
    private readonly IEdmSendService _send;
    private readonly IEdmMailSender _mail;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EdmSendEngine> _logger;

    public EdmSendEngine(AppDbContext context, IEdmSendService send, IEdmMailSender mail, IConfiguration configuration, ILogger<EdmSendEngine> logger)
    {
        _context = context;
        _send = send;
        _mail = mail;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await StartDueAsync(ct);

        var budget = int.TryParse(_configuration["EDM_SEND_PER_MINUTE"], out var b) && b > 0 ? b : 30;
        var clock = Stopwatch.StartNew();

        var sending = await _context.EdmCampaigns.Include(c => c.Sender)
            .Where(c => c.Status == EdmCampaignStatus.Sending)
            .OrderBy(c => c.StartedAt)
            .ToListAsync(ct);

        foreach (var campaign in sending)
        {
            if (budget <= 0 || clock.Elapsed > TickBudget) break;

            var batch = await _context.EdmRecipients.Include(r => r.Contact)
                .Where(r => r.CampaignId == campaign.Id && r.Status == EdmRecipientStatus.Pending)
                .OrderBy(r => r.Email)
                .Take(budget)
                .ToListAsync(ct);

            foreach (var recipient in batch)
            {
                if (clock.Elapsed > TickBudget) break;

                // Unsubscribed or bounced since the snapshot was taken: drop them rather than send.
                if (recipient.Contact is { Status: not EdmContactStatus.Subscribed })
                {
                    _context.EdmRecipients.Remove(recipient);
                    await _context.SaveChangesAsync(ct);
                    continue;
                }

                var unsubscribe = EdmUrls.Unsubscribe(_configuration, recipient.Id);
                var built = EdmMessageBuilder.Build(campaign, campaign.Sender!, new EdmMessageBuilder.Personalisation(
                    recipient.Email, recipient.FirstName, recipient.LastName, unsubscribe, EdmUrls.Pixel(_configuration, recipient.Id)));

                try
                {
                    recipient.MessageId = await _mail.SendAsync(new EdmOutgoingMessage(
                        campaign.Sender!.FromAddress, campaign.Sender.ReplyTo, recipient.Email,
                        built.Subject, built.Html, built.Text, unsubscribe), ct);
                    recipient.Status = EdmRecipientStatus.Sent;
                }
                catch (EdmThrottledException)
                {
                    _logger.LogWarning("ACS throttled the eDM send; stopping this tick");
                    budget = 0;
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "eDM send to recipient {RecipientId} failed", recipient.Id);
                    recipient.Status = EdmRecipientStatus.Failed;
                    recipient.Error = EdmText.Optional(ex.Message, 500);
                }

                recipient.SentAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                budget--;
            }
        }

        await CompleteFinishedAsync(ct);
    }

    private async Task StartDueAsync(CancellationToken ct)
    {
        var due = await _context.EdmCampaigns.Include(c => c.List).Include(c => c.Sender)
            .Where(c => c.Status == EdmCampaignStatus.Scheduled && c.ScheduledFor <= DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var campaign in due)
        {
            try
            {
                await _send.StartAsync(campaign, ct);
                _logger.LogInformation("Started scheduled eDM campaign {CampaignId}", campaign.Id);
            }
            catch (EdmValidationException ex)
            {
                // e.g. the list was deleted after scheduling. Put it back to draft so someone notices.
                _logger.LogWarning("Scheduled eDM campaign {CampaignId} couldn't start: {Reason}", campaign.Id, ex.Message);
                campaign.Status = EdmCampaignStatus.Draft;
                campaign.ScheduledFor = null;
                campaign.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }
        }
    }

    private async Task CompleteFinishedAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await _context.EdmCampaigns
            .Where(c => c.Status == EdmCampaignStatus.Sending
                && !_context.EdmRecipients.Any(r => r.CampaignId == c.Id && r.Status == EdmRecipientStatus.Pending))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, EdmCampaignStatus.Sent)
                .SetProperty(c => c.CompletedAt, now)
                .SetProperty(c => c.UpdatedAt, now), ct);
    }
}
