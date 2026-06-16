namespace Panwar.Api.Models.DTOs;

public sealed record CpdInvestmentListItemDto(
    Guid Id,
    Guid BrandId, string BrandName,
    Guid AudienceId, string AudienceName,
    Guid PublisherId, string PublisherName,
    int Year,
    string Title,
    string Format,
    decimal Cost,
    string? Notes);

public sealed record CpdInvestmentListResponse(
    IReadOnlyList<CpdInvestmentListItemDto> Items,
    IReadOnlyList<int> Years);

public class CpdInvestmentWriteRequest
{
    public Guid BrandId { get; set; }
    public Guid AudienceId { get; set; }
    public Guid PublisherId { get; set; }
    public int Year { get; set; }
    public string Title { get; set; } = "";
    public string Format { get; set; } = "";
    public decimal Cost { get; set; }
    public string? Notes { get; set; }
}

public static class CpdFormats
{
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "article", "video", "podcast", "webinar", "research_paper",
    };
}
