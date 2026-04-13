namespace Panwar.Api.Services;

/// <summary>
/// Calls Microsoft Graph API to manage Entra ID users and app role assignments
/// for the Employee SSO app registration. Uses client credentials (app-only).
/// </summary>
public interface IGraphService
{
    /// <summary>
    /// Lists all users in the tenant with their app role assignments for our app.
    /// </summary>
    Task<List<GraphUser>> GetUsersWithRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns an app role to a user. Returns the assignment ID.
    /// </summary>
    Task<string> AssignRoleAsync(string userObjectId, string roleValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an app role assignment from a user.
    /// </summary>
    Task RemoveRoleAsync(string assignmentId, CancellationToken cancellationToken = default);
}

public class GraphUser
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public List<GraphRoleAssignment> Roles { get; set; } = [];
}

public class GraphRoleAssignment
{
    public required string AssignmentId { get; set; }
    public required string RoleValue { get; set; }
    public required string RoleDisplayName { get; set; }
}
