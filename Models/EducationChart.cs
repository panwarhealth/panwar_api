namespace Panwar.Api.Models;

public class EducationChart
{
    public Guid Id { get; set; }
    public Guid EducationPageId { get; set; }
    public required string Title { get; set; }
    public string? Subtitle { get; set; }
    public int SortOrder { get; set; }
    public string[] GroupLabels { get; set; } = Array.Empty<string>();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public EducationPage Page { get; set; } = null!;
    public ICollection<EducationAnnotation> Annotations { get; set; } = new List<EducationAnnotation>();
}
