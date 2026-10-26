namespace Panwar.Api.Models;

public class Course
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastExportedAt { get; set; }

    public AppUser Creator { get; set; } = null!;
    public AppUser Updater { get; set; } = null!;
}
