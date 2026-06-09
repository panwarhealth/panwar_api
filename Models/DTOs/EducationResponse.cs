namespace Panwar.Api.Models.DTOs;

/// <summary>
/// Lightweight education-page entry for the picker / nav.
/// </summary>
public sealed record EducationPageSummaryDto(Guid Id, string Name, string Slug, int SortOrder, int ChartCount);

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
    IReadOnlyList<EducationChartDto> Charts);

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
