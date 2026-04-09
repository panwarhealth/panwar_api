namespace Panwar.Api.Models;

/// <summary>
/// Append-only audit log of every write operation. Before/after state stored
/// as JSONB so we can show "what changed" without joining back to the entity.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Action { get; set; }       // create | update | delete | publish | approve | query
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
