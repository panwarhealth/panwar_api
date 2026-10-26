using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services;

namespace Panwar.Api.Shared.Middleware;

// Reads the panwar_session cookie (prod) or Bearer header (local dev), validates the JWT,
// and injects user context into FunctionContext.Items for use via HttpRequestDataExtensions.
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

        var authCookie = requestData.Cookies.FirstOrDefault(c => c.Name == CookieName);
        if (authCookie is not null)
        {
            token = authCookie.Value;
        }

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

        if (principal is null
            || !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            || !Enum.TryParse<UserType>(principal.FindFirst(JwtService.ClaimUserType)?.Value, ignoreCase: true, out var userType))
        {
            await next(context);
            return;
        }

        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

        if (userType == UserType.Employee)
        {
            var entraObjectId = principal.FindFirst(JwtService.ClaimEntraObjectId)?.Value;
            roles = entraObjectId is null
                ? []
                : await context.InstanceServices.GetRequiredService<IEmployeeRoleService>()
                    .GetRolesAsync(entraObjectId, context.CancellationToken);

            if (roles.Length == 0)
            {
                await next(context);
                return;
            }
        }

        context.Items["UserId"] = userId;
        context.Items["Roles"] = roles;
        context.Items["UserType"] = userType;

        await next(context);
    }
}
