using Panwar.Api.Models;

namespace Panwar.Api.Services;

public interface IAuthService
{
    /// <summary>
    /// Find an existing client user by email, or refuse to create one.
    /// Client users are NOT auto-provisioned by magic-link sign-in — they must
    /// be created in the employee portal first (so the editor explicitly
    /// chooses which client they belong to). Returns null if no user exists.
    /// </summary>
    Task<AppUser?> GetOrCreateClientUserAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find an existing employee user by Entra ID, or create one on first sign-in.
    /// Employees are trusted from the Entra tenant — they get auto-provisioned
    /// the first time they hit the API.
    /// </summary>
    Task<AppUser> GetOrCreateEmployeeUserAsync(string entraId, string email, string? name, CancellationToken cancellationToken = default);
}
