using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Functions.Auth;

/// <summary>
/// POST /api/auth/method  { "email": "..." } → { "method": "magic-link" | "entra" | "denied" }
///
/// The client portal calls this once the user enters their email. It decides
/// which auth flow to use without leaking account existence (all valid inputs
/// get a meaningful method, invalid ones get "denied" — same response time).
///
/// Rules (order matters):
///   1. If a Client user exists with this email → magic-link
///      (keeps the rob+client@panwarhealth.com.au plus-addressing hack working)
///   2. Else if email ends in @panwarhealth.com.au → entra
///   3. Else → denied
/// </summary>
public class LoginMethodFunction
{
    private const string StaffDomainSuffix = "@panwarhealth.com.au";

    private readonly ILogger<LoginMethodFunction> _logger;
    private readonly AppDbContext _context;

    public LoginMethodFunction(ILogger<LoginMethodFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Function("LoginMethod")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/method")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<LoginMethodRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var email = data?.Email?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(email))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Email is required" });
                return bad;
            }

            var method = await ResolveMethodAsync(email, context.CancellationToken);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new LoginMethodResponse { Method = method });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving login method");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to resolve login method" });
            return error;
        }
    }

    private async Task<string> ResolveMethodAsync(string email, CancellationToken ct)
    {
        var isClient = await _context.Users
            .AnyAsync(u => u.Email == email && u.Type == UserType.Client, ct);
        if (isClient) return "magic-link";

        if (email.EndsWith(StaffDomainSuffix, StringComparison.Ordinal)) return "entra";

        return "denied";
    }
}
