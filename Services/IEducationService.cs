using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

/// <summary>
/// Read models for the client portal's education pages. Returns null when the
/// page does not exist within the client.
/// </summary>
public interface IEducationService
{
    Task<EducationPagesResponse> GetPagesAsync(
        Guid clientId, string? from, string? to, CancellationToken cancellationToken);

    Task<EducationPageResponse?> GetPageAsync(
        Guid clientId, string pageSlug, string? from, string? to, CancellationToken cancellationToken);
}
