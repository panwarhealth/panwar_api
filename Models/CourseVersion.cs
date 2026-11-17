namespace Panwar.Api.Models;

public class CourseVersion
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public Guid SavedBy { get; set; }
    public DateTime SavedAt { get; set; }

    public Course Course { get; set; } = null!;
    public AppUser Saver { get; set; } = null!;
}
