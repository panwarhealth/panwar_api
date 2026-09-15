namespace Panwar.Api.Models.DTOs;

/// <summary>
/// Response for GET /api/dashboards/{clientSlug}/summary — the client-level
/// overview (the workbook's OVERVIEW + FY-summary-by-asset, modernised). Rolls up
/// every brand × audience for the client within the month window: headline totals,
/// a per-brand×audience breakdown (each drillable into its performance page) and a
/// per-publisher breakdown. Spend figures double as the cost summary (Media + CPD
/// vs Planned). Metrics are flexible <c>metricKey -&gt; value</c> maps; the frontend
/// derives touchpoints/engagements/rates.
/// </summary>
public sealed record ClientSummaryResponse(
    ClientSummaryClientDto Client,
    DashboardPeriodDto Period,
    DashboardTotalsDto Totals,
    IReadOnlyList<SummaryRowDto> ByBrandAudience,
    IReadOnlyList<SummaryRowDto> ByPublisher,
    /// <summary>Per-category rollup (Digital / Print / Education) — same shape as ByPublisher.</summary>
    IReadOnlyList<SummaryRowDto> ByCategory,
    /// <summary>Per-digital-format rollup (eDM Solus/Spon Con/Banners, Digital Display, Spon Con) — digital placements only.</summary>
    IReadOnlyList<SummaryRowDto> ByDigitalFormat,
    /// <summary>Brands present in the window (for the performance-card brand filter), with display colour.</summary>
    IReadOnlyList<BrandRefDto> Brands,
    /// <summary>True when the window has no actuals — the dashboard shows a plan, not results.</summary>
    bool IsPlan,
    /// <summary>Analyst-written summary for the window's end year; null when none exists.</summary>
    YearSummaryDto? Summary,
    /// <summary>Per-client toggle: render the monthly touchpoints-by-brand chart.</summary>
    bool ShowBrandMonthlyChart,
    /// <summary>Per-client toggle: render the touchpoints-vs-engagements-by-publisher chart.</summary>
    bool ShowPublisherChart,
    /// <summary>Every placement as its own row (the workbook's FY25 Summary by Asset), grouped client-side by brand.</summary>
    IReadOnlyList<AssetRowDto> ByAsset,
    IReadOnlyList<DashboardPlacementDto> Placements);

public sealed record YearSummaryDto(int Year, string Text);

public sealed record ClientSummaryClientDto(Guid Id, string Name, string Slug);

public sealed record BrandRefDto(string Slug, string Name, string? Color);

/// <summary>
/// One rollup row. For brand×audience rows <see cref="BrandSlug"/>/<see cref="AudienceSlug"/>
/// are set so the UI can link into the performance page; for publisher rows they are null.
/// </summary>
public sealed record SummaryRowDto(
    string Label,
    string? BrandSlug,
    string? AudienceSlug,
    int PlacementCount,
    decimal MediaCost,
    decimal? PlannedMediaCost,
    decimal CpdInvestmentCost,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyDictionary<string, decimal> TargetMetrics);

/// <summary>
/// One placement as a single row for the FY25 Summary-by-Asset table.
/// <see cref="TemplateCode"/> lets the UI split touchpoints into the workbook's
/// Print vs Digital impression columns (print template → print, else digital).
/// </summary>
public sealed record AssetRowDto(
    string Name,
    string BrandName,
    string BrandSlug,
    string AudienceName,
    string AudienceSlug,
    string PublisherName,
    string PublisherSlug,
    string Objective,
    string TemplateCode,
    string MediaType,
    string? OsCode,
    int[] LiveMonths,
    string? StartDate,
    string? EndDate,
    IReadOnlyList<string> SendDates,
    decimal MediaCost,
    decimal CpdInvestmentCost,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyDictionary<string, decimal> TargetMetrics);
