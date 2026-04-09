namespace Panwar.Api.Services.Seed;

public interface IReckittSeedService
{
    /// <summary>
    /// Phase A: seed Reckitt + brands + audiences + all publishers + metric
    /// templates + baselines, plus all NUROFEN-Pharmacists placements (digital
    /// + print) with monthly actuals from the YTD Data sheet of the workbook.
    ///
    /// Idempotent — deletes any existing Reckitt data before re-seeding.
    /// Returns a summary of what was inserted.
    /// </summary>
    Task<SeedSummary> SeedAsync(string workbookPath, CancellationToken cancellationToken = default);
}

public class SeedSummary
{
    public int Brands { get; set; }
    public int Audiences { get; set; }
    public int Publishers { get; set; }
    public int MetricTemplates { get; set; }
    public int MetricFields { get; set; }
    public int Baselines { get; set; }
    public int Placements { get; set; }
    public int PlacementKpis { get; set; }
    public int PlacementActuals { get; set; }
    public List<string> Warnings { get; set; } = new();
}
