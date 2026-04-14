using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Authorization;

/// <summary>
/// Decides whether a user can view a given client's dashboards. Policies are
/// independent and composed by <see cref="IDashboardAccessResolver"/>. Add a
/// new policy class to extend access rules without touching existing ones.
/// </summary>
public interface IDashboardAccessPolicy
{
    /// <summary>
    /// True if this policy applies to the user type + role context. Lets the
    /// resolver skip irrelevant policies instead of every policy inspecting
    /// the user shape itself.
    /// </summary>
    bool AppliesTo(UserType userType, IEnumerable<string> roles);

    /// <summary>Can the user access the given client's dashboards?</summary>
    Task<bool> CanAccessAsync(Guid userId, Guid clientId, CancellationToken cancellationToken);

    /// <summary>Which client IDs can the user access? (For listing.)</summary>
    Task<IReadOnlyList<Guid>> ListAccessibleClientsAsync(Guid userId, CancellationToken cancellationToken);
}
