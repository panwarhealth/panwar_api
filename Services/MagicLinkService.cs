using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;

namespace Panwar.Api.Services;

public class MagicLinkService : IMagicLinkService
{
    private readonly AppDbContext _context;
    private readonly ILogger<MagicLinkService> _logger;
    private readonly IEmailService _emailService;
    private readonly IAuthService _authService;

    public MagicLinkService(
        AppDbContext context,
        ILogger<MagicLinkService> logger,
        IEmailService emailService,
        IAuthService authService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public async Task GenerateMagicLinkAsync(string email, string portalUrl, string? ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating magic link for {Email}", email);

        var rawToken = GenerateRandomToken();
        var tokenHash = HashToken(rawToken);

        var record = new MagicLink
        {
            Id = Guid.NewGuid(),
            Email = email,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            IpAddress = ipAddress
        };

        _context.MagicLinks.Add(record);
        await _context.SaveChangesAsync(cancellationToken);

        var magicLink = $"{portalUrl.TrimEnd('/')}/auth/verify?token={rawToken}";
        await _emailService.SendMagicLinkEmailAsync(email, magicLink, cancellationToken);

        _logger.LogInformation("Magic link sent to {Email}", email);
    }

    public async Task<AppUser?> VerifyTokenAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(rawToken);

        var record = await _context.MagicLinks
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash
                                      && t.UsedAt == null
                                      && t.ExpiresAt > DateTime.UtcNow,
                                 cancellationToken);

        if (record is null)
        {
            _logger.LogWarning("Invalid or expired magic link token");
            return null;
        }

        record.UsedAt = DateTime.UtcNow;

        // Opportunistic cleanup of expired tokens for the same email
        var expired = await _context.MagicLinks
            .Where(t => t.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            _context.MagicLinks.RemoveRange(expired);
        }

        var user = await _authService.GetOrCreateClientUserAsync(record.Email, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Magic link verified for {Email}", record.Email);
        return user;
    }

    public async Task<bool> IsRateLimitedAsync(string email, CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow.AddSeconds(-30);
        return await _context.MagicLinks
            .AnyAsync(t => t.Email == email && t.CreatedAt > threshold, cancellationToken);
    }

    private static string GenerateRandomToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
