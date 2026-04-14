using System.Security.Claims;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services;

public interface IJwtService
{
    /// <summary>
    /// Mint a session JWT for an authenticated user. Client users are no longer
    /// scoped to a single client via the JWT — memberships are looked up per
    /// request from the user_client junction table.
    /// </summary>
    string GenerateToken(Guid userId, string email, UserType userType, IEnumerable<string> roles);

    /// <summary>
    /// Validate a JWT and return the principal, or null if validation failed
    /// (signature, expiry, issuer, audience).
    /// </summary>
    ClaimsPrincipal? ValidateToken(string token);
}
