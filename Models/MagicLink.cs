namespace Panwar.Api.Models;

/// <summary>
/// One-shot magic-link tokens issued to client users for passwordless sign-in.
/// Token is hashed before storage; the raw token only ever exists in the
/// outbound email and the user's URL.
/// </summary>
public class MagicLink
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
