using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services;
using Panwar.Api.Shared.Helpers;

namespace Panwar.Api.Functions.Auth;

/// <summary>
/// POST /api/auth/magic-link/verify  { "token": "..." }
/// On success: sets the panwar_session HttpOnly cookie and returns the user.
/// On failure: 401.
/// </summary>
public class MagicLinkVerifyFunction
{
    private readonly ILogger<MagicLinkVerifyFunction> _logger;
    private readonly IMagicLinkService _magicLinkService;
    private readonly IJwtService _jwtService;

    public MagicLinkVerifyFunction(
        ILogger<MagicLinkVerifyFunction> logger,
        IMagicLinkService magicLinkService,
        IJwtService jwtService)
    {
        _logger = logger;
        _magicLinkService = magicLinkService;
        _jwtService = jwtService;
    }

    [Function("MagicLinkVerify")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/magic-link/verify")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<MagicLinkVerifyRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data is null || string.IsNullOrWhiteSpace(data.Token))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Token is required" });
                return bad;
            }

            var user = await _magicLinkService.VerifyTokenAsync(data.Token);
            if (user is null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var roles = user.Roles.Select(r => r.Role).ToArray();
            var token = _jwtService.GenerateToken(user.Id, user.Email, user.Type, roles);

            var response = req.CreateResponse(HttpStatusCode.OK);
            CookieHelper.SetAuthCookie(response, req, token);
            await response.WriteAsJsonAsync(new
            {
                id = user.Id,
                email = user.Email,
                name = user.Name,
                type = user.Type.ToString().ToLowerInvariant(),
                roles
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying magic link token");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Verification failed" });
            return error;
        }
    }
}
