namespace Panwar.Api.Models;

/// <summary>
/// A single month's completion count for one series (the height of one bar).
/// Unique per (series, year, month).
/// </summary>
public class EducationDataPoint
{
    public Guid Id { get; set; }
    public Guid EducationSeriesId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Value { get; set; }

    public EducationSeries Series { get; set; } = null!;
}
