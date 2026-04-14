using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services.Authorization;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Dashboards;

/// <summary>
/// GET /api/my/clients — clients the authenticated user can view.
/// Employees with a dashboard role see every client; client users see their
/// memberships from user_client.
/// </summary>
public class GetMyClientsFunction
{
    private readonly ILogger<GetMyClientsFunction> _logger;
    private readonly AppDbContext _context;
    private readonly IDashboardAccessResolver _accessResolver;

    public GetMyClientsFunction(
        ILogger<GetMyClientsFunction> logger,
        AppDbContext context,
        IDashboardAccessResolver accessResolver)
    {
        _logger = logger;
        _context = context;
        _accessResolver = accessResolver;
    }

    [Function("GetMyClients")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "my/clients")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var userId = req.GetUserId(context);
            var userType = req.GetUserType(context);
            if (userId is null || userType is null)
                return await req.CreateUnauthorizedResponseAsync();

            var ct = context.CancellationToken;
            var clientIds = await _accessResolver.ListAccessibleClientsAsync(
                userId.Value, userType.Value, req.GetRoles(context), ct);

            var clients = await _context.Clients
                .AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .OrderBy(c => c.Name)
                .Select(c => new ClientSummaryDto(
                    c.Id, c.Name, c.Slug, c.LogoUrl, c.PrimaryColor, c.AccentColor))
                .ToListAsync(ct);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new ClientListResponse(clients));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list accessible clients");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to list clients" });
            return error;
        }
    }
}
