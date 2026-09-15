namespace Panwar.Api.Models.DTOs;

public sealed record EducationPageSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    int SortOrder,
    int ChartCount,
    int AssetCount = 0,
    decimal Completions = 0);

public sealed record EducationPagesResponse(IReadOnlyList<EducationPageSummaryDto> Pages);

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
    IReadOnlyList<string> GroupLabels,
    IReadOnlyList<EducationSeriesDto> BrandSeries,
    IReadOnlyList<EducationSeriesDto> ActivitySeries,
    IReadOnlyList<EducationAnnotationDto> Annotations);

public sealed record EducationSeriesDto(
    string Id,
    string Label,
    string? Color,
    IReadOnlyList<EducationPointDto> Points);

public sealed record EducationPointDto(int Year, int Month, decimal Value);

public sealed record EducationAnnotationDto(
    Guid Id,
    string Brand,
    int Year,
    int Month,
    string Text);

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
