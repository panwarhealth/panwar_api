namespace Panwar.Api.Models.DTOs;

/// <summary>
/// Lightweight education-page entry for the picker / nav. The trailing
/// aggregates feed the client overview cards; defaults keep the admin/list
/// call sites that don't compute them compiling unchanged.
/// </summary>
public sealed record EducationPageSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    int SortOrder,
    int ChartCount,
    int ModuleCount = 0,
    int AssetCount = 0,
    decimal Completions = 0);

public sealed record EducationPagesResponse(IReadOnlyList<EducationPageSummaryDto> Pages);

/// <summary>
/// A full education page: its charts, each with series (coloured bars + monthly
/// completion points) and staff annotations. Used for both the client dashboard
/// (scoped to a month window) and the employee editor (unwindowed). Points and
/// annotations outside the window are omitted on the client read.
/// </summary>
public sealed record EducationPageResponse(
    EducationPageSummaryDto Page,
    DashboardPeriodDto Period,
    IReadOnlyList<EducationChartDto> Charts,
    IReadOnlyList<EducationAssetDto> Assets);

public sealed record EducationChartDto(
    Guid Id,
    string Title,
    string? Subtitle,
    int SortOrder,
    IReadOnlyList<EducationSeriesDto> Series,
    IReadOnlyList<EducationAnnotationDto> Annotations);

public sealed record EducationSeriesDto(
    Guid Id,
    string Label,
    string? Color,
    int SortOrder,
    IReadOnlyList<EducationPointDto> Points);

public sealed record EducationPointDto(int Year, int Month, decimal Value);

public sealed record EducationAnnotationDto(
    Guid Id,
    Guid SeriesId,
    int Year,
    int Month,
    string Text);

/// <summary>
/// One row of the page's detail table (the workbook's per-asset education
/// table): metadata plus one monthly series per status (Completed / Enrolled /
/// Views). Points are window-filtered on the client read; Total sums them.
/// </summary>
public sealed record EducationAssetDto(
    Guid Id,
    string GroupLabel,
    string? Brand,
    string? Type,
    string Title,
    string? Author,
    DateOnly? Expiry,
    int SortOrder,
    IReadOnlyList<EducationAssetStatusDto> Statuses);

public sealed record EducationAssetStatusDto(
    string Status,
    IReadOnlyList<EducationPointDto> Points,
    decimal Total);
