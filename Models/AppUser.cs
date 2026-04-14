using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

/// <summary>
/// Both client users (magic-link auth) and employees (Entra SSO) live in this
/// table. Type discriminates the two. Employees access all clients via their
/// portal roles; clients have rows in user_client mapping them to one or more
/// clients they can view.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; }
    public UserType Type { get; set; }
    public required string Email { get; set; }
    public string? Name { get; set; }
    public string? EntraId { get; set; }      // employees only
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<UserClient> Clients { get; set; } = new List<UserClient>();
    public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();
}
