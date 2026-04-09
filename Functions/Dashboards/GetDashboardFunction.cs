using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Dashboards;

/// <summary>
/// GET /api/dashboards/{brandSlug}/{audienceSlug}
///
/// Returns the rolled-up brand × audience dashboard for the caller's client.
/// Auth is required (cookie or bearer); the brand/audience must belong to the
/// caller's client or the response is 404 (we deliberately don't leak whether
/// the slugs exist for a different tenant).
/// </summary>
public class GetDashboardFunction
{
    private readonly ILogger<GetDashboardFunction> _logger;
    private readonly IDashboardService _dashboardService;

    public GetDashboardFunction(
        ILogger<GetDashboardFunction> logger,
        IDashboardService dashboardService)
    {
        _logger = logger;
        _dashboardService = dashboardService;
    }

    [Function("GetDashboard")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboards/{brandSlug}/{audienceSlug}")] HttpRequestData req,
        FunctionContext context,
        string brandSlug,
        string audienceSlug)
    {
        try
        {
            var userId = req.GetUserId(context);
            if (userId is null)
                return await req.CreateUnauthorizedResponseAsync();

            // Client portal endpoint: must be a Client user with a clientId.
            // Employee/admin tooling will eventually have its own endpoint that
            // takes the clientId as a parameter.
            var userType = req.GetUserType(context);
            var clientId = req.GetClientId(context);
            if (userType != UserType.Client || clientId is null)
                return await req.CreateForbiddenResponseAsync();

            var cancellationToken = context.CancellationToken;
            var dashboard = await _dashboardService.GetDashboardAsync(
                clientId.Value,
                brandSlug,
                audienceSlug,
                cancellationToken);

            if (dashboard is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dashboard not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(dashboard);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard {BrandSlug}/{AudienceSlug}", brandSlug, audienceSlug);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to load dashboard" });
            return error;
        }
    }
}
