using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

/// <summary>
/// One row per address per campaign, snapshotted from the list when sending starts. The Id doubles
/// as the token in the open pixel and unsubscribe URLs (a random Guid is not guessable).
/// MessageId is the ACS operation id, which Event Grid delivery reports carry as messageId.
/// </summary>
public class EdmRecipient
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid? ContactId { get; set; }
    public required string Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public EdmRecipientStatus Status { get; set; }
    public string? MessageId { get; set; }
    public string? Error { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? FirstOpenedAt { get; set; }
    public int OpenCount { get; set; }
    public DateTime? UnsubscribedAt { get; set; }

    public EdmCampaign Campaign { get; set; } = null!;
    public EdmContact? Contact { get; set; }
}
