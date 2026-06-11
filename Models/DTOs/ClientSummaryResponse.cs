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
    /// <summary>True when the window has no actuals — the dashboard shows a plan, not results.</summary>
    bool IsPlan,
    /// <summary>Analyst-written summary for the window's end year; null when none exists.</summary>
    YearSummaryDto? Summary,
    /// <summary>Per-client toggle: render the monthly touchpoints-by-brand chart.</summary>
    bool ShowBrandMonthlyChart,
    /// <summary>Per-client toggle: render the touchpoints-vs-engagements-by-publisher chart.</summary>
    bool ShowPublisherChart,
    /// <summary>Monthly in-window metrics per brand for the brand chart; empty when disabled or planning.</summary>
    IReadOnlyList<BrandMonthlyDto> MonthlyByBrand,
    /// <summary>Every placement as its own row (the workbook's FY25 Summary by Asset), grouped client-side by brand.</summary>
    IReadOnlyList<AssetRowDto> ByAsset);

public sealed record YearSummaryDto(int Year, string Text);

/// <summary>One brand's in-window monthly metric series (for the overview brand chart).</summary>
public sealed record BrandMonthlyDto(
    string Label,
    string BrandSlug,
    IReadOnlyList<DashboardMonthDto> Months);

public sealed record ClientSummaryClientDto(Guid Id, string Name, string Slug);

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
    string PublisherName,
    string Objective,
    string TemplateCode,
    decimal MediaCost,
    decimal CpdInvestmentCost,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyDictionary<string, decimal> TargetMetrics);
