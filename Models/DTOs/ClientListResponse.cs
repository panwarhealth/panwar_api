namespace Panwar.Api.Models.DTOs;

/// <summary>
/// Response for GET /api/my/clients — the clients the authenticated user can
/// view. For employees with a dashboard role this is every client; for client
/// users this is their user_client memberships.
/// </summary>
public sealed record ClientListResponse(IReadOnlyList<ClientSummaryDto> Clients);

public sealed record ClientSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string? LogoUrl,
    string? PrimaryColor,
    string? AccentColor);

/// <summary>
/// Response for GET /api/clients/{clientSlug}/brands — the brands under a
/// specific client, including available audiences for each brand.
/// </summary>
public sealed record ClientBrandsResponse(
    ClientSummaryDto Client,
    IReadOnlyList<BrandSummaryDto> Brands,
    IReadOnlyList<AudienceSummaryDto> Audiences);

public sealed record BrandSummaryDto(Guid Id, string Name, string Slug);

public sealed record AudienceSummaryDto(Guid Id, string Name, string Slug);
