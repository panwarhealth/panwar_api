using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext context, ILogger<AuthService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AppUser?> GetOrCreateClientUserAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.Type == UserType.Client, cancellationToken);

        if (user is null)
        {
            // Clients must be pre-provisioned in the employee portal — magic-link is not a sign-up flow.
            _logger.LogWarning("Magic link verified for unknown client email {Email}", normalizedEmail);
            return null;
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return user;
    }

    public async Task<AppUser> GetOrCreateEmployeeUserAsync(string entraId, string email, string? name, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.EntraId == entraId, cancellationToken);

        if (user is not null)
        {
            user.Email = normalizedEmail;
            if (!string.IsNullOrWhiteSpace(name))
            {
                user.Name = name;
            }
            user.LastLoginAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return user;
        }

        user = new AppUser
        {
            Id = Guid.NewGuid(),
            Type = UserType.Employee,
            Email = normalizedEmail,
            Name = name,
            EntraId = entraId,
            LastLoginAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("New employee user created: {Email} (entra={EntraId})", normalizedEmail, entraId);
        return user;
    }
}
