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
/// GET /api/dashboards/{clientSlug}/summary?from=&to= — client-level overview.
/// Policy-gated: caller must have access to the client.
/// </summary>
public class GetClientSummaryFunction
{
    private readonly ILogger<GetClientSummaryFunction> _logger;
    private readonly AppDbContext _context;
    private readonly IDashboardAccessResolver _accessResolver;
    private readonly IClientSummaryService _summaryService;

    public GetClientSummaryFunction(
        ILogger<GetClientSummaryFunction> logger,
        AppDbContext context,
        IDashboardAccessResolver accessResolver,
        IClientSummaryService summaryService)
    {
        _logger = logger;
        _context = context;
        _accessResolver = accessResolver;
        _summaryService = summaryService;
    }

    [Function("GetClientSummary")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboards/{clientSlug}/summary")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        try
        {
            var userId = req.GetUserId(context);
            var userType = req.GetUserType(context);
            if (userId is null || userType is null)
                return await req.CreateUnauthorizedResponseAsync();

            var ct = context.CancellationToken;

            var client = await _context.Clients.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
            if (client is null) return await NotFoundAsync(req);

            var canAccess = await _accessResolver.CanAccessClientAsync(
                userId.Value, userType.Value, req.GetRoles(context), client.Id, ct);
            if (!canAccess) return await NotFoundAsync(req); // 404 not 403 — don't leak existence

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var summary = await _summaryService.GetSummaryAsync(client.Id, query["from"], query["to"], ct);
            if (summary is null) return await NotFoundAsync(req);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(summary);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load client summary {ClientSlug}", clientSlug);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to load summary" });
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
