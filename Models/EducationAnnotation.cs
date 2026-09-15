namespace Panwar.Api.Models;

public class EducationAnnotation
{
    public Guid Id { get; set; }
    public Guid EducationChartId { get; set; }
    public required string Brand { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public required string Text { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public EducationChart Chart { get; set; } = null!;
    public AppUser? CreatedBy { get; set; }
}
