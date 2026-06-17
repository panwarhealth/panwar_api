using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Panwar.Api.Data;
using Panwar.Api.Models;

namespace Panwar.Api.Functions.Tracking;

public class RedirectFunction
{
    private static readonly string[] BotMarkers =
    {
        "bot", "crawl", "spider", "slurp", "preview", "scan", "monitor",
        "curl", "wget", "python-requests", "headless", "proofpoint",
        "mimecast", "barracuda", "googleimageproxy",
    };

    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public RedirectFunction(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [Function("InviteRedirect")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "r/{token}")] HttpRequestData req,
        FunctionContext context,
        string token)
    {
        var ct = context.CancellationToken;
        var portalUrl = (_configuration["CLIENT_PORTAL_URL"] ?? "http://localhost:5173").TrimEnd('/');

        var invite = await _context.ReportInvites
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Token == token, ct);

        if (invite is null) return Redirect(req, portalUrl);

        var ua = req.Headers.TryGetValues("User-Agent", out var uaVals) ? uaVals.FirstOrDefault() : null;
        var isBot = IsBot(ua);

        _context.InviteEvents.Add(new InviteEvent
        {
            Id = Guid.NewGuid(),
            InviteId = invite.Id,
            Type = "click",
            At = DateTime.UtcNow,
            Ip = GetClientIp(req),
            UserAgent = ua,
            IsBot = isBot,
        });

        if (!isBot)
        {
            invite.ClickCount += 1;
            invite.ClickedAt ??= DateTime.UtcNow;
            invite.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);

        return Redirect(req, BuildTarget(portalUrl, invite));
    }

    private static string BuildTarget(string portalUrl, ReportInvite invite)
    {
        var from = string.Format(CultureInfo.InvariantCulture, "{0:0000}-{1:00}", invite.Year, invite.StartMonth ?? 1);
        var to = string.Format(CultureInfo.InvariantCulture, "{0:0000}-{1:00}", invite.Year, invite.EndMonth ?? 12);
        return $"{portalUrl}/dashboard/{invite.Client.Slug}?from={from}&to={to}&e={invite.Token}";
    }

    private static HttpResponseData Redirect(HttpRequestData req, string location)
    {
        var resp = req.CreateResponse(HttpStatusCode.Redirect);
        resp.Headers.Add("Location", location);
        return resp;
    }

    private static bool IsBot(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua) || ua == "-") return true;
        var lower = ua.ToLowerInvariant();
        return BotMarkers.Any(lower.Contains);
    }

    private static string? GetClientIp(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("X-Forwarded-For", out var fwd))
        {
            var first = fwd.FirstOrDefault();
            if (!string.IsNullOrEmpty(first)) return first.Split(',')[0].Trim();
        }
        return null;
    }
}
