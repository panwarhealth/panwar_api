using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Shared.Extensions;

/// <summary>
/// Convenience accessors for the user context that AuthenticationMiddleware
/// stuffs into FunctionContext.Items. Functions call these to enforce
/// authn/authz without re-parsing the JWT.
/// </summary>
public static class HttpRequestDataExtensions
{
    public static Guid? GetUserId(this HttpRequestData _, FunctionContext context)
        => context.Items.TryGetValue("UserId", out var v) ? v as Guid? : null;

    public static UserType? GetUserType(this HttpRequestData _, FunctionContext context)
        => context.Items.TryGetValue("UserType", out var v) ? v as UserType? : null;

    public static string[] GetRoles(this HttpRequestData _, FunctionContext context)
        => context.Items.TryGetValue("Roles", out var v) && v is string[] roles ? roles : Array.Empty<string>();

    public static bool HasRole(this HttpRequestData req, FunctionContext context, string role)
        => req.GetRoles(context).Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    public static async Task<HttpResponseData> CreateUnauthorizedResponseAsync(this HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.Unauthorized);
        await response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return response;
    }

    public static async Task<HttpResponseData> CreateForbiddenResponseAsync(this HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.Forbidden);
        await response.WriteAsJsonAsync(new { error = "Forbidden" });
        return response;
    }
}
