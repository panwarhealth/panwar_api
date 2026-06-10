namespace Panwar.Api.Models;

/// <summary>
/// A single month's number for one status row of an education asset (e.g.
/// "Completed" Mar 2025 = 18). Unique per (asset, status, year, month).
/// </summary>
public class EducationAssetValue
{
    public Guid Id { get; set; }
    public Guid EducationAssetId { get; set; }

    /// <summary>Row label within the asset: "Completed", "Enrolled", "Views"…</summary>
    public required string Status { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Value { get; set; }

    public EducationAsset Asset { get; set; } = null!;
}
