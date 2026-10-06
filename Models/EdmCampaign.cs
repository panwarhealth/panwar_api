using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

/// <summary>
/// HtmlBody is the uploaded build with image paths rewritten to the public R2 bucket and the
/// original preheader stripped out. Everything per-recipient (merge tags, preheader, open pixel,
/// unsubscribe link) is applied at send time by EdmMessageBuilder, so the stored HTML stays reusable.
/// </summary>
public class EdmCampaign
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? CampaignCode { get; set; }
    public Guid? ListId { get; set; }
    public Guid? SenderId { get; set; }
    public string? FromName { get; set; }
    public string? Subject { get; set; }
    public string? PreviewText { get; set; }
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
    public string? SourceFileName { get; set; }
    public EdmCampaignStatus Status { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? SentBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public EdmList? List { get; set; }
    public EdmSender? Sender { get; set; }
    public AppUser Creator { get; set; } = null!;
    public AppUser? SentByUser { get; set; }
    public ICollection<EdmRecipient> Recipients { get; set; } = new List<EdmRecipient>();
}
