namespace Panwar.Api.Models;

/// <summary>
/// One module/course within a chart — a single coloured bar series and legend
/// entry (e.g. "Managing hand osteoarthritis - the latest evidence"). Holds its
/// monthly completion data points.
/// </summary>
public class EducationSeries
{
    public Guid Id { get; set; }
    public Guid EducationChartId { get; set; }
    public required string Label { get; set; }
    /// <summary>Optional bar colour (hex). Null falls back to a palette by index in the UI.</summary>
    public string? Color { get; set; }
    public int SortOrder { get; set; }

    public EducationChart Chart { get; set; } = null!;
    public ICollection<EducationDataPoint> DataPoints { get; set; } = new List<EducationDataPoint>();
}
