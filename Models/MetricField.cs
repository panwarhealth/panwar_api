namespace Panwar.Api.Models;

/// <summary>
/// A single metric tracked by a template, e.g. "impressions" / "clicks" / "ctr"
/// for the digital_display template. Calculated fields (CTR, CPM, CPC, etc.)
/// have IsCalculated=true and are not stored as actuals — they're derived in views.
/// </summary>
public class MetricField
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public required string Key { get; set; }       // e.g. "impressions", "clicks"
    public required string Label { get; set; }     // e.g. "Impressions", "Clicks"
    public string? Unit { get; set; }              // e.g. "%", "AUD", null for counts
    public bool IsCalculated { get; set; }
    public string? CalcFormula { get; set; }       // human-readable formula, e.g. "clicks / impressions"
    public int SortOrder { get; set; }

    public MetricTemplate Template { get; set; } = null!;
}
