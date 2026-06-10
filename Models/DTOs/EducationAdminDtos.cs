namespace Panwar.Api.Models.DTOs;

/// <summary>Create/update an education page.</summary>
public sealed class EducationPageWriteRequest
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public int? SortOrder { get; set; }
}

/// <summary>Create/update a chart within a page.</summary>
public sealed class EducationChartWriteRequest
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public int? SortOrder { get; set; }
}

/// <summary>Create/update a series (one coloured bar / legend entry).</summary>
public sealed class EducationSeriesWriteRequest
{
    public string? Label { get; set; }
    public string? Color { get; set; }
    public int? SortOrder { get; set; }
}

public sealed class EducationPointWrite
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Value { get; set; }
}

/// <summary>Full replace of a series' monthly completion points.</summary>
public sealed class EducationSeriesDataRequest
{
    public List<EducationPointWrite> Points { get; set; } = new();
}

/// <summary>Create/update a bar annotation.</summary>
public sealed class EducationAnnotationWriteRequest
{
    public Guid SeriesId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string? Text { get; set; }
}

/// <summary>Create/update an education asset (one row of the page's detail table).</summary>
public sealed class EducationAssetWriteRequest
{
    public string? GroupLabel { get; set; }
    public string? Brand { get; set; }
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public DateOnly? Expiry { get; set; }
    /// <summary>On update, true clears the expiry (a null Expiry alone means "unchanged").</summary>
    public bool? ClearExpiry { get; set; }
    public int? SortOrder { get; set; }
}

public sealed class EducationAssetValueWrite
{
    public string Status { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Value { get; set; }
}

/// <summary>Full replace of an asset's monthly status values.</summary>
public sealed class EducationAssetValuesRequest
{
    public List<EducationAssetValueWrite> Values { get; set; } = new();
}
