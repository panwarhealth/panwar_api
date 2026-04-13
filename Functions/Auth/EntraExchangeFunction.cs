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
/// POST /api/auth/entra/exchange  { "idToken": "..." }
/// Validates an Entra ID token from the employee dash frontend, upserts
/// the employee user, mints a panwar_session JWT, and sets the HttpOnly cookie.
/// Roles come from Entra App Roles (defined in the app registration and
/// assigned via Enterprise Applications → Users and groups).
/// </summary>
public class EntraExchangeFunction
{
    private readonly ILogger<EntraExchangeFunction> _logger;
    private readonly IEntraTokenValidator _entraValidator;
    private readonly IAuthService _authService;
    private readonly IJwtService _jwtService;

    public EntraExchangeFunction(
        ILogger<EntraExchangeFunction> logger,
        IEntraTokenValidator entraValidator,
        IAuthService authService,
        IJwtService jwtService)
    {
        _logger = logger;
        _entraValidator = entraValidator;
        _authService = authService;
        _jwtService = jwtService;
    }

    [Function("EntraExchange")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/entra/exchange")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<EntraExchangeRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data is null || string.IsNullOrWhiteSpace(data.IdToken))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "idToken is required" });
                return bad;
            }

            var entraResult = await _entraValidator.ValidateAsync(data.IdToken);
            if (entraResult is null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid Entra ID token" });
                return unauthorized;
            }

            var user = await _authService.GetOrCreateEmployeeUserAsync(
                entraResult.ObjectId, entraResult.Email, entraResult.Name);

            var token = _jwtService.GenerateToken(user.Id, user.Email, user.Type, user.ClientId, entraResult.Roles);

            var response = req.CreateResponse(HttpStatusCode.OK);
            CookieHelper.SetAuthCookie(response, req, token);
            await response.WriteAsJsonAsync(new
            {
                id = user.Id,
                email = user.Email,
                name = user.Name,
                type = user.Type.ToString().ToLowerInvariant(),
                clientId = user.ClientId,
                roles = entraResult.Roles
            });

            _logger.LogInformation("Entra exchange successful for {Email}, roles: [{Roles}]",
                entraResult.Email, string.Join(", ", entraResult.Roles));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Entra token exchange");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Token exchange failed" });
            return error;
        }
    }
}
