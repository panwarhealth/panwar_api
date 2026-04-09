namespace Panwar.Api.Models.DTOs;

public class MagicLinkRequest
{
    public string Email { get; set; } = "";
}

public class MagicLinkVerifyRequest
{
    public string Token { get; set; } = "";
}

public class MeResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? Name { get; set; }
    public string Type { get; set; } = "";
    public Guid? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? ClientLogoUrl { get; set; }
    public string? ClientPrimaryColor { get; set; }
    public string? ClientAccentColor { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
}
