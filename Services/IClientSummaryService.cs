using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

/// <summary>
/// Builds the client-level overview (rollup across every brand × audience) scoped
/// to a month window. Returns null when the client does not exist.
/// </summary>
public interface IClientSummaryService
{
    Task<ClientSummaryResponse?> GetSummaryAsync(
        Guid clientId,
        string? from,
        string? to,
        CancellationToken cancellationToken);
}
