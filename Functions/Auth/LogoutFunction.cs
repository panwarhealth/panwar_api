using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Panwar.Api.Shared.Helpers;

namespace Panwar.Api.Functions.Auth;

/// <summary>
/// POST /api/auth/logout — clears the panwar_session cookie.
/// </summary>
public class LogoutFunction
{
    private readonly ILogger<LogoutFunction> _logger;

    public LogoutFunction(ILogger<LogoutFunction> logger)
    {
        _logger = logger;
    }

    [Function("Logout")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/logout")] HttpRequestData req)
    {
        try
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            CookieHelper.ClearAuthCookie(response, req);
            await response.WriteAsJsonAsync(new { success = true });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Logout failed" });
            return error;
        }
    }
}
