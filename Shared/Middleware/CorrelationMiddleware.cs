using System.Diagnostics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Panwar.Api.Shared.Middleware;

/// <summary>
/// Tags every request with a correlation ID and logs request/response timing.
/// Pulled from the inbound X-Correlation-ID header if present, otherwise generated.
/// </summary>
public class CorrelationMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<CorrelationMiddleware> _logger;

    public CorrelationMiddleware(ILogger<CorrelationMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData is null)
        {
            await next(context);
            return;
        }

        string correlationId;
        if (requestData.Headers.TryGetValues("X-Correlation-ID", out var headers))
        {
            correlationId = headers.FirstOrDefault() ?? Guid.NewGuid().ToString();
        }
        else
        {
            correlationId = Guid.NewGuid().ToString();
        }

        context.Items["CorrelationId"] = correlationId;
        Activity.Current?.AddBaggage("CorrelationId", correlationId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Request started: {Method} {Path} | CorrelationId: {CorrelationId}",
                requestData.Method,
                requestData.Url.PathAndQuery,
                correlationId);

            await next(context);

            stopwatch.Stop();

            _logger.LogInformation(
                "Request completed: {Method} {Path} | Duration: {Duration}ms | CorrelationId: {CorrelationId}",
                requestData.Method,
                requestData.Url.PathAndQuery,
                stopwatch.ElapsedMilliseconds,
                correlationId);

            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning(
                    "Slow request detected: {Method} {Path} | Duration: {Duration}ms | CorrelationId: {CorrelationId}",
                    requestData.Method,
                    requestData.Url.PathAndQuery,
                    stopwatch.ElapsedMilliseconds,
                    correlationId);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Request failed: {Method} {Path} | Duration: {Duration}ms | CorrelationId: {CorrelationId}",
                requestData.Method,
                requestData.Url.PathAndQuery,
                stopwatch.ElapsedMilliseconds,
                correlationId);
            throw;
        }
    }
}
