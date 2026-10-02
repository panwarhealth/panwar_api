namespace Panwar.Api.Models;

public class TrackedLink
{
    public Guid Id { get; set; }
    public required string DestinationUrl { get; set; }
    public required string CampaignId { get; set; }
    public string? ClientName { get; set; }
    public required string Source { get; set; }
    public required string Medium { get; set; }
    public string? Content { get; set; }
    public required string Url { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AppUser Creator { get; set; } = null!;
}
