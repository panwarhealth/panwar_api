using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Services;
using Panwar.Api.Services.Authorization;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Dashboards;

/// <summary>
/// GET /api/dashboards/{clientSlug}/{brandSlug}/{audienceSlug}
///
/// Policy-gated: caller must have access to the client (via role or membership).
/// </summary>
public class GetDashboardFunction
{
    private readonly ILogger<GetDashboardFunction> _logger;
    private readonly AppDbContext _context;
    private readonly IDashboardAccessResolver _accessResolver;
    private readonly IDashboardService _dashboardService;

    public GetDashboardFunction(
        ILogger<GetDashboardFunction> logger,
        AppDbContext context,
        IDashboardAccessResolver accessResolver,
        IDashboardService dashboardService)
    {
        _logger = logger;
        _context = context;
        _accessResolver = accessResolver;
        _dashboardService = dashboardService;
    }

    [Function("GetDashboard")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboards/{clientSlug}/{brandSlug}/{audienceSlug}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string brandSlug,
        string audienceSlug)
    {
        try
        {
            var userId = req.GetUserId(context);
            var userType = req.GetUserType(context);
            if (userId is null || userType is null)
                return await req.CreateUnauthorizedResponseAsync();

            var ct = context.CancellationToken;

            var client = await _context.Clients
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
            if (client is null)
                return await NotFoundAsync(req);

            var canAccess = await _accessResolver.CanAccessClientAsync(
                userId.Value, userType.Value, req.GetRoles(context), client.Id, ct);
            if (!canAccess)
                return await NotFoundAsync(req); // 404 not 403 — don't leak existence

            var dashboard = await _dashboardService.GetDashboardAsync(
                client.Id, brandSlug, audienceSlug, ct);
            if (dashboard is null)
                return await NotFoundAsync(req);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(dashboard);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard {ClientSlug}/{BrandSlug}/{AudienceSlug}",
                clientSlug, brandSlug, audienceSlug);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to load dashboard" });
            return error;
        }
    }

    private static async Task<HttpResponseData> NotFoundAsync(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.NotFound);
        await resp.WriteAsJsonAsync(new { error = "Not found" });
        return resp;
    }
}
