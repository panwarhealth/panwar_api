using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

/// <summary>
/// Both client users (magic-link auth, scoped to a single ClientId) and
/// employees (Entra ID SSO, no ClientId) live in the same table. Type
/// discriminates the two; ClientId is required for clients and null for
/// employees, EntraId vice-versa.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; }
    public UserType Type { get; set; }
    public required string Email { get; set; }
    public string? Name { get; set; }
    public Guid? ClientId { get; set; }       // clients only
    public string? EntraId { get; set; }      // employees only
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Client? Client { get; set; }
    public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();
}
