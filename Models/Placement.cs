using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

/// <summary>
/// One advertised media placement. The metric template determines which
/// metrics this placement tracks (see PlacementKpi / PlacementActual).
/// Type-specific fields (Circulation, PlacementsCount) are nullable for
/// non-print placements.
/// </summary>
public class Placement
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid AudienceId { get; set; }
    public Guid PublisherId { get; set; }
    public Guid TemplateId { get; set; }

    public required string Name { get; set; }
    public PlacementObjective Objective { get; set; }
    public string? AssetType { get; set; }            // banner, solus_edm, dps, ifc, etc.
    public string? CreativeCode { get; set; }         // "RB0686"
    public string? OsCode { get; set; }               // "RT-M-Zv9qDM" — not unique
    public string? UtmUrl { get; set; }
    public string? ArtworkUrl { get; set; }           // R2 key
    public string? Comments { get; set; }             // long-form markdown
    public string? Notes { get; set; }                // short internal notes

    public int[] LiveMonths { get; set; } = Array.Empty<int>();  // e.g. [3, 6] for Mar+Jun

    public decimal MediaCost { get; set; }
    public decimal? CpdInvestmentCost { get; set; }   // only for placements in a CPD package
    public bool IsBonus { get; set; }
    public bool IsCpdPackage { get; set; }

    // Print-only (nullable for everything else)
    public decimal? Circulation { get; set; }
    public int? PlacementsCount { get; set; }

    // Optional link to the CPD course this placement drives traffic to
    public Guid? TargetCourseId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    public Brand Brand { get; set; } = null!;
    public Audience Audience { get; set; } = null!;
    public Publisher Publisher { get; set; } = null!;
    public MetricTemplate Template { get; set; } = null!;
    public EducationCourse? TargetCourse { get; set; }

    public ICollection<PlacementKpi> Kpis { get; set; } = new List<PlacementKpi>();
    public ICollection<PlacementActual> Actuals { get; set; } = new List<PlacementActual>();
    public ICollection<PlacementComment> Discussion { get; set; } = new List<PlacementComment>();
}
