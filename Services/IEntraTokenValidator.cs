namespace Panwar.Api.Services;

/// <summary>
/// Validates Entra ID tokens issued by Microsoft for the Employee SSO app
/// registration. Returns the extracted claims (oid, email, name) on success.
/// </summary>
public interface IEntraTokenValidator
{
    Task<EntraTokenResult?> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Claims extracted from a validated Entra ID token.
/// </summary>
public record EntraTokenResult(string ObjectId, string Email, string? Name, string[] Roles);
