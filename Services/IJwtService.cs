using System.Security.Claims;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services;

public interface IJwtService
{
    /// <summary>
    /// Mint a session JWT for an authenticated user. ClientId is included as
    /// a claim for client users so the API can scope every query by tenant.
    /// </summary>
    string GenerateToken(Guid userId, string email, UserType userType, Guid? clientId, IEnumerable<string> roles);

    /// <summary>
    /// Validate a JWT and return the principal, or null if validation failed
    /// (signature, expiry, issuer, audience).
    /// </summary>
    ClaimsPrincipal? ValidateToken(string token);
}
