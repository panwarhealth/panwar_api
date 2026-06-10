namespace Panwar.Api.Models;

/// <summary>
/// One row of the workbook's education detail table: a single CPD asset
/// (article / podcast / webinar / module) on an education page, grouped under
/// a publisher block heading (e.g. "AP", "AJP", "Pharmacy Club", "HealthEd").
/// Monthly numbers live in <see cref="EducationAssetValue"/>, keyed by a
/// status label (Completed / Enrolled / Views).
/// </summary>
public class EducationAsset
{
    public Guid Id { get; set; }
    public Guid EducationPageId { get; set; }

    /// <summary>Publisher block heading the asset sits under.</summary>
    public required string GroupLabel { get; set; }
    public string? Brand { get; set; }
    /// <summary>Asset type as the workbook labels it: Article, Podcast, Webinar, Module, Video…</summary>
    public string? Type { get; set; }
    public required string Title { get; set; }
    /// <summary>The workbook's "By" column - author or presenter.</summary>
    public string? Author { get; set; }
    public DateOnly? Expiry { get; set; }
    public int SortOrder { get; set; }

    public EducationPage Page { get; set; } = null!;
    public ICollection<EducationAssetValue> Values { get; set; } = new List<EducationAssetValue>();
}
