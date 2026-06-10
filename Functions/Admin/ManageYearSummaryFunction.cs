using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

/// <summary>
/// The analyst-written yearly summary for a client (the workbook's FY RESULTS
/// commentary). One text per (client, year):
///   GET /api/manage/clients/{clientSlug}/summary?year=YYYY
///   PUT /api/manage/clients/{clientSlug}/summary   { year, text }  (empty text deletes)
/// </summary>
public class ManageYearSummaryFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<ManageYearSummaryFunction> _logger;
    private readonly AppDbContext _context;

    public ManageYearSummaryFunction(ILogger<ManageYearSummaryFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public sealed record YearSummaryDto(int Year, string Text, DateTime? UpdatedAt);

    public class YearSummaryWriteRequest
    {
        public int Year { get; set; }
        public string Text { get; set; } = "";
    }

    [Function("ManageGetYearSummary")]
    public async Task<HttpResponseData> GetSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/summary")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var query = HttpUtility.ParseQueryString(req.Url.Query);
        if (!int.TryParse(query["year"], out var year))
            return await BadRequest(req, "A year query parameter is required");

        var summary = await _context.ClientYearSummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ClientId == client.Id && s.Year == year, ct);

        // Years that already have a summary, so the tab's year picker can mark them.
        var years = await _context.ClientYearSummaries
            .AsNoTracking()
            .Where(s => s.ClientId == client.Id)
            .Select(s => s.Year)
            .OrderBy(y => y)
            .ToListAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            summary = summary is null ? null : new YearSummaryDto(summary.Year, summary.Text, summary.UpdatedAt),
            years,
        });
        return resp;
    }

    [Function("ManagePutYearSummary")]
    public async Task<HttpResponseData> PutSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/clients/{clientSlug}/summary")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var data = JsonSerializer.Deserialize<YearSummaryWriteRequest>(body, JsonOptions);
        if (data is null) return await BadRequest(req, "Request body required");
        if (data.Year is < 2000 or > 2100) return await BadRequest(req, $"Invalid year: {data.Year}");

        var text = data.Text.Trim();
        var existing = await _context.ClientYearSummaries
            .FirstOrDefaultAsync(s => s.ClientId == client.Id && s.Year == data.Year, ct);

        if (text.Length == 0)
        {
            // Clearing the text removes the summary for that year.
            if (existing is not null)
            {
                _context.ClientYearSummaries.Remove(existing);
                await _context.SaveChangesAsync(ct);
            }
            return req.CreateResponse(HttpStatusCode.NoContent);
        }

        if (existing is null)
        {
            existing = new ClientYearSummary
            {
                Id = Guid.NewGuid(),
                ClientId = client.Id,
                Year = data.Year,
                Text = text,
            };
            _context.ClientYearSummaries.Add(existing);
        }
        else
        {
            existing.Text = text;
        }
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedByUserId = req.GetUserId(context);
        await _context.SaveChangesAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new YearSummaryDto(existing.Year, existing.Text, existing.UpdatedAt));
        return resp;
    }

    private static bool CanManage(HttpRequestData req, FunctionContext context)
        => req.HasRole(context, "panwar-admin") || req.HasRole(context, "dashboard-editor");

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
