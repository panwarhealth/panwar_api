using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Shared.Middleware;

namespace Panwar.Api.Shared.Helpers;

/// <summary>
/// Sets/clears the panwar_session HttpOnly cookie. In production it's
/// scoped to .panwarhealth.com.au so portal., a1., and api. share it.
/// In local dev (localhost origins) we can't set Domain and need
/// SameSite=None to make cross-port cookies work.
/// </summary>
public static class CookieHelper
{
    public const string CookieName = AuthenticationMiddleware.CookieName;
    private const int OneYearSeconds = 365 * 24 * 60 * 60;

    public static void SetAuthCookie(HttpResponseData response, HttpRequestData request, string token)
    {
        var origin = GetOrigin(request);

        var parts = new List<string>
        {
            $"{CookieName}={token}",
            "HttpOnly",
            "Secure",
            "Path=/",
            $"Max-Age={OneYearSeconds}"
        };

        if (origin?.Contains("localhost") == true)
        {
            parts.Add("SameSite=None");
        }
        else
        {
            parts.Add("SameSite=Lax");
            parts.Add("Domain=.panwarhealth.com.au");
        }

        response.Headers.Add("Set-Cookie", string.Join("; ", parts));
    }

    public static void ClearAuthCookie(HttpResponseData response, HttpRequestData request)
    {
        var origin = GetOrigin(request);

        var parts = new List<string>
        {
            $"{CookieName}=",
            "HttpOnly",
            "Secure",
            "Path=/",
            "Max-Age=0"
        };

        if (origin?.Contains("localhost") == true)
        {
            parts.Add("SameSite=None");
        }
        else
        {
            parts.Add("SameSite=Lax");
            parts.Add("Domain=.panwarhealth.com.au");
        }

        response.Headers.Add("Set-Cookie", string.Join("; ", parts));
    }

    private static string? GetOrigin(HttpRequestData request)
    {
        return request.Headers.TryGetValues("Origin", out var origins)
            ? origins.FirstOrDefault()
            : null;
    }
}
