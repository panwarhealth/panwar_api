namespace Panwar.Api.Models;

/// <summary>
/// A trackable URL with UTM parameters. Optional links to placement/course
/// because some UTMs predate placements (Maria sometimes generates them in advance).
/// First-party click data — distinct from publisher impression/click reporting.
/// </summary>
public class UtmLink
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public required string Destination { get; set; }     // "Reckitt HCP Portal" / "AJP Article: Reflux Guideline"
    public string? AssetType { get; set; }
    public string? CreativeCode { get; set; }
    public string? OsCode { get; set; }
    public required string Url { get; set; }              // full URL with UTM params
    public int[] LiveMonths { get; set; } = Array.Empty<int>();
    public string? Notes { get; set; }
    public Guid? PlacementId { get; set; }
    public Guid? CourseId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Client Client { get; set; } = null!;
    public Placement? Placement { get; set; }
    public EducationCourse? Course { get; set; }

    public ICollection<UtmLinkClicks> MonthlyClicks { get; set; } = new List<UtmLinkClicks>();
}
