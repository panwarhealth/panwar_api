using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Authorization;

/// <summary>
/// Composes registered <see cref="IDashboardAccessPolicy"/> instances to answer
/// access questions. A user's access is the union across every policy that
/// <c>AppliesTo</c> them.
/// </summary>
public interface IDashboardAccessResolver
{
    Task<bool> CanAccessClientAsync(
        Guid userId, UserType userType, IEnumerable<string> roles, Guid clientId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListAccessibleClientsAsync(
        Guid userId, UserType userType, IEnumerable<string> roles,
        CancellationToken cancellationToken);
}
