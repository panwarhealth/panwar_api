namespace Panwar.Api.Models;

/// <summary>
/// Role assignment for a user. For employees, role values mirror Entra group
/// membership (admin, dashboard_editor, dashboard_viewer, etc.). For clients,
/// roles are usually just empty — they're scoped via ClientId.
/// </summary>
public class UserRole
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Role { get; set; }
    public DateTime CreatedAt { get; set; }

    public AppUser User { get; set; } = null!;
}
