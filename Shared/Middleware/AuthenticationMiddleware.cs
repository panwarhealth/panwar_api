using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services;

namespace Panwar.Api.Shared.Middleware;

/// <summary>
/// Reads the auth cookie (production) or Authorization Bearer header (local dev),
/// validates the JWT, and injects user context into FunctionContext.Items so
/// downstream functions can grab it via HttpRequestDataExtensions.
/// </summary>
public class AuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    public const string CookieName = "panwar_session";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData is null)
        {
            await next(context);
            return;
        }

        string? token = null;

        // Cookie path (production / SPAs)
        var authCookie = requestData.Cookies.FirstOrDefault(c => c.Name == CookieName);
        if (authCookie is not null)
        {
            token = authCookie.Value;
        }

        // Bearer header fallback (local dev / API clients)
        if (string.IsNullOrEmpty(token)
            && requestData.Headers.TryGetValues("Authorization", out var authHeaders))
        {
            var authHeader = authHeaders.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                token = authHeader["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            await next(context);
            return;
        }

        var jwtService = context.InstanceServices.GetRequiredService<IJwtService>();
        var principal = jwtService.ValidateToken(token);

        if (principal is not null)
        {
            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userTypeClaim = principal.FindFirst(JwtService.ClaimUserType)?.Value;
            // JwtSecurityTokenHandler maps "role" → ClaimTypes.Role on read
            var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

            if (Guid.TryParse(userIdClaim, out var userId))
            {
                context.Items["UserId"] = userId;
                context.Items["Roles"] = roles;

                if (Enum.TryParse<UserType>(userTypeClaim, ignoreCase: true, out var userType))
                {
                    context.Items["UserType"] = userType;
                }
            }
        }

        await next(context);
    }
}
