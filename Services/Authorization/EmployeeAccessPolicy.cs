using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Authorization;

/// <summary>
/// Employees with a dashboard role can view every client. No membership rows
/// needed — access comes from the Entra App Role on their account.
/// </summary>
public class EmployeeAccessPolicy : IDashboardAccessPolicy
{
    private static readonly string[] DashboardRoles = ["panwar-admin", "dashboard-editor"];

    private readonly AppDbContext _context;

    public EmployeeAccessPolicy(AppDbContext context)
    {
        _context = context;
    }

    public bool AppliesTo(UserType userType, IEnumerable<string> roles)
        => userType == UserType.Employee
        && roles.Any(r => DashboardRoles.Contains(r, StringComparer.OrdinalIgnoreCase));

    public Task<bool> CanAccessAsync(Guid userId, Guid clientId, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public async Task<IReadOnlyList<Guid>> ListAccessibleClientsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Clients
            .AsNoTracking()
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }
}
