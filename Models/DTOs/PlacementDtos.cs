namespace Panwar.Api.Models.DTOs;

/// <summary>
/// Lightweight placement row for the card grid. Artwork is the raw R2 object key
/// (the editor resolves a presigned view URL via the detail endpoint).
/// </summary>
public sealed record PlacementListItemDto(
    Guid Id,
    Guid BrandId, string BrandName,
    Guid AudienceId, string AudienceName,
    Guid PublisherId, string PublisherName,
    Guid TemplateId, string TemplateCode,
    int Year,
    string Name,
    string Objective,
    string? AssetType,
    string? CreativeCode,
    string? OsCode,
    string? ArtworkUrl,
    int[] LiveMonths,
    decimal MediaCost,
    decimal? PlannedMediaCost,
    decimal? CpdInvestmentCost,
    bool IsBonus,
    bool IsCpdPackage);

/// <summary>
/// Full placement for the edit form, including KPI targets and monthly actuals.
/// <see cref="ArtworkViewUrl"/> is a short-lived presigned GET URL for previewing
/// the artwork; null when the placement has no artwork.
/// </summary>
public sealed record PlacementDetailDto(
    Guid Id,
    Guid BrandId, string BrandName,
    Guid AudienceId, string AudienceName,
    Guid PublisherId, string PublisherName,
    Guid TemplateId, string TemplateCode, string TemplateName,
    int Year,
    string Name,
    string Objective,
    string? AssetType,
    string? CreativeCode,
    string? OsCode,
    string? UtmUrl,
    string? ArtworkUrl,
    string? ArtworkViewUrl,
    string? Comments,
    string? Notes,
    int[] LiveMonths,
    decimal MediaCost,
    decimal? PlannedMediaCost,
    decimal? CpdInvestmentCost,
    bool IsBonus,
    bool IsCpdPackage,
    decimal? Circulation,
    int? PlacementsCount,
    Guid? TargetCourseId,
    IReadOnlyList<PlacementKpiDto> Kpis,
    IReadOnlyList<PlacementActualDto> Actuals);

public sealed record PlacementKpiDto(string MetricKey, decimal TargetValue);

public sealed record PlacementActualDto(int Year, int Month, string MetricKey, decimal Value, string? Note);

/// <summary>Create/update payload for a placement's core fields.</summary>
public class PlacementWriteRequest
{
    public Guid BrandId { get; set; }
    public Guid AudienceId { get; set; }
    public Guid PublisherId { get; set; }
    public Guid TemplateId { get; set; }
    public int Year { get; set; }
    public string Name { get; set; } = "";
    public string Objective { get; set; } = "awareness";
    public string? AssetType { get; set; }
    public string? CreativeCode { get; set; }
    public string? OsCode { get; set; }
    public string? UtmUrl { get; set; }
    public string? ArtworkUrl { get; set; }     // R2 object key from the artwork-upload-url endpoint
    public string? Comments { get; set; }
    public string? Notes { get; set; }
    public int[] LiveMonths { get; set; } = Array.Empty<int>();
    public decimal MediaCost { get; set; }
    public decimal? PlannedMediaCost { get; set; }
    public decimal? CpdInvestmentCost { get; set; }
    public bool IsBonus { get; set; }
    public bool IsCpdPackage { get; set; }
    public decimal? Circulation { get; set; }
    public int? PlacementsCount { get; set; }
    public Guid? TargetCourseId { get; set; }
}

/// <summary>Bulk replace of a placement's KPI targets.</summary>
public class PlacementKpiWriteRequest
{
    public List<PlacementKpiDto> Kpis { get; set; } = new();
}

/// <summary>Upsert of monthly actuals (existing months not in the payload are left untouched).</summary>
public class PlacementActualsWriteRequest
{
    public List<PlacementActualDto> Actuals { get; set; } = new();
}

public class ArtworkUploadUrlRequest
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
}

public sealed record ArtworkUploadUrlResponse(string UploadUrl, string ObjectKey);

/// <summary>Carry this year's placements forward into a new reporting year.</summary>
public class ClonePlacementYearRequest
{
    public int FromYear { get; set; }
    public int ToYear { get; set; }
}

public sealed record ClonePlacementYearResponse(int Created, int Skipped);
