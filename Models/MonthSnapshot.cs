using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

/// <summary>
/// The Draft → Published → ClientApproved | ClientQueried workflow gate
/// for one brand×audience combination for a given month. Clients only
/// see months in Published or ClientApproved status.
/// </summary>
public class MonthSnapshot
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid AudienceId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public MonthSnapshotStatus Status { get; set; }
    public string? Notes { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Brand Brand { get; set; } = null!;
    public Audience Audience { get; set; } = null!;
}
