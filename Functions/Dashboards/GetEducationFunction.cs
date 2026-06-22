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
/// Client-facing education reads:
///   GET /api/dashboards/{clientSlug}/education            — list pages
///   GET /api/dashboards/{clientSlug}/education/{pageSlug}  — one page (windowed)
/// Both are policy-gated: the caller must have access to the client.
/// </summary>
public class GetEducationFunction
{
    private readonly ILogger<GetEducationFunction> _logger;
    private readonly AppDbContext _context;
    private readonly IDashboardAccessResolver _accessResolver;
    private readonly IEducationService _education;

    public GetEducationFunction(
        ILogger<GetEducationFunction> logger,
        AppDbContext context,
        IDashboardAccessResolver accessResolver,
        IEducationService education)
    {
        _logger = logger;
        _context = context;
        _accessResolver = accessResolver;
        _education = education;
    }

    [Function("GetEducationPages")]
    public async Task<HttpResponseData> ListPages(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboards/{clientSlug}/education")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        try
        {
            var (client, denied) = await ResolveAsync(req, context, clientSlug);
            if (denied is not null) return denied;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var result = await _education.GetPagesAsync(
                client!.Id, query["from"], query["to"], context.CancellationToken);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(result);
            return resp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list education pages for {ClientSlug}", clientSlug);
            return await ErrorAsync(req);
        }
    }

    [Function("GetEducationPage")]
    public async Task<HttpResponseData> GetPage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboards/{clientSlug}/education/{pageSlug}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string pageSlug)
    {
        try
        {
            var (client, denied) = await ResolveAsync(req, context, clientSlug);
            if (denied is not null) return denied;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var page = await _education.GetPageAsync(
                client!.Id, pageSlug, query["from"], query["to"], context.CancellationToken);
            if (page is null) return await NotFoundAsync(req);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(page);
            return resp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load education page {ClientSlug}/{PageSlug}", clientSlug, pageSlug);
            return await ErrorAsync(req);
        }
    }

    /// <summary>Resolves the client + access. Returns (client, null) on success or (null, response) to short-circuit.</summary>
    private async Task<(Models.Client? client, HttpResponseData? denied)> ResolveAsync(
        HttpRequestData req, FunctionContext context, string clientSlug)
    {
        var userId = req.GetUserId(context);
        var userType = req.GetUserType(context);
        if (userId is null || userType is null)
            return (null, await req.CreateUnauthorizedResponseAsync());

        var ct = context.CancellationToken;
        var client = await _context.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return (null, await NotFoundAsync(req));

        var canAccess = await _accessResolver.CanAccessClientAsync(
            userId.Value, userType.Value, req.GetRoles(context), client.Id, ct);
        if (!canAccess) return (null, await NotFoundAsync(req)); // 404 not 403 — don't leak existence

        return (client, null);
    }

    private static async Task<HttpResponseData> NotFoundAsync(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.NotFound);
        await resp.WriteAsJsonAsync(new { error = "Not found" });
        return resp;
    }

    private static async Task<HttpResponseData> ErrorAsync(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.InternalServerError);
        await resp.WriteAsJsonAsync(new { error = "Failed to load education" });
        return resp;
    }
}
