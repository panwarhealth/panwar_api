namespace Panwar.Api.Models.DTOs;

public sealed record TrackedLinkDto(
    Guid Id,
    string DestinationUrl,
    string CampaignId,
    string? ClientName,
    string Source,
    string Medium,
    string? Content,
    string Url,
    string CreatedByName,
    DateTime CreatedAt);

public sealed record JobClientDto(string Prefix, string Name);

public sealed record QrCodeDto(
    Guid Id,
    string Url,
    int Size,
    string Foreground,
    string Background,
    bool HasLogo,
    string CreatedByName,
    DateTime CreatedAt);

public class TrackedLinkWriteRequest
{
    public string DestinationUrl { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public string Source { get; set; } = "";
    public string Medium { get; set; } = "";
}

public class UrlCheckRequest
{
    public string Url { get; set; } = "";
}

public class TrackedLinkContentRequest
{
    public string? Content { get; set; }
}

public class QrCodeWriteRequest
{
    public string Url { get; set; } = "";
    public int Size { get; set; }
    public string Foreground { get; set; } = "";
    public string Background { get; set; } = "";
    public bool HasLogo { get; set; }
}
