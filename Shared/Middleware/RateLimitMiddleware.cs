using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Panwar.Api.Shared.Middleware;

/// <summary>
/// In-memory sliding-window rate limiter. Three buckets:
///  - auth endpoints (magic link request): 5 / 60s per IP
///  - authenticated users: 120 / 60s per UserId
///  - unauthenticated: 30 / 60s per IP
/// Resets on Function App cold-start, which is fine for our threat model.
/// </summary>
public class RateLimitMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly ConcurrentDictionary<string, SlidingWindowCounter> _counters = new();
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private static DateTime _lastCleanup = DateTime.UtcNow;

    private const int AuthWindowSeconds = 60;
    private const int AuthMaxRequests = 5;

    private const int AuthenticatedWindowSeconds = 60;
    private const int AuthenticatedMaxRequests = 120;

    private const int UnauthenticatedWindowSeconds = 60;
    private const int UnauthenticatedMaxRequests = 30;

    private static readonly string[] AuthRoutes = { "/api/auth/magic-link" };

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData is null)
        {
            await next(context);
            return;
        }

        CleanupStaleEntries();

        var path = requestData.Url.AbsolutePath.ToLowerInvariant();
        var ip = GetClientIp(requestData);

        string bucketKey;
        int maxRequests;
        int windowSeconds;

        var isAuthRoute = AuthRoutes.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
        var isAuthenticated = context.Items.ContainsKey("UserId");

        if (isAuthRoute)
        {
            bucketKey = $"auth:{ip}";
            maxRequests = AuthMaxRequests;
            windowSeconds = AuthWindowSeconds;
        }
        else if (isAuthenticated)
        {
            var userId = context.Items["UserId"];
            bucketKey = $"user:{userId}";
            maxRequests = AuthenticatedMaxRequests;
            windowSeconds = AuthenticatedWindowSeconds;
        }
        else
        {
            bucketKey = $"ip:{ip}";
            maxRequests = UnauthenticatedMaxRequests;
            windowSeconds = UnauthenticatedWindowSeconds;
        }

        var counter = _counters.GetOrAdd(bucketKey, _ => new SlidingWindowCounter(windowSeconds));

        if (!counter.TryConsume(maxRequests))
        {
            var logger = context.InstanceServices.GetService(typeof(ILogger<RateLimitMiddleware>)) as ILogger;
            logger?.LogWarning("Rate limit exceeded for {BucketKey} on {Method} {Path}", bucketKey, requestData.Method, path);

            var response = requestData.CreateResponse(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Retry-After", windowSeconds.ToString());
            await response.WriteAsJsonAsync(new { error = "Too many requests. Please try again later." });

            context.GetInvocationResult().Value = response;
            return;
        }

        await next(context);
    }

    private static string GetClientIp(HttpRequestData request)
    {
        if (request.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
        {
            var first = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(first))
            {
                var ip = first.Split(',')[0].Trim();
                var colonIndex = ip.LastIndexOf(':');
                if (colonIndex > 0 && !ip.Contains('['))
                    ip = ip[..colonIndex];
                return ip;
            }
        }

        if (request.Headers.TryGetValues("X-Real-IP", out var realIp))
        {
            var ip = realIp.FirstOrDefault();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }

        return "unknown";
    }

    private static void CleanupStaleEntries()
    {
        if (DateTime.UtcNow - _lastCleanup < CleanupInterval)
            return;

        _lastCleanup = DateTime.UtcNow;

        var staleKeys = _counters
            .Where(kvp => kvp.Value.IsStale())
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in staleKeys)
            _counters.TryRemove(key, out _);
    }

    private sealed class SlidingWindowCounter
    {
        private readonly int _windowSeconds;
        private readonly Queue<DateTime> _timestamps = new();
        private readonly Lock _lock = new();

        public SlidingWindowCounter(int windowSeconds)
        {
            _windowSeconds = windowSeconds;
        }

        public bool TryConsume(int maxRequests)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var windowStart = now.AddSeconds(-_windowSeconds);

                while (_timestamps.Count > 0 && _timestamps.Peek() < windowStart)
                    _timestamps.Dequeue();

                if (_timestamps.Count >= maxRequests)
                    return false;

                _timestamps.Enqueue(now);
                return true;
            }
        }

        public bool IsStale()
        {
            lock (_lock)
            {
                if (_timestamps.Count == 0)
                    return true;
                var last = _timestamps.Last();
                return DateTime.UtcNow - last > TimeSpan.FromSeconds(_windowSeconds * 2);
            }
        }
    }
}
