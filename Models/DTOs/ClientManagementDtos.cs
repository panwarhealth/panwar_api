namespace Panwar.Api.Models.DTOs;

public sealed record ClientListItemDto(
    Guid Id,
    string Name,
    string Slug,
    string? LogoUrl,
    string? PrimaryColor,
    string? AccentColor,
    int UserCount);

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
