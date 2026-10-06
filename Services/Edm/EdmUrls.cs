using Microsoft.Extensions.Configuration;

namespace Panwar.Api.Services.Edm;

/// <summary>Public URLs baked into each email. They must be absolute, so they come from API_BASE_URL.</summary>
public static class EdmUrls
{
    public static string ApiBase(IConfiguration configuration) =>
        (configuration["API_BASE_URL"] is { Length: > 0 } b ? b : "https://api.panwarhealth.com.au").TrimEnd('/');

    // "preview" renders the page without acting on anything, for previews and test sends.
    public static string Unsubscribe(IConfiguration configuration, Guid? recipientId) =>
        $"{ApiBase(configuration)}/api/edm/u/{(recipientId is { } id ? id.ToString("N") : "preview")}";

    public static string Pixel(IConfiguration configuration, Guid recipientId) =>
        $"{ApiBase(configuration)}/api/edm/o/{recipientId:N}.gif";
}
