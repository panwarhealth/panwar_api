using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

public class ManageInvitesFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] EngagementKeys = { "clicks", "completions", "downloads", "opens" };

    private readonly ILogger<ManageInvitesFunction> _logger;
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateService _templateService;
    private readonly IClientSummaryService _summaryService;
    private readonly IConfiguration _configuration;

    public ManageInvitesFunction(
        ILogger<ManageInvitesFunction> logger,
        AppDbContext context,
        IEmailService emailService,
        IEmailTemplateService templateService,
        IClientSummaryService summaryService,
        IConfiguration configuration)
    {
        _logger = logger;
        _context = context;
        _emailService = emailService;
        _templateService = templateService;
        _summaryService = summaryService;
        _configuration = configuration;
    }

    [Function("ManageListInvites")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/invites")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var items = await _context.ReportInvites.AsNoTracking()
            .Where(i => i.ClientId == client.Id)
            .OrderByDescending(i => i.SentAt)
            .Select(i => new InviteListItemDto(
                i.Id,
                i.RecipientUserId,
                i.RecipientEmail,
                i.Recipient.Name,
                i.Template,
                i.Year,
                i.StartMonth,
                i.EndMonth,
                i.SentAt,
                i.SendCount,
                i.ClickedAt,
                i.ViewedAt))
            .ToListAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new InviteListResponse(items));
        return resp;
    }

    [Function("ManageSendInvites")]
    public async Task<HttpResponseData> Send(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/invites")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var data = await ReadJson<SendInvitesRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var error = Validate(data);
        if (error is not null) return await BadRequest(req, error);

        var recipientIds = data.RecipientUserIds.Distinct().ToList();
        if (recipientIds.Count == 0) return await BadRequest(req, "Pick at least one recipient");

        var members = await _context.UserClients
            .AsNoTracking()
            .Where(uc => uc.ClientId == client.Id && recipientIds.Contains(uc.UserId))
            .Join(_context.Users, uc => uc.UserId, u => u.Id, (uc, u) => u)
            .ToListAsync(ct);
        if (members.Count == 0) return await BadRequest(req, "No valid recipients for this client");

        var apiBase = ApiBase(req);
        var template = data.Template.Trim().ToLowerInvariant();
        var templateFile = TemplateFile(template);
        var periodLabel = PeriodLabel(data.Year, data.StartMonth, data.EndMonth);
        var subject = $"Your {periodLabel} report is ready";
        var now = DateTime.UtcNow;
        var userId = req.GetUserId(context);

        var previewBlock = await BuildPreviewAsync(client.Id, data, ct);

        var sent = 0;
        var failed = new List<string>();

        foreach (var user in members)
        {
            var invite = await _context.ReportInvites.FirstOrDefaultAsync(i =>
                i.ClientId == client.Id &&
                i.RecipientUserId == user.Id &&
                i.Template == template &&
                i.Year == data.Year &&
                i.StartMonth == data.StartMonth &&
                i.EndMonth == data.EndMonth, ct);

            if (invite is null)
            {
                invite = new ReportInvite
                {
                    Id = Guid.NewGuid(),
                    ClientId = client.Id,
                    RecipientUserId = user.Id,
                    RecipientEmail = user.Email,
                    Template = template,
                    Year = data.Year,
                    StartMonth = data.StartMonth,
                    EndMonth = data.EndMonth,
                    Token = GenerateToken(),
                    CreatedAt = now,
                };
                _context.ReportInvites.Add(invite);
            }

            invite.RecipientEmail = user.Email;
            invite.SentAt = now;
            invite.SentBy = userId;
            invite.SendCount += 1;
            invite.UpdatedAt = now;

            var tokens = new Dictionary<string, string>
            {
                ["clientName"] = client.Name,
                ["recipientName"] = string.IsNullOrWhiteSpace(user.Name) ? "there" : user.Name!,
                ["periodLabel"] = periodLabel,
                ["ctaUrl"] = $"{apiBase}/api/r/{invite.Token}",
                ["year"] = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
            };
            var rawTokens = new Dictionary<string, string> { ["previewBlock"] = previewBlock };

            try
            {
                var html = _templateService.Render(templateFile, tokens, rawTokens);
                await _emailService.SendHtmlEmailAsync(user.Email, subject, html, ct);
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send report invite to {Email}", user.Email);
                failed.Add(user.Email);
            }
        }

        await _context.SaveChangesAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { sent, failed });
        return resp;
    }

    [Function("ManagePreviewInvite")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/invites/preview")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var data = await ReadJson<SendInvitesRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");
        var error = Validate(data);
        if (error is not null) return await BadRequest(req, error);

        var template = data.Template.Trim().ToLowerInvariant();
        var periodLabel = PeriodLabel(data.Year, data.StartMonth, data.EndMonth);
        var previewBlock = await BuildPreviewAsync(client.Id, data, ct);

        var tokens = new Dictionary<string, string>
        {
            ["clientName"] = client.Name,
            ["recipientName"] = "there",
            ["periodLabel"] = periodLabel,
            ["ctaUrl"] = "#",
            ["year"] = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
        };
        var rawTokens = new Dictionary<string, string> { ["previewBlock"] = previewBlock };
        var html = _templateService.Render(TemplateFile(template), tokens, rawTokens);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { subject = $"Your {periodLabel} report is ready", html });
        return resp;
    }

    private static string? Validate(SendInvitesRequest data)
    {
        var template = (data.Template ?? "").Trim().ToLowerInvariant();
        if (!InviteTemplates.Allowed.Contains(template)) return "Invalid template";
        if (data.Year < 2000 || data.Year > 2100) return "Year is out of range";
        if (data.StartMonth is null != (data.EndMonth is null)) return "Provide both start and end month, or neither";
        if (data.StartMonth is { } sm && (sm < 1 || sm > 12)) return "Start month is out of range";
        if (data.EndMonth is { } em && (em < 1 || em > 12)) return "End month is out of range";
        if (data.StartMonth is { } s && data.EndMonth is { } e && s > e) return "Start month must be on or before end month";
        if (!PreviewModes.Allowed.Contains((data.PreviewMode ?? "").Trim())) return "Invalid preview mode";
        return null;
    }

    private async Task<string> BuildPreviewAsync(Guid clientId, SendInvitesRequest data, CancellationToken ct)
    {
        var mode = (data.PreviewMode ?? "").Trim().ToLowerInvariant();

        if (mode == PreviewModes.Note)
        {
            var note = data.PreviewNote?.Trim();
            return string.IsNullOrWhiteSpace(note)
                ? ""
                : PreviewSection("Highlight", BuildNoteBlock(WebUtility.HtmlEncode(note)));
        }

        if (mode is PreviewModes.Stats or PreviewModes.Chart or PreviewModes.Summary)
        {
            var from = string.Format(CultureInfo.InvariantCulture, "{0:0000}-{1:00}", data.Year, data.StartMonth ?? 1);
            var to = string.Format(CultureInfo.InvariantCulture, "{0:0000}-{1:00}", data.Year, data.EndMonth ?? 12);
            var summary = await _summaryService.GetSummaryAsync(clientId, from, to, ct);
            if (summary is null) return "";

            if (mode == PreviewModes.Summary)
            {
                var text = summary.Summary?.Text?.Trim();
                return string.IsNullOrWhiteSpace(text)
                    ? ""
                    : PreviewSection("From your report - summary excerpt", BuildSummaryBlock(WebUtility.HtmlEncode(Excerpt(text, 320))));
            }

            if (mode == PreviewModes.Chart)
            {
                var bars = summary.ByBrandAudience
                    .Select(r => (Label: r.Label, Value: r.MediaCost + r.CpdInvestmentCost))
                    .Where(b => b.Value > 0)
                    .OrderByDescending(b => b.Value)
                    .Take(5)
                    .ToList();
                return bars.Count == 0 ? "" : PreviewSection("From your report - spend by brand", BuildChartBlock(bars));
            }

            var spend = summary.Totals.MediaCost + summary.Totals.CpdInvestmentCost;

            decimal engagements = 0;
            foreach (var key in EngagementKeys)
                if (summary.Totals.Metrics.TryGetValue(key, out var v)) engagements += v;

            decimal actual = 0, target = 0;
            foreach (var (key, t) in summary.Totals.TargetMetrics)
            {
                if (t <= 0) continue;
                target += t;
                if (summary.Totals.Metrics.TryGetValue(key, out var a)) actual += a;
            }
            var vsKpi = target > 0
                ? Math.Round(actual / target * 100m).ToString("0", CultureInfo.InvariantCulture) + "%"
                : "-";

            return PreviewSection("From your report - at a glance", BuildStatsBlock(CompactCurrency(spend), CompactNumber(engagements), vsKpi));
        }

        return "";
    }

    // Eyebrow label + content + bottom spacing. Empty when no preview, so the whole section collapses.
    private static string PreviewSection(string label, string inner) =>
        $@"<table cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"">
  <tr><td style=""font-family:Arial,Helvetica,sans-serif;font-size:11px;font-weight:600;color:#aaaaaa;text-transform:uppercase;letter-spacing:0.8px;padding:0 0 10px;"">{WebUtility.HtmlEncode(label)}</td></tr>
  <tr><td style=""padding:0 0 36px;"">{inner}</td></tr>
</table>";

    private static string Excerpt(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = text.LastIndexOf(' ', max);
        return text[..(cut > 0 ? cut : max)].TrimEnd() + "...";
    }

    private static string TruncateLabel(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    private static string BuildChartBlock(IReadOnlyList<(string Label, decimal Value)> bars)
    {
        var max = bars.Max(b => b.Value);
        var rows = string.Concat(bars.Select(b =>
        {
            var pct = max > 0 ? (int)Math.Round(b.Value / max * 100m) : 0;
            if (pct < 2) pct = 2;
            return $@"<tr>
    <td valign=""middle"" width=""50%"" style=""width:50%;font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#555555;white-space:nowrap;padding:6px 10px 6px 0;"">{WebUtility.HtmlEncode(TruncateLabel(b.Label, 26))}</td>
    <td valign=""middle"" width=""100%"" style=""padding:6px 0;"">
      <table cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""><tr>
        <td width=""{pct}%"" bgcolor=""#702f8f"" style=""height:14px;border-radius:3px;font-size:0;line-height:0;mso-line-height-rule:exactly;"">&nbsp;</td>
        <td style=""font-size:0;line-height:0;"">&nbsp;</td>
      </tr></table>
    </td>
    <td valign=""middle"" style=""font-family:Arial,Helvetica,sans-serif;font-size:13px;font-weight:700;color:#702f8f;padding:6px 0 6px 10px;white-space:nowrap;text-align:right;"">{CompactCurrency(b.Value)}</td>
  </tr>";
        }));
        return PreviewCard($@"<table cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"">{rows}</table>");
    }

    private static string BuildStatsBlock(string spend, string engagements, string vsKpi)
    {
        string LabelCell(string label) =>
            $@"<td align=""center"" valign=""top"" height=""15"" style=""width:33%;height:15px;padding:0 6px;font-family:Arial,Helvetica,sans-serif;font-size:10px;color:#999999;text-transform:uppercase;letter-spacing:0.3px;line-height:13px;"">{label}</td>";
        string ValueCell(string value) =>
            $@"<td align=""center"" valign=""top"" style=""width:33%;padding:8px 6px 0;font-family:Arial,Helvetica,sans-serif;font-size:22px;font-weight:700;color:#702f8f;line-height:1.1;white-space:nowrap;"">{value}</td>";
        const string divider = @"<td rowspan=""2"" width=""1"" bgcolor=""#e5e0ee"" style=""width:1px;font-size:0;line-height:0;mso-line-height-rule:exactly;"">&nbsp;</td>";
        return PreviewCard($@"<table cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"">
  <tr>{LabelCell("Spend")}{divider}{LabelCell("Engagement")}{divider}{LabelCell("vs KPI")}</tr>
  <tr>{ValueCell(spend)}{ValueCell(engagements)}{ValueCell(vsKpi)}</tr>
</table>");
    }

    private static string BuildSummaryBlock(string text) =>
        PreviewCard($@"<div style=""font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:23px;color:#8a8a8a;font-style:italic;"">{text}</div>");

    // A framed panel that reads as a peek into the report, distinct from the email's own copy.
    private static string PreviewCard(string inner) =>
        $@"<table cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""border:1px solid #e2dcef;border-radius:8px;background-color:#faf9fd;"">
  <tr><td style=""padding:16px 18px;"">{inner}</td></tr>
</table>";

    private static string BuildNoteBlock(string note) =>
        $@"<table cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"">
  <tr>
    <td width=""3"" valign=""top"" style=""background-color:#702f8f;border-radius:2px;font-size:0;line-height:0;mso-line-height-rule:exactly;"">&nbsp;</td>
    <td style=""padding-left:16px;font-size:15px;font-family:Arial,Helvetica,sans-serif;color:#333333;line-height:26px;"">{note}</td>
  </tr>
</table>";

    private static string CompactCurrency(decimal v)
    {
        if (v >= 1_000_000m) return "$" + (v / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (v >= 1_000m) return "$" + (v / 1_000m).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return "$" + v.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string CompactNumber(decimal v)
    {
        if (v >= 1_000_000m) return (v / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (v >= 1_000m) return (v / 1_000m).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return v.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string TemplateFile(string template) => template switch
    {
        InviteTemplates.ReportReady => "email-report-ready.html",
        _ => throw new ArgumentOutOfRangeException(nameof(template), template, "No template file"),
    };

    private static string PeriodLabel(int year, int? startMonth, int? endMonth)
    {
        if (startMonth is null || endMonth is null) return year.ToString(CultureInfo.InvariantCulture);
        string Name(int m) => new DateTime(year, m, 1).ToString("MMMM", CultureInfo.InvariantCulture);
        return startMonth == endMonth
            ? $"{Name(startMonth.Value)} {year}"
            : $"{Name(startMonth.Value)} - {Name(endMonth.Value)} {year}";
    }

    // The email's tracked link points back at this API. Derive the public base from the
    // request (custom domain / forwarded host) so no extra App Setting is needed; an
    // optional API_BASE_URL override still wins if set.
    private string ApiBase(HttpRequestData req)
    {
        var configured = _configuration["API_BASE_URL"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');

        var host = req.Headers.TryGetValues("X-Forwarded-Host", out var h) ? h.FirstOrDefault()?.Split(',')[0].Trim() : null;
        var proto = req.Headers.TryGetValues("X-Forwarded-Proto", out var p) ? p.FirstOrDefault()?.Split(',')[0].Trim() : null;
        return !string.IsNullOrEmpty(host)
            ? $"{(string.IsNullOrEmpty(proto) ? req.Url.Scheme : proto)}://{host}"
            : req.Url.GetLeftPart(UriPartial.Authority);
    }

    private static string GenerateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private static bool CanManage(HttpRequestData req, FunctionContext context)
        => req.HasRole(context, "panwar-admin") || req.HasRole(context, "dashboard-editor");

    private static async Task<T?> ReadJson<T>(HttpRequestData req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var resp = req.CreateResponse(HttpStatusCode.BadRequest);
        await resp.WriteAsJsonAsync(new { error = message });
        return resp;
    }

    private static async Task<HttpResponseData> NotFound(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.NotFound);
        await resp.WriteAsJsonAsync(new { error = "Not found" });
        return resp;
    }
}
