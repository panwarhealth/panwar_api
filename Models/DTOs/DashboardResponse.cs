namespace Panwar.Api.Models.DTOs;

/// <summary>
/// Response shape for GET /api/dashboards/{brandSlug}/{audienceSlug}.
/// One brand × one audience, with rolled-up totals, monthly breakdown, per-publisher
/// breakdown and per-placement detail (each with their KPIs and monthly actuals).
///
/// Metric values are exposed as a flexible <c>metricKey -&gt; value</c> dictionary so
/// the response can carry whichever metrics each placement template tracks
/// (impressions/clicks/views/sends/page_views/etc.) without a brittle column-per-metric
/// schema. Calculated metrics like CTR/CPM/CPV are NOT pre-computed by the API — the
/// frontend derives them from the raw fields it cares about.
/// </summary>
public sealed record DashboardResponse(
    DashboardBrandDto Brand,
    DashboardAudienceDto Audience,
    int Year,
    DashboardTotalsDto Totals,
    IReadOnlyList<DashboardMonthDto> Monthly,
    IReadOnlyList<DashboardPublisherDto> Publishers,
    IReadOnlyList<DashboardPlacementDto> Placements);

public sealed record DashboardBrandDto(Guid Id, string Name, string Slug);

public sealed record DashboardAudienceDto(Guid Id, string Name, string Slug);

/// <summary>YTD rollup across every placement in this brand × audience.</summary>
public sealed record DashboardTotalsDto(
    int PlacementCount,
    decimal MediaCost,
    IReadOnlyDictionary<string, decimal> Metrics);

/// <summary>One month's totals (across every placement). Months 1..12 always present.</summary>
public sealed record DashboardMonthDto(
    int Month,
    IReadOnlyDictionary<string, decimal> Metrics);

/// <summary>Per-publisher rollup, sorted by media cost descending.</summary>
public sealed record DashboardPublisherDto(
    Guid Id,
    string Name,
    string Slug,
    int PlacementCount,
    decimal MediaCost,
    IReadOnlyDictionary<string, decimal> Metrics);

/// <summary>One placement with its targets and YTD actuals.</summary>
public sealed record DashboardPlacementDto(
    Guid Id,
    string Name,
    string Objective,
    string TemplateCode,
    string PublisherName,
    string PublisherSlug,
    bool IsBonus,
    bool IsCpdPackage,
    decimal MediaCost,
    int[] LiveMonths,
    IReadOnlyDictionary<string, decimal> Totals,
    IReadOnlyDictionary<string, decimal> Targets);
