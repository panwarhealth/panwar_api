namespace Panwar.Api.Models;

public class CourseAssistantJob
{
    public Guid Id { get; set; }
    public required string Status { get; set; }
    public string? Message { get; set; }
    public string? Reply { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
