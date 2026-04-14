using System.Net;
using System.Text.Json;
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
/// Per-client baselines — the expected performance value for a (client, publisher,
/// template, metric) combination. Editors use these as defaults when setting
/// placement KPI targets.
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

        var baselines = await _context.ClientPublisherBaselines
            .AsNoTracking()
            .Include(b => b.Publisher)
            .Include(b => b.Template)
            .Where(b => b.ClientId == client.Id)
            .OrderBy(b => b.Publisher.Name)
            .ThenBy(b => b.MetricKey)
            .Select(b => new BaselineDto(
                b.Id, b.PublisherId, b.Publisher.Name,
                b.TemplateId, b.Template.Code.ToString().ToLower(),
                b.MetricKey, b.Value, b.EffectiveFrom, b.Note))
            .ToListAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { baselines });
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
        if (string.IsNullOrWhiteSpace(data.MetricKey)) return await BadRequest(req, "Metric key is required");

        // Verify FKs exist
        var publisherExists = await _context.Publishers.AnyAsync(p => p.Id == data.PublisherId, ct);
        if (!publisherExists) return await BadRequest(req, "Unknown publisher");
        var templateExists = await _context.MetricTemplates.AnyAsync(t => t.Id == data.TemplateId, ct);
        if (!templateExists) return await BadRequest(req, "Unknown template");

        var baseline = new ClientPublisherBaseline
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            PublisherId = data.PublisherId,
            TemplateId = data.TemplateId,
            MetricKey = data.MetricKey.Trim().ToLowerInvariant(),
            Value = data.Value,
            EffectiveFrom = data.EffectiveFrom,
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

        baseline.PublisherId = data.PublisherId;
        baseline.TemplateId = data.TemplateId;
        baseline.MetricKey = data.MetricKey.Trim().ToLowerInvariant();
        baseline.Value = data.Value;
        baseline.EffectiveFrom = data.EffectiveFrom;
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

    private async Task<HttpResponseData> LoadAndReturn(HttpRequestData req, Guid id, HttpStatusCode status, CancellationToken ct)
    {
        var dto = await _context.ClientPublisherBaselines
            .AsNoTracking()
            .Include(b => b.Publisher)
            .Include(b => b.Template)
            .Where(b => b.Id == id)
            .Select(b => new BaselineDto(
                b.Id, b.PublisherId, b.Publisher.Name,
                b.TemplateId, b.Template.Code.ToString().ToLower(),
                b.MetricKey, b.Value, b.EffectiveFrom, b.Note))
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
