namespace Panwar.Api.Models;

public class CpdInvestment
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid AudienceId { get; set; }
    public Guid PublisherId { get; set; }

    public int Year { get; set; }
    public required string Title { get; set; }
    public required string Format { get; set; }
    public decimal Cost { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    public Brand Brand { get; set; } = null!;
    public Audience Audience { get; set; } = null!;
    public Publisher Publisher { get; set; } = null!;
}
