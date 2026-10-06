using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

/// <summary>
/// One address on one list. Email is stored lower-cased and unique per list. Unsubscribed and
/// bounced contacts stay on the list (so the counts and the history survive) but are never sent to.
/// WritebackPending marks a synced contact whose unsubscribe hasn't reached its platform yet.
/// </summary>
public class EdmContact
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public required string Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ExternalId { get; set; }
    public EdmContactStatus Status { get; set; }
    public DateTime? StatusChangedAt { get; set; }
    public bool WritebackPending { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public EdmList List { get; set; } = null!;
}
