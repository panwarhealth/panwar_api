namespace Panwar.Api.Models;

/// <summary>
/// Per-month completion tracking for a CPD course.
/// </summary>
public class EducationCourseStatus
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int CompleteCount { get; set; }
    public int PendingCount { get; set; }

    public EducationCourse Course { get; set; } = null!;
}
