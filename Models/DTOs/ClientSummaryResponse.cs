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
    YearSummaryDto? Summary);

public sealed record YearSummaryDto(int Year, string Text);

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
