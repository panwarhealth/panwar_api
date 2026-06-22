namespace Panwar.Api.Models.DTOs;

public sealed record ClientListItemDto(
    Guid Id,
    string Name,
    string Slug,
    string? LogoUrl,
    string? PrimaryColor,
    string? AccentColor,
    bool ShowBrandMonthlyChart,
    bool ShowPublisherChart,
    int UserCount);

/// <summary>PATCH manage/clients/{slug}/overview-charts body.</summary>
public class OverviewChartsRequest
{
    public bool ShowBrandMonthlyChart { get; set; } = true;
    public bool ShowPublisherChart { get; set; } = true;
}

public class CreateClientRequest
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? AccentColor { get; set; }
}

public sealed record ClientUserDto(
    Guid Id,
    string Email,
    string? Name,
    DateTime? LastLoginAt,
    DateTime CreatedAt);

public class AddClientUserRequest
{
    public string Email { get; set; } = "";
    public string? Name { get; set; }
}

/// <summary>DELETE manage/clients/{slug} body - the exact client name, typed to confirm.</summary>
public class DeleteClientRequest
{
    public string ConfirmName { get; set; } = "";
}
