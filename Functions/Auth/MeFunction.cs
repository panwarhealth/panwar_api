using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Auth;

/// <summary>
/// GET /api/auth/me — returns the current user from the session cookie.
/// Client-specific fields (brands, branding, etc.) live on separate endpoints
/// because users can now belong to multiple clients.
/// </summary>
public class MeFunction
{
    private readonly ILogger<MeFunction> _logger;
    private readonly AppDbContext _context;

    public MeFunction(ILogger<MeFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Function("Me")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/me")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var userId = req.GetUserId(context);
            if (userId is null)
                return await req.CreateUnauthorizedResponseAsync();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId.Value);

            if (user is null)
                return await req.CreateUnauthorizedResponseAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new MeResponse
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.Name,
                Type = user.Type.ToString().ToLowerInvariant(),
                Roles = req.GetRoles(context)
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching current user");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to fetch user" });
            return error;
        }
    }
}
