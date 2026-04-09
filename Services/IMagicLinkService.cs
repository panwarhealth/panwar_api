using Panwar.Api.Models;

namespace Panwar.Api.Services;

public interface IMagicLinkService
{
    /// <summary>
    /// Generate a single-use magic link token, store the hash, send the email.
    /// Throws if rate-limited (use IsRateLimitedAsync first to check).
    /// </summary>
    Task GenerateMagicLinkAsync(string email, string portalUrl, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up a magic link by raw token, validate (not used, not expired),
    /// mark used, return the corresponding user. Returns null on any failure.
    /// </summary>
    Task<AppUser?> VerifyTokenAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if a magic link was issued for this email within the last 30 seconds.
    /// </summary>
    Task<bool> IsRateLimitedAsync(string email, CancellationToken cancellationToken = default);
}
