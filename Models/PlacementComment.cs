namespace Panwar.Api.Models;

/// <summary>
/// Threaded comments on a placement. Authored by either client users or
/// employees. ParentId is null for top-level threads.
/// </summary>
public class PlacementComment
{
    public Guid Id { get; set; }
    public Guid PlacementId { get; set; }
    public Guid AuthorUserId { get; set; }
    public Guid? ParentId { get; set; }
    public required string Body { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Placement Placement { get; set; } = null!;
    public AppUser Author { get; set; } = null!;
    public PlacementComment? Parent { get; set; }
    public ICollection<PlacementComment> Replies { get; set; } = new List<PlacementComment>();
}
