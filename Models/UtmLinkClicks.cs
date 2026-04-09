namespace Panwar.Api.Models;

/// <summary>
/// Per-month click count for a UTM link. First-party data sourced from QR/UTM tracking.
/// </summary>
public class UtmLinkClicks
{
    public Guid Id { get; set; }
    public Guid UtmLinkId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int ClickCount { get; set; }

    public UtmLink UtmLink { get; set; } = null!;
}
