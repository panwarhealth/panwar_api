namespace Panwar.Api.Models;

/// <summary>
/// The "Expected" target value for a single metric on a placement.
/// Defaults from ClientPublisherBaseline on creation; overridable per placement.
/// </summary>
public class PlacementKpi
{
    public Guid Id { get; set; }
    public Guid PlacementId { get; set; }
    public required string MetricKey { get; set; }
    public decimal TargetValue { get; set; }

    public Placement Placement { get; set; } = null!;
}
