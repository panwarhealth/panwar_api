using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

/// <summary>
/// A CPD course / educational asset. Separate from Placement because courses
/// have their own multi-year lifecycle (launched/expires, completion tracking)
/// independent of the ads driving traffic to them.
/// </summary>
public class EducationCourse
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid AudienceId { get; set; }
    public Guid PublisherId { get; set; }
    public required string Title { get; set; }
    public EducationCourseType CourseType { get; set; }
    public string? Presenter { get; set; }
    public DateOnly LaunchedAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public decimal? CpdInvestmentCost { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Brand Brand { get; set; } = null!;
    public Audience Audience { get; set; } = null!;
    public Publisher Publisher { get; set; } = null!;

    public ICollection<EducationCourseStatus> MonthlyStatus { get; set; } = new List<EducationCourseStatus>();
}
