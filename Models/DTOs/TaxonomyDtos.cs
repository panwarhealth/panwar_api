namespace Panwar.Api.Models.DTOs;

public sealed record BrandDto(Guid Id, string Name, string Slug, int PlacementCount);
public sealed record AudienceDto(Guid Id, string Name, string Slug, int PlacementCount);

public class BrandWriteRequest
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
}

public class AudienceWriteRequest
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
}

public sealed record PublisherDto(
    Guid Id,
    string Name,
    string Slug,
    string? Website,
    IReadOnlyList<PublisherTemplateDto> Templates);

public sealed record PublisherTemplateDto(Guid TemplateId, string TemplateCode, string TemplateName);

public sealed record MetricTemplateDto(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyList<MetricFieldDto> Fields);

public sealed record MetricFieldDto(string Key, string Label, string? Unit);

public class PublisherWriteRequest
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Website { get; set; }
    public List<Guid> TemplateIds { get; set; } = new();
}

public sealed record BaselineDto(
    Guid Id,
    Guid PublisherId,
    string PublisherName,
    Guid TemplateId,
    string TemplateCode,
    string MetricKey,
    decimal Value,
    DateOnly EffectiveFrom,
    string? Note);

public class BaselineWriteRequest
{
    public Guid PublisherId { get; set; }
    public Guid TemplateId { get; set; }
    public string MetricKey { get; set; } = "";
    public decimal Value { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string? Note { get; set; }
}
