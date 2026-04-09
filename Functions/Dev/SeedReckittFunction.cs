using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panwar.Api.Services.Seed;

namespace Panwar.Api.Functions.Dev;

/// <summary>
/// POST /api/dev/seed-reckitt
/// One-shot endpoint to populate the DB from the Reckitt workbook. Gated to
/// the Development environment so it can never run in production. Path to the
/// workbook comes from RECKITT_WORKBOOK_PATH config (or query string override
/// for ad-hoc testing).
///
/// Lives under /dev/ instead of /admin/ because Azure Functions reserves /admin/
/// for its host management API. This is a temporary dev endpoint and will be
/// removed once we have a proper import UI in the employee portal.
/// </summary>
public class SeedReckittFunction
{
    private readonly ILogger<SeedReckittFunction> _logger;
    private readonly IReckittSeedService _seedService;
    private readonly IConfiguration _configuration;

    public SeedReckittFunction(
        ILogger<SeedReckittFunction> logger,
        IReckittSeedService seedService,
        IConfiguration configuration)
    {
        _logger = logger;
        _seedService = seedService;
        _configuration = configuration;
    }

    [Function("SeedReckitt")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dev/seed-reckitt")] HttpRequestData req)
    {
        var environment = _configuration["AZURE_FUNCTIONS_ENVIRONMENT"] ?? "Production";
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "This endpoint only runs in Development." });
            return forbidden;
        }

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var path = query["path"] ?? _configuration["RECKITT_WORKBOOK_PATH"];

        if (string.IsNullOrWhiteSpace(path))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "RECKITT_WORKBOOK_PATH not configured and no ?path= query string provided." });
            return bad;
        }

        try
        {
            var summary = await _seedService.SeedAsync(path);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(summary);
            return response;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Reckitt workbook not found");
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = ex.Message });
            return notFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reckitt seed failed");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = ex.Message, type = ex.GetType().Name });
            return error;
        }
    }
}
