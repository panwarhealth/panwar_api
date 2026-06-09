namespace Panwar.Api.Models;

/// <summary>
/// A staff-authored note anchored to a specific bar (series + month) on a chart.
/// Rendered as a floating bubble whose pointer aims at the top of that bar
/// (e.g. "Paracetamol CPD article live"). Created in the employee portal by
/// clicking a bar; read-only on the client dashboard.
/// </summary>
public class EducationAnnotation
{
    public Guid Id { get; set; }
    public Guid EducationChartId { get; set; }
    /// <summary>The bar's series. The annotation's pointer anchors to this series in <see cref="Month"/>.</summary>
    public Guid EducationSeriesId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public required string Text { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public EducationChart Chart { get; set; } = null!;
    public EducationSeries Series { get; set; } = null!;
    public AppUser? CreatedBy { get; set; }
}
