namespace Panwar.Api.Models;

/// <summary>
/// One month's actual value for a single metric on a placement. The Note column
/// captures per-month context like "1 send (5 Mar)" or "1 send (4 Jun) — 242 CPD".
/// Multi-year supported (the workbook's education sheet has data back to 2023).
/// </summary>
public class PlacementActual
{
    public Guid Id { get; set; }
    public Guid PlacementId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public required string MetricKey { get; set; }
    public decimal Value { get; set; }
    public string? Note { get; set; }

    public Placement Placement { get; set; } = null!;
}
