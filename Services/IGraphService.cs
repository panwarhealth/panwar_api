namespace Panwar.Api.Services;

public interface IGraphService
{
    Task<List<GraphUser>> GetUsersWithRolesAsync(CancellationToken cancellationToken = default);
    Task<string> AssignRoleAsync(string userObjectId, string roleValue, CancellationToken cancellationToken = default);
    Task RemoveRoleAsync(string userObjectId, string assignmentId, CancellationToken cancellationToken = default);
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
