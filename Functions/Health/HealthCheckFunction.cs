using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;

namespace Panwar.Api.Functions.Health;

/// <summary>
/// GET /api/health — returns 200 if the API is up and the DB is reachable.
/// Used by Azure App Insights availability tests + the deploy pipeline smoke check.
/// </summary>
public class HealthCheckFunction
{
    private readonly ILogger<HealthCheckFunction> _logger;
    private readonly AppDbContext _context;

    public HealthCheckFunction(ILogger<HealthCheckFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Function("HealthCheck")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var dbOk = false;
        try
        {
            dbOk = await _context.Database.CanConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check DB ping failed");
        }

        var status = dbOk ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new
        {
            status = dbOk ? "healthy" : "degraded",
            database = dbOk ? "ok" : "unreachable",
            timestamp = DateTime.UtcNow
        });
        return response;
    }
}
