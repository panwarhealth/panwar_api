using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Authorization;

/// <summary>
/// Client users can see the clients they have rows for in the user_client
/// junction table.
/// </summary>
public class ClientMembershipAccessPolicy : IDashboardAccessPolicy
{
    private readonly AppDbContext _context;

    public ClientMembershipAccessPolicy(AppDbContext context)
    {
        _context = context;
    }

    public bool AppliesTo(UserType userType, IEnumerable<string> roles)
        => userType == UserType.Client;

    public async Task<bool> CanAccessAsync(Guid userId, Guid clientId, CancellationToken cancellationToken)
    {
        return await _context.UserClients
            .AnyAsync(uc => uc.UserId == userId && uc.ClientId == clientId, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListAccessibleClientsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.UserClients
            .AsNoTracking()
            .Where(uc => uc.UserId == userId)
            .Select(uc => uc.ClientId)
            .ToListAsync(cancellationToken);
    }
}
