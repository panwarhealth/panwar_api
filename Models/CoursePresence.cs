namespace Panwar.Api.Models;

public class CoursePresence
{
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; }
    public string? PageId { get; set; }
    public DateTime SeenAt { get; set; }

    public Course Course { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}
