namespace Panwar.Api.Models;

/// <summary>
/// One bar chart on an education page (e.g. "Pharmacist CPD Completions L12M").
/// Holds a set of series (one coloured bar per module/course) and the staff
/// annotations that float above specific bars.
/// </summary>
public class EducationChart
{
    public Guid Id { get; set; }
    public Guid EducationPageId { get; set; }
    public required string Title { get; set; }
    /// <summary>Optional descriptive subtitle shown under the chart title.</summary>
    public string? Subtitle { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public EducationPage Page { get; set; } = null!;
    public ICollection<EducationSeries> Series { get; set; } = new List<EducationSeries>();
    public ICollection<EducationAnnotation> Annotations { get; set; } = new List<EducationAnnotation>();
}
