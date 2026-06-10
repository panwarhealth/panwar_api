using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

/// <summary>
/// Year-scoped KPI targets per (client, publisher, template, metric). Clients
/// hand these over before the year starts; creating a placement for that year
/// auto-populates its KPI targets from them (editors can override per placement).
/// </summary>
public class ManageBaselinesFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<ManageBaselinesFunction> _logger;
    private readonly AppDbContext _context;

    public ManageBaselinesFunction(ILogger<ManageBaselinesFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Function("ManageListBaselines")]
    public async Task<HttpResponseData> ListBaselines(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/baselines")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var filters = HttpUtility.ParseQueryString(req.Url.Query);
        var query = _context.ClientPublisherBaselines
            .AsNoTracking()
            .Where(b => b.ClientId == client.Id);
        if (int.TryParse(filters["year"], out var year))
            query = query.Where(b => b.Year == year);

        var baselines = await query
            .OrderBy(b => b.Publisher.Name)
            .ThenBy(b => b.MetricKey)
            .Select(b => new BaselineDto(
                b.Id, b.PublisherId, b.Publisher.Name,
                b.TemplateId, b.Template.Code.ToString().ToLower(),
                b.Year, b.MetricKey, b.Value, b.Note))
            .ToListAsync(ct);

        // Every year that has targets for this client (unfiltered) so the tab's
        // year picker can populate its options.
        var years = await _context.ClientPublisherBaselines
            .AsNoTracking()
            .Where(b => b.ClientId == client.Id)
            .Select(b => b.Year)
            .Distinct()
            .OrderBy(y => y)
            .ToListAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { baselines, years });
        return resp;
    }

    [Function("ManageCreateBaseline")]
    public async Task<HttpResponseData> CreateBaseline(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/baselines")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var data = await ReadJson<BaselineWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var error = await Validate(client.Id, data, excludeId: null, ct);
        if (error is not null) return await BadRequest(req, error);

        var baseline = new ClientPublisherBaseline
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            PublisherId = data.PublisherId,
            TemplateId = data.TemplateId,
            Year = data.Year,
            MetricKey = data.MetricKey.Trim().ToLowerInvariant(),
            Value = data.Value,
            Note = string.IsNullOrWhiteSpace(data.Note) ? null : data.Note.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.ClientPublisherBaselines.Add(baseline);
        await _context.SaveChangesAsync(ct);
        return await LoadAndReturn(req, baseline.Id, HttpStatusCode.Created, ct);
    }

    [Function("ManageUpdateBaseline")]
    public async Task<HttpResponseData> UpdateBaseline(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/baselines/{baselineId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string baselineId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(baselineId, out var id)) return await BadRequest(req, "Invalid baseline id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var baseline = await _context.ClientPublisherBaselines
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == client.Id, ct);
        if (baseline is null) return await NotFound(req);

        var data = await ReadJson<BaselineWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var error = await Validate(client.Id, data, excludeId: id, ct);
        if (error is not null) return await BadRequest(req, error);

        baseline.PublisherId = data.PublisherId;
        baseline.TemplateId = data.TemplateId;
        baseline.Year = data.Year;
        baseline.MetricKey = data.MetricKey.Trim().ToLowerInvariant();
        baseline.Value = data.Value;
        baseline.Note = string.IsNullOrWhiteSpace(data.Note) ? null : data.Note.Trim();
        baseline.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return await LoadAndReturn(req, baseline.Id, HttpStatusCode.OK, ct);
    }

    [Function("ManageDeleteBaseline")]
    public async Task<HttpResponseData> DeleteBaseline(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/baselines/{baselineId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string baselineId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(baselineId, out var id)) return await BadRequest(req, "Invalid baseline id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var baseline = await _context.ClientPublisherBaselines
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == client.Id, ct);
        if (baseline is null) return req.CreateResponse(HttpStatusCode.NoContent);

        _context.ClientPublisherBaselines.Remove(baseline);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    /// <summary>Shared create/update validation, including the friendly duplicate check.</summary>
    private async Task<string?> Validate(Guid clientId, BaselineWriteRequest data, Guid? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.MetricKey)) return "Metric key is required";
        if (data.Year is < 2000 or > 2100) return $"Invalid year: {data.Year}";

        var publisherExists = await _context.Publishers.AnyAsync(p => p.Id == data.PublisherId, ct);
        if (!publisherExists) return "Unknown publisher";
        var templateExists = await _context.MetricTemplates.AnyAsync(t => t.Id == data.TemplateId, ct);
        if (!templateExists) return "Unknown template";

        var metricKey = data.MetricKey.Trim().ToLowerInvariant();
        var duplicate = await _context.ClientPublisherBaselines.AnyAsync(b =>
            b.ClientId == clientId
            && b.PublisherId == data.PublisherId
            && b.TemplateId == data.TemplateId
            && b.MetricKey == metricKey
            && b.Year == data.Year
            && (excludeId == null || b.Id != excludeId), ct);
        if (duplicate) return $"A {data.Year} target for this publisher + metric already exists - edit it instead";

        return null;
    }

    private async Task<HttpResponseData> LoadAndReturn(HttpRequestData req, Guid id, HttpStatusCode status, CancellationToken ct)
    {
        var dto = await _context.ClientPublisherBaselines
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BaselineDto(
                b.Id, b.PublisherId, b.Publisher.Name,
                b.TemplateId, b.Template.Code.ToString().ToLower(),
                b.Year, b.MetricKey, b.Value, b.Note))
            .FirstAsync(ct);
        var resp = req.CreateResponse(status);
        await resp.WriteAsJsonAsync(dto);
        return resp;
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
