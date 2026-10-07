using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panwar.Api.Models;
using Panwar.Api.Services.Edm;

namespace Panwar.Api.Functions.Edm;

/// <summary>
/// Anonymous endpoints the emails and Azure call: the open pixel, the branded unsubscribe page
/// (including RFC 8058 one-click POSTs from Gmail / Apple Mail), and the Event Grid webhook for ACS
/// delivery reports. All three are exempt from the per-IP rate limit (see RateLimitMiddleware)
/// because Gmail's image proxy and Event Grid each arrive from a handful of shared IPs.
/// </summary>
public class EdmPublicFunction
{
    // 1x1 transparent GIF.
    private static readonly byte[] Pixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    private readonly IEdmTrackingService _tracking;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EdmPublicFunction> _logger;

    public EdmPublicFunction(IEdmTrackingService tracking, IConfiguration configuration, ILogger<EdmPublicFunction> logger)
    {
        _tracking = tracking;
        _configuration = configuration;
        _logger = logger;
    }

    [Function("EdmOpenPixel")]
    public async Task<HttpResponseData> Open(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/o/{token}")] HttpRequestData req, FunctionContext context, string token)
    {
        if (Guid.TryParse(Path.GetFileNameWithoutExtension(token), out var id))
        {
            try { await _tracking.RecordOpenAsync(id, context.CancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Failed to record eDM open"); }
        }

        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "image/gif");
        resp.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
        await resp.WriteBytesAsync(Pixel);
        return resp;
    }

    [Function("EdmUnsubscribe")]
    public async Task<HttpResponseData> Unsubscribe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "edm/u/{token}")] HttpRequestData req, FunctionContext context, string token)
    {
        var ct = context.CancellationToken;
        var isPost = req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

        if (token == "preview")
            return await Page(req, UnsubscribePage.Preview());

        if (!Guid.TryParse(token, out var id) || await _tracking.GetUnsubscribeStateAsync(id, ct) is not { } state)
            return await Page(req, UnsubscribePage.Expired(), HttpStatusCode.NotFound);

        // GET only shows the confirm button: link scanners and previewers follow GETs, and they
        // mustn't unsubscribe anyone. Real clicks and one-click POSTs both arrive as POST.
        if (!isPost)
            return await Page(req, state.Unsubscribed
                ? UnsubscribePage.Done(state.Sender, state.Recipient.Email)
                : UnsubscribePage.Confirm(state.Sender, state.Recipient.Email));

        var form = await req.ReadAsStringAsync() ?? "";
        if (form.Contains("action=resubscribe", StringComparison.OrdinalIgnoreCase))
        {
            await _tracking.ResubscribeAsync(id, ct);
            return await Page(req, UnsubscribePage.Resubscribed(state.Sender, state.Recipient.Email));
        }

        await _tracking.UnsubscribeAsync(id, ct);
        return await Page(req, UnsubscribePage.Done(state.Sender, state.Recipient.Email));
    }

    /// <summary>
    /// Event Grid subscription on the ACS resource for Microsoft.Communication.EmailDeliveryReportReceived,
    /// pointed at /api/edm/events?key={EDM_EVENTGRID_KEY}.
    /// </summary>
    [Function("EdmEvents")]
    public async Task<HttpResponseData> Events(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/events")] HttpRequestData req, FunctionContext context)
    {
        var expected = _configuration["EDM_EVENTGRID_KEY"];
        var given = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["key"] ?? "";
        if (string.IsNullOrEmpty(expected)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        using var doc = await JsonDocument.ParseAsync(req.Body, cancellationToken: context.CancellationToken);
        var events = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];

        foreach (var evt in events)
        {
            var type = Str(evt, "eventType");
            if (!evt.TryGetProperty("data", out var data)) continue;

            if (type == "Microsoft.EventGrid.SubscriptionValidationEvent")
            {
                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteAsJsonAsync(new { validationResponse = Str(data, "validationCode") });
                return resp;
            }

            if (type != "Microsoft.Communication.EmailDeliveryReportReceived") continue;

            var messageId = Str(data, "messageId");
            var status = Str(data, "status");
            if (messageId is null || status is null) continue;
            var detail = data.TryGetProperty("deliveryStatusDetails", out var d) ? Str(d, "statusMessage") : null;
            var at = data.TryGetProperty("deliveryAttemptTimeStamp", out var ts) && ts.TryGetDateTime(out var parsed)
                ? parsed.ToUniversalTime()
                : (DateTime?)null;

            await _tracking.ApplyDeliveryReportAsync(messageId, status, detail, at, context.CancellationToken);
        }

        return req.CreateResponse(HttpStatusCode.OK);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static async Task<HttpResponseData> Page(HttpRequestData req, string html, HttpStatusCode status = HttpStatusCode.OK)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("Content-Type", "text/html; charset=utf-8");
        resp.Headers.Add("Cache-Control", "no-store");
        resp.Headers.Add("X-Robots-Tag", "noindex");
        await resp.WriteStringAsync(html);
        return resp;
    }
}

/// <summary>The unsubscribe page, branded with the sender's colour and logo.</summary>
internal static class UnsubscribePage
{
    public static string Confirm(EdmSender s, string email) => Render(s.Name, s.BrandColour, s.LogoUrl,
        "Unsubscribe?",
        $"<strong>{E(email)}</strong> will stop getting {E(s.Name)} emails.",
        Button("Unsubscribe", null, s.BrandColour));

    public static string Done(EdmSender s, string email) => Render(s.Name, s.BrandColour, s.LogoUrl,
        "You're unsubscribed",
        $"{E(email)} won't get any more {E(s.Name)} emails. Changed your mind?",
        Button("Subscribe again", "resubscribe", null));

    public static string Resubscribed(EdmSender s, string email) => Render(s.Name, s.BrandColour, s.LogoUrl,
        "You're subscribed again",
        $"Welcome back. {E(email)} will keep getting {E(s.Name)} emails.",
        "");

    public static string Preview() => Render("Preview", "#702f8f", null,
        "Unsubscribe?",
        "This is a preview link from a test send, so nothing will change.",
        "");

    public static string Expired() => Render("Unsubscribe", "#454646", null,
        "This link has expired",
        "We couldn't find that subscription. Reply to the email you received and we'll take you off the list.",
        "");

    private static string Button(string label, string? action, string? colour)
    {
        var hidden = action is null ? "" : $"<input type=\"hidden\" name=\"action\" value=\"{E(action)}\">";
        var style = colour is null ? "class=\"link\"" : $"class=\"btn\" style=\"background:{E(colour)}\"";
        return $"<form method=\"post\">{hidden}<button type=\"submit\" {style}>{E(label)}</button></form>";
    }

    private static string Render(string brand, string colour, string? logo, string heading, string body, string action)
    {
        var mark = logo is null
            ? $"<div class=\"badge\" style=\"color:{E(colour)}\">{E(brand)}</div>"
            : $"<img class=\"logo\" src=\"{E(logo)}\" alt=\"{E(brand)}\">";
        return $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>{{E(heading)}} · {{E(brand)}}</title>
            <style>
              body{margin:0;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;color:#454646;background:#fff}
              main{max-width:440px;margin:0 auto;padding:72px 24px;text-align:center}
              .badge{font-weight:600;font-size:20px;letter-spacing:.01em}
              .logo{max-height:56px;max-width:220px}
              h1{font-size:28px;margin:32px 0 12px}
              p{font-size:16px;line-height:1.55;color:#6b6b6b;margin:0 0 28px;overflow-wrap:anywhere}
              .btn{border:0;color:#fff;font-size:16px;font-weight:400;padding:12px 28px;border-radius:4px;cursor:pointer}
              .link{border:0;background:none;color:#6b6b6b;font-size:16px;text-decoration:underline;cursor:pointer;padding:0}
              button:focus-visible{outline:3px solid #38c6f4;outline-offset:2px}
            </style></head>
            <body><main>{{mark}}<h1>{{E(heading)}}</h1><p>{{body}}</p>{{action}}</main></body></html>
            """;
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
