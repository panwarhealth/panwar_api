using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

/// <summary>
/// Reads the rolled-up dashboard view for a single brand × audience scoped to
/// a client. Returns null when the brand or audience does not exist within the
/// caller's client (so the function layer can map that to a 404).
/// </summary>
public interface IDashboardService
{
    Task<DashboardResponse?> GetDashboardAsync(
        Guid clientId,
        string brandSlug,
        string audienceSlug,
        CancellationToken cancellationToken);
}
