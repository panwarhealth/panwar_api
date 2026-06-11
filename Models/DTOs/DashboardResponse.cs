namespace Panwar.Api.Models.DTOs;

/// <summary>
/// Response shape for GET /api/dashboards/{clientSlug}/{brandSlug}/{audienceSlug}.
/// One brand × one audience, scoped to a month range (the global date filter),
/// with rolled-up totals, a per-month breakdown, per-publisher breakdown and
/// per-placement detail (each with actuals + KPI targets + presigned artwork).
///
/// Metric values are exposed as a flexible <c>metricKey -&gt; value</c> dictionary so
/// the response can carry whichever metrics each placement template tracks
/// (impressions/clicks/views/sends/page_views/etc.) without a brittle column-per-metric
/// schema. Calculated metrics (CTR/CPM/CPC, engagement rate, cost-per-X) are derived
/// in the frontend from the raw fields.
/// </summary>
public sealed record DashboardResponse(
    DashboardBrandDto Brand,
    DashboardAudienceDto Audience,
    DashboardPeriodDto Period,
    DashboardTotalsDto Totals,
    IReadOnlyList<DashboardMonthDto> Monthly,
    IReadOnlyList<DashboardPublisherDto> Publishers,
    IReadOnlyList<DashboardPlacementDto> Placements,
    /// <summary>True when the window has no actuals — the dashboard shows a plan, not results.</summary>
    bool IsPlan);

public sealed record DashboardBrandDto(Guid Id, string Name, string Slug);

public sealed record DashboardAudienceDto(Guid Id, string Name, string Slug);

/// <summary>
/// The resolved month window for this response plus the full span of data that
/// exists for this brand × audience (so the UI can bound the filter). All values
/// are "YYYY-MM"; Available* are null when there are no actuals yet.
/// </summary>
public sealed record DashboardPeriodDto(string From, string To, string? AvailableFrom, string? AvailableTo);

/// <summary>Rollup across every placement in this brand × audience, within the window.</summary>
public sealed record DashboardTotalsDto(
    int PlacementCount,
    decimal MediaCost,
    decimal? PlannedMediaCost,
    decimal CpdInvestmentCost,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyDictionary<string, decimal> TargetMetrics);

/// <summary>One month's totals across every placement. Only months within the window are present.</summary>
public sealed record DashboardMonthDto(
    int Year,
    int Month,
    IReadOnlyDictionary<string, decimal> Metrics);

/// <summary>Per-publisher rollup, sorted by media cost descending.</summary>
public sealed record DashboardPublisherDto(
    Guid Id,
    string Name,
    string Slug,
    int PlacementCount,
    decimal MediaCost,
    decimal? PlannedMediaCost,
    decimal CpdInvestmentCost,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyDictionary<string, decimal> TargetMetrics);

/// <summary>
/// One placement with its windowed actuals, KPI targets and presigned artwork.
/// For eDMs that were duplicated across multiple sends, this is the merged card:
/// summed actuals/targets and the list of in-window send dates.
/// </summary>
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
    decimal? PlannedMediaCost,
    decimal? CpdInvestmentCost,
    string? ArtworkViewUrl,
    int[] LiveMonths,
    /// <summary>Storable metric keys in template SortOrder — use to drive display order.</summary>
    string[] MetricKeys,
    IReadOnlyDictionary<string, decimal> Totals,
    IReadOnlyDictionary<string, decimal> Targets,
    /// <summary>eDM send date / education range start ("YYYY-MM-DD"); null for LiveMonths placements.</summary>
    string? StartDate,
    /// <summary>Education range end ("YYYY-MM-DD"); null otherwise.</summary>
    string? EndDate,
    /// <summary>eDM or education sub-category (snake_case); null when not applicable.</summary>
    string? Subcategory,
    /// <summary>In-window send dates for a merged eDM group ("YYYY-MM-DD"), sorted; empty otherwise.</summary>
    IReadOnlyList<string> SendDates,
    /// <summary>Analyst's findings/commentary for this placement (the workbook's per-placement comments); null when none.</summary>
    string? Comments);
