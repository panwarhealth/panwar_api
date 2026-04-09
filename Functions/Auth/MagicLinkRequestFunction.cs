using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services;

namespace Panwar.Api.Functions.Auth;

/// <summary>
/// POST /api/auth/magic-link  { "email": "..." }
/// Always returns 200 even for unknown emails (don't leak which addresses
/// are valid). Rate-limited at 5 requests/minute per IP via the middleware,
/// plus a 30-second per-email throttle in MagicLinkService.
/// </summary>
public class MagicLinkRequestFunction
{
    private readonly ILogger<MagicLinkRequestFunction> _logger;
    private readonly IMagicLinkService _magicLinkService;
    private readonly IConfiguration _configuration;

    public MagicLinkRequestFunction(
        ILogger<MagicLinkRequestFunction> logger,
        IMagicLinkService magicLinkService,
        IConfiguration configuration)
    {
        _logger = logger;
        _magicLinkService = magicLinkService;
        _configuration = configuration;
    }

    [Function("MagicLinkRequest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/magic-link")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<MagicLinkRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data is null || string.IsNullOrWhiteSpace(data.Email))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Email is required" });
                return bad;
            }

            var email = data.Email.Trim().ToLowerInvariant();

            if (!IsValidEmail(email))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Invalid email format" });
                return bad;
            }

            if (await _magicLinkService.IsRateLimitedAsync(email))
            {
                var tooMany = req.CreateResponse(HttpStatusCode.TooManyRequests);
                await tooMany.WriteAsJsonAsync(new
                {
                    error = "Too many requests. Please wait 30 seconds before requesting another magic link."
                });
                return tooMany;
            }

            var portalUrl = _configuration["CLIENT_PORTAL_URL"] ?? "http://localhost:5173";
            var ip = GetClientIp(req);

            await _magicLinkService.GenerateMagicLinkAsync(email, portalUrl, ip);

            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(new { message = "If an account exists for that email, a sign-in link has been sent." });
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing magic link request");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to send magic link" });
            return error;
        }
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetClientIp(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("X-Forwarded-For", out var fwd))
        {
            var first = fwd.FirstOrDefault();
            if (!string.IsNullOrEmpty(first))
                return first.Split(',')[0].Trim();
        }
        return null;
    }
}
