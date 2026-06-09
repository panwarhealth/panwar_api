using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.CloudflareR2;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

/// <summary>
/// Placement CRUD for the employee portal's Dashboard Updater — the editor
/// workflow behind each Reckitt-style card (artwork + commercials + KPI targets
/// + monthly actuals). A placement is scoped to a client through its brand;
/// publisher and template are shared registries. The template determines which
/// metric keys are valid for KPIs and actuals.
/// </summary>
public class ManagePlacementsFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly HashSet<string> AllowedArtworkContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif", "application/pdf",
    };

    private readonly ILogger<ManagePlacementsFunction> _logger;
    private readonly AppDbContext _context;
    private readonly ICloudflareR2Service _r2;

    public ManagePlacementsFunction(
        ILogger<ManagePlacementsFunction> logger,
        AppDbContext context,
        ICloudflareR2Service r2)
    {
        _logger = logger;
        _context = context;
        _r2 = r2;
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [Function("ManageListPlacements")]
    public async Task<HttpResponseData> ListPlacements(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/placements")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var filters = HttpUtility.ParseQueryString(req.Url.Query);
        var query = _context.Placements
            .AsNoTracking()
            .Where(p => p.Brand.ClientId == client.Id);

        if (Guid.TryParse(filters["brandId"], out var brandId))
            query = query.Where(p => p.BrandId == brandId);
        if (Guid.TryParse(filters["audienceId"], out var audienceId))
            query = query.Where(p => p.AudienceId == audienceId);
        if (Guid.TryParse(filters["publisherId"], out var publisherId))
            query = query.Where(p => p.PublisherId == publisherId);

        var placements = await query
            .OrderBy(p => p.Brand.Name)
            .ThenBy(p => p.Audience.Name)
            .ThenBy(p => p.Name)
            .Select(p => new PlacementListItemDto(
                p.Id,
                p.BrandId, p.Brand.Name,
                p.AudienceId, p.Audience.Name,
                p.PublisherId, p.Publisher.Name,
                p.TemplateId, p.Template.Code.ToString().ToLower(),
                p.Name,
                p.Objective.ToString().ToLower(),
                p.AssetType,
                p.CreativeCode,
                p.OsCode,
                p.ArtworkUrl,
                p.LiveMonths,
                p.MediaCost,
                p.PlannedMediaCost,
                p.CpdInvestmentCost,
                p.IsBonus,
                p.IsCpdPackage))
            .ToListAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { placements });
        return resp;
    }

    // ── Detail ───────────────────────────────────────────────────────────────

    [Function("ManageGetPlacement")]
    public async Task<HttpResponseData> GetPlacement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/placements/{placementId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string placementId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(placementId, out var id)) return await BadRequest(req, "Invalid placement id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var dto = await LoadDetail(id, client.Id, ct);
        if (dto is null) return await NotFound(req);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(dto);
        return resp;
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Function("ManageCreatePlacement")]
    public async Task<HttpResponseData> CreatePlacement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/placements")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var data = await ReadJson<PlacementWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var (error, objective, liveMonths) = await ValidateWrite(client.Id, data, ct);
        if (error is not null) return await BadRequest(req, error);

        var now = DateTime.UtcNow;
        var userId = req.GetUserId(context);
        var placement = new Placement
        {
            Id = Guid.NewGuid(),
            BrandId = data.BrandId,
            AudienceId = data.AudienceId,
            PublisherId = data.PublisherId,
            TemplateId = data.TemplateId,
            Name = data.Name.Trim(),
            Objective = objective,
            AssetType = Clean(data.AssetType),
            CreativeCode = Clean(data.CreativeCode),
            OsCode = Clean(data.OsCode),
            UtmUrl = Clean(data.UtmUrl),
            ArtworkUrl = Clean(data.ArtworkUrl),
            Comments = Clean(data.Comments),
            Notes = Clean(data.Notes),
            LiveMonths = liveMonths,
            MediaCost = data.MediaCost,
            PlannedMediaCost = data.PlannedMediaCost,
            CpdInvestmentCost = data.CpdInvestmentCost,
            IsBonus = data.IsBonus,
            IsCpdPackage = data.IsCpdPackage,
            Circulation = data.Circulation,
            PlacementsCount = data.PlacementsCount,
            TargetCourseId = data.TargetCourseId,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
            UpdatedBy = userId,
        };
        _context.Placements.Add(placement);

        // Seed KPI targets from the client's baselines for this publisher + template.
        // Most recent baseline per metric wins; the editor can override via PUT /kpis.
        var baselines = await _context.ClientPublisherBaselines
            .Where(b => b.ClientId == client.Id
                     && b.PublisherId == data.PublisherId
                     && b.TemplateId == data.TemplateId)
            .ToListAsync(ct);

        var seededKpis = baselines
            .GroupBy(b => b.MetricKey)
            .Select(g => g.OrderByDescending(b => b.EffectiveFrom).First())
            .Select(b => new PlacementKpi
            {
                Id = Guid.NewGuid(),
                PlacementId = placement.Id,
                MetricKey = b.MetricKey,
                TargetValue = b.Value,
            });
        _context.PlacementKpis.AddRange(seededKpis);

        await _context.SaveChangesAsync(ct);

        var dto = await LoadDetail(placement.Id, client.Id, ct);
        var resp = req.CreateResponse(HttpStatusCode.Created);
        await resp.WriteAsJsonAsync(dto);
        return resp;
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Function("ManageUpdatePlacement")]
    public async Task<HttpResponseData> UpdatePlacement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/placements/{placementId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string placementId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(placementId, out var id)) return await BadRequest(req, "Invalid placement id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var placement = await _context.Placements
            .FirstOrDefaultAsync(p => p.Id == id && p.Brand.ClientId == client.Id, ct);
        if (placement is null) return await NotFound(req);

        var data = await ReadJson<PlacementWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var (error, objective, liveMonths) = await ValidateWrite(client.Id, data, ct);
        if (error is not null) return await BadRequest(req, error);

        placement.BrandId = data.BrandId;
        placement.AudienceId = data.AudienceId;
        placement.PublisherId = data.PublisherId;
        placement.TemplateId = data.TemplateId;
        placement.Name = data.Name.Trim();
        placement.Objective = objective;
        placement.AssetType = Clean(data.AssetType);
        placement.CreativeCode = Clean(data.CreativeCode);
        placement.OsCode = Clean(data.OsCode);
        placement.UtmUrl = Clean(data.UtmUrl);
        placement.ArtworkUrl = Clean(data.ArtworkUrl);
        placement.Comments = Clean(data.Comments);
        placement.Notes = Clean(data.Notes);
        placement.LiveMonths = liveMonths;
        placement.MediaCost = data.MediaCost;
        placement.PlannedMediaCost = data.PlannedMediaCost;
        placement.CpdInvestmentCost = data.CpdInvestmentCost;
        placement.IsBonus = data.IsBonus;
        placement.IsCpdPackage = data.IsCpdPackage;
        placement.Circulation = data.Circulation;
        placement.PlacementsCount = data.PlacementsCount;
        placement.TargetCourseId = data.TargetCourseId;
        placement.UpdatedAt = DateTime.UtcNow;
        placement.UpdatedBy = req.GetUserId(context);

        await _context.SaveChangesAsync(ct);

        var dto = await LoadDetail(placement.Id, client.Id, ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(dto);
        return resp;
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Function("ManageDeletePlacement")]
    public async Task<HttpResponseData> DeletePlacement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/placements/{placementId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string placementId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(placementId, out var id)) return await BadRequest(req, "Invalid placement id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var placement = await _context.Placements
            .FirstOrDefaultAsync(p => p.Id == id && p.Brand.ClientId == client.Id, ct);
        if (placement is null) return req.CreateResponse(HttpStatusCode.NoContent);

        // KPIs, actuals and comments cascade at the DB level. Artwork in R2 is
        // best-effort cleaned up; a failed delete there must not block the row removal.
        if (!string.IsNullOrWhiteSpace(placement.ArtworkUrl))
        {
            try { await _r2.DeleteAsync(placement.ArtworkUrl, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete artwork {Key} for placement {Id}", placement.ArtworkUrl, id); }
        }

        _context.Placements.Remove(placement);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── KPI targets ──────────────────────────────────────────────────────────

    [Function("ManageSetPlacementKpis")]
    public async Task<HttpResponseData> SetKpis(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/clients/{clientSlug}/placements/{placementId}/kpis")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string placementId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(placementId, out var id)) return await BadRequest(req, "Invalid placement id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var placement = await _context.Placements
            .Include(p => p.Kpis)
            .FirstOrDefaultAsync(p => p.Id == id && p.Brand.ClientId == client.Id, ct);
        if (placement is null) return await NotFound(req);

        var data = await ReadJson<PlacementKpiWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var validKeys = await StorableMetricKeys(placement.TemplateId, ct);
        var incoming = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var kpi in data.Kpis)
        {
            var key = (kpi.MetricKey ?? "").Trim().ToLowerInvariant();
            if (key.Length == 0) return await BadRequest(req, "Each KPI requires a metric key");
            if (!validKeys.Contains(key)) return await BadRequest(req, $"Metric '{key}' is not part of this placement's template");
            incoming[key] = kpi.TargetValue;  // last write wins on duplicate keys
        }

        // Full replace: drop existing, insert the provided set.
        _context.PlacementKpis.RemoveRange(placement.Kpis);
        _context.PlacementKpis.AddRange(incoming.Select(kv => new PlacementKpi
        {
            Id = Guid.NewGuid(),
            PlacementId = placement.Id,
            MetricKey = kv.Key,
            TargetValue = kv.Value,
        }));
        await _context.SaveChangesAsync(ct);

        var dto = await LoadDetail(placement.Id, client.Id, ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(dto);
        return resp;
    }

    // ── Monthly actuals ──────────────────────────────────────────────────────

    [Function("ManageSetPlacementActuals")]
    public async Task<HttpResponseData> SetActuals(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/clients/{clientSlug}/placements/{placementId}/actuals")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string placementId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(placementId, out var id)) return await BadRequest(req, "Invalid placement id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var placement = await _context.Placements
            .Include(p => p.Actuals)
            .FirstOrDefaultAsync(p => p.Id == id && p.Brand.ClientId == client.Id, ct);
        if (placement is null) return await NotFound(req);

        var data = await ReadJson<PlacementActualsWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var validKeys = await StorableMetricKeys(placement.TemplateId, ct);
        foreach (var row in data.Actuals)
        {
            if (row.Month is < 1 or > 12) return await BadRequest(req, $"Invalid month: {row.Month}");
            if (row.Year is < 2000 or > 2100) return await BadRequest(req, $"Invalid year: {row.Year}");
            var key = (row.MetricKey ?? "").Trim().ToLowerInvariant();
            if (key.Length == 0) return await BadRequest(req, "Each actual requires a metric key");
            if (!validKeys.Contains(key)) return await BadRequest(req, $"Metric '{key}' is not part of this placement's template");
        }

        // Upsert by (year, month, metricKey). Months absent from the payload are left as-is.
        foreach (var row in data.Actuals)
        {
            var key = row.MetricKey.Trim().ToLowerInvariant();
            var existing = placement.Actuals.FirstOrDefault(a =>
                a.Year == row.Year && a.Month == row.Month &&
                string.Equals(a.MetricKey, key, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _context.PlacementActuals.Add(new PlacementActual
                {
                    Id = Guid.NewGuid(),
                    PlacementId = placement.Id,
                    Year = row.Year,
                    Month = row.Month,
                    MetricKey = key,
                    Value = row.Value,
                    Note = Clean(row.Note),
                });
            }
            else
            {
                existing.Value = row.Value;
                existing.Note = Clean(row.Note);
            }
        }
        await _context.SaveChangesAsync(ct);

        var dto = await LoadDetail(placement.Id, client.Id, ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(dto);
        return resp;
    }

    // ── Artwork upload URL ───────────────────────────────────────────────────

    [Function("ManagePlacementArtworkUploadUrl")]
    public async Task<HttpResponseData> ArtworkUploadUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/placements/{placementId}/artwork-upload-url")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string placementId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(placementId, out var id)) return await BadRequest(req, "Invalid placement id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var exists = await _context.Placements
            .AnyAsync(p => p.Id == id && p.Brand.ClientId == client.Id, ct);
        if (!exists) return await NotFound(req);

        var data = await ReadJson<ArtworkUploadUrlRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");
        if (string.IsNullOrWhiteSpace(data.FileName)) return await BadRequest(req, "File name is required");
        if (!AllowedArtworkContentTypes.Contains(data.ContentType))
            return await BadRequest(req, "Unsupported file type — use PNG, JPEG, WebP, GIF or PDF");

        var (uploadUrl, objectKey) = await _r2.GenerateUploadUrlAsync(data.FileName.Trim(), data.ContentType, ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new ArtworkUploadUrlResponse(uploadUrl, objectKey));
        return resp;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the foreign keys and scalar fields shared by create and update.
    /// Returns an error message (or null) plus the parsed objective and sanitised
    /// live-months array.
    /// </summary>
    private async Task<(string? error, PlacementObjective objective, int[] liveMonths)> ValidateWrite(
        Guid clientId, PlacementWriteRequest data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.Name))
            return ("Name is required", default, Array.Empty<int>());

        if (!Enum.TryParse<PlacementObjective>(data.Objective, ignoreCase: true, out var objective))
            return ($"Invalid objective '{data.Objective}'", default, Array.Empty<int>());

        if (data.LiveMonths.Any(m => m is < 1 or > 12))
            return ("Live months must be between 1 and 12", default, Array.Empty<int>());
        var liveMonths = data.LiveMonths.Distinct().OrderBy(m => m).ToArray();

        var brandOk = await _context.Brands.AnyAsync(b => b.Id == data.BrandId && b.ClientId == clientId, ct);
        if (!brandOk) return ("Unknown brand for this client", objective, liveMonths);

        var audienceOk = await _context.Audiences.AnyAsync(a => a.Id == data.AudienceId && a.ClientId == clientId, ct);
        if (!audienceOk) return ("Unknown audience for this client", objective, liveMonths);

        var publisherOk = await _context.Publishers.AnyAsync(p => p.Id == data.PublisherId, ct);
        if (!publisherOk) return ("Unknown publisher", objective, liveMonths);

        var templateOk = await _context.MetricTemplates.AnyAsync(t => t.Id == data.TemplateId, ct);
        if (!templateOk) return ("Unknown template", objective, liveMonths);

        var publisherSupportsTemplate = await _context.PublisherTemplates
            .AnyAsync(pt => pt.PublisherId == data.PublisherId && pt.TemplateId == data.TemplateId, ct);
        if (!publisherSupportsTemplate)
            return ("This publisher does not offer the selected template", objective, liveMonths);

        if (data.TargetCourseId is { } courseId)
        {
            var courseOk = await _context.EducationCourses
                .AnyAsync(c => c.Id == courseId && c.Brand.ClientId == clientId, ct);
            if (!courseOk) return ("Unknown target course for this client", objective, liveMonths);
        }

        return (null, objective, liveMonths);
    }

    /// <summary>The stored (non-calculated) metric keys valid for a template.</summary>
    private async Task<HashSet<string>> StorableMetricKeys(Guid templateId, CancellationToken ct)
    {
        var keys = await _context.MetricFields
            .Where(f => f.TemplateId == templateId && !f.IsCalculated)
            .Select(f => f.Key)
            .ToListAsync(ct);
        return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<PlacementDetailDto?> LoadDetail(Guid id, Guid clientId, CancellationToken ct)
    {
        var p = await _context.Placements
            .AsNoTracking()
            .Include(x => x.Brand)
            .Include(x => x.Audience)
            .Include(x => x.Publisher)
            .Include(x => x.Template)
            .Include(x => x.Kpis)
            .Include(x => x.Actuals)
            .FirstOrDefaultAsync(x => x.Id == id && x.Brand.ClientId == clientId, ct);
        if (p is null) return null;

        string? artworkViewUrl = null;
        if (!string.IsNullOrWhiteSpace(p.ArtworkUrl))
            artworkViewUrl = await _r2.GenerateDownloadUrlAsync(p.ArtworkUrl, ct);

        return new PlacementDetailDto(
            p.Id,
            p.BrandId, p.Brand.Name,
            p.AudienceId, p.Audience.Name,
            p.PublisherId, p.Publisher.Name,
            p.TemplateId, p.Template.Code.ToString().ToLower(), p.Template.Name,
            p.Name,
            p.Objective.ToString().ToLower(),
            p.AssetType,
            p.CreativeCode,
            p.OsCode,
            p.UtmUrl,
            p.ArtworkUrl,
            artworkViewUrl,
            p.Comments,
            p.Notes,
            p.LiveMonths,
            p.MediaCost,
            p.PlannedMediaCost,
            p.CpdInvestmentCost,
            p.IsBonus,
            p.IsCpdPackage,
            p.Circulation,
            p.PlacementsCount,
            p.TargetCourseId,
            p.Kpis
                .OrderBy(k => k.MetricKey)
                .Select(k => new PlacementKpiDto(k.MetricKey, k.TargetValue))
                .ToList(),
            p.Actuals
                .OrderBy(a => a.Year).ThenBy(a => a.Month).ThenBy(a => a.MetricKey)
                .Select(a => new PlacementActualDto(a.Year, a.Month, a.MetricKey, a.Value, a.Note))
                .ToList());
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
