using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Authorization;

public class DashboardAccessResolver : IDashboardAccessResolver
{
    private readonly IEnumerable<IDashboardAccessPolicy> _policies;

    public DashboardAccessResolver(IEnumerable<IDashboardAccessPolicy> policies)
    {
        _policies = policies;
    }

    public async Task<bool> CanAccessClientAsync(
        Guid userId, UserType userType, IEnumerable<string> roles, Guid clientId,
        CancellationToken cancellationToken)
    {
        var rolesList = roles as IList<string> ?? roles.ToList();

        foreach (var policy in _policies.Where(p => p.AppliesTo(userType, rolesList)))
        {
            if (await policy.CanAccessAsync(userId, clientId, cancellationToken))
                return true;
        }
        return false;
    }

    public async Task<IReadOnlyList<Guid>> ListAccessibleClientsAsync(
        Guid userId, UserType userType, IEnumerable<string> roles,
        CancellationToken cancellationToken)
    {
        var rolesList = roles as IList<string> ?? roles.ToList();
        var accessible = new HashSet<Guid>();

        foreach (var policy in _policies.Where(p => p.AppliesTo(userType, rolesList)))
        {
            var ids = await policy.ListAccessibleClientsAsync(userId, cancellationToken);
            foreach (var id in ids) accessible.Add(id);
        }
        return accessible.ToList();
    }
}
