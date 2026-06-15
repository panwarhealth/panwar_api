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
using Panwar.Api.Services;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

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
        if (int.TryParse(filters["year"], out var year))
        {
            // Date-shape branches mirror PeriodWindow.AppearsInWindow — no EF-friendly way to call it.
            query = query.Where(p =>
                (p.EndDate != null && p.StartDate!.Value.Year <= year && p.EndDate!.Value.Year >= year)
                || (p.StartDate != null && p.EndDate == null && p.StartDate!.Value.Year == year)
                || (p.StartDate == null && p.Year == year));
        }

        var rows = await query
            .OrderBy(p => p.Brand.Name)
            .ThenBy(p => p.Audience.Name)
            .ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.BrandId, BrandName = p.Brand.Name,
                p.AudienceId, AudienceName = p.Audience.Name,
                p.PublisherId, PublisherName = p.Publisher.Name,
                p.TemplateId, TemplateCode = p.Template.Code,
                p.Year, p.Name, p.Objective,
                p.AssetType, p.OsCode, p.ArtworkUrl,
                p.LiveMonths, p.StartDate, p.EndDate,
                p.EdmSubcategory, p.EducationSubcategory, p.GroupId,
                p.MediaCost, p.PlannedMediaCost, p.CpdInvestmentCost,
                p.IsBonus, p.IsCpdPackage,
            })
            .ToListAsync(ct);

        var placements = rows.Select(r => new PlacementListItemDto(
            r.Id,
            r.BrandId, r.BrandName,
            r.AudienceId, r.AudienceName,
            r.PublisherId, r.PublisherName,
            r.TemplateId, r.TemplateCode.ToString().ToLower(),
            r.Year,
            r.Name,
            r.Objective.ToString().ToLower(),
            r.AssetType,
            r.OsCode,
            r.ArtworkUrl,
            r.LiveMonths,
            r.StartDate?.ToString("yyyy-MM-dd"),
            r.EndDate?.ToString("yyyy-MM-dd"),
            r.EdmSubcategory.HasValue ? PlacementEnumNames.ToName(r.EdmSubcategory.Value) : null,
            r.EducationSubcategory.HasValue ? PlacementEnumNames.ToName(r.EducationSubcategory.Value) : null,
            r.GroupId,
            r.MediaCost,
            r.PlannedMediaCost,
            r.CpdInvestmentCost,
            r.IsBonus,
            r.IsCpdPackage)).ToList();

        var spans = await _context.Placements
            .AsNoTracking()
            .Where(p => p.Brand.ClientId == client.Id)
            .Select(p => new { p.Year, p.StartDate, p.EndDate })
            .ToListAsync(ct);
        var yearSet = new SortedSet<int>();
        foreach (var s in spans)
        {
            if (s.StartDate is { } sd && s.EndDate is { } ed)
                for (var y = sd.Year; y <= ed.Year; y++) yearSet.Add(y);
            else if (s.StartDate is { } only)
                yearSet.Add(only.Year);
            else
                yearSet.Add(s.Year);
        }
        var years = yearSet.ToList();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { placements, years });
        return resp;
    }

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

        var (error, result) = await ValidateWrite(client.Id, data, ct);
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
            Year = data.Year,
            Name = data.Name.Trim(),
            Objective = result!.Objective,
            AssetType = Clean(data.AssetType),
            OsCode = Clean(data.OsCode),
            UtmUrl = Clean(data.UtmUrl),
            ArtworkUrl = Clean(data.ArtworkUrl),
            Comments = Clean(data.Comments),
            Notes = Clean(data.Notes),
            LiveMonths = result.LiveMonths,
            StartDate = result.StartDate,
            EndDate = result.EndDate,
            EdmSubcategory = result.EdmSubcategory,
            EducationSubcategory = result.EducationSubcategory,
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

        // Seed from the client's baselines; eDM/education use their start year, others use the reporting year.
        var targetYear = result.StartDate?.Year ?? data.Year;
        var seededKpis = await _context.ClientPublisherBaselines
            .Where(b => b.ClientId == client.Id
                     && b.PublisherId == data.PublisherId
                     && b.TemplateId == data.TemplateId
                     && b.Year == targetYear)
            .Select(b => new PlacementKpi
            {
                Id = Guid.NewGuid(),
                PlacementId = placement.Id,
                MetricKey = b.MetricKey,
                TargetValue = b.Value,
            })
            .ToListAsync(ct);
        _context.PlacementKpis.AddRange(seededKpis);

        await _context.SaveChangesAsync(ct);

        var dto = await LoadDetail(placement.Id, client.Id, ct);
        var resp = req.CreateResponse(HttpStatusCode.Created);
        await resp.WriteAsJsonAsync(dto);
        return resp;
    }

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

        var (error, result) = await ValidateWrite(client.Id, data, ct);
        if (error is not null) return await BadRequest(req, error);

        placement.BrandId = data.BrandId;
        placement.AudienceId = data.AudienceId;
        placement.PublisherId = data.PublisherId;
        placement.TemplateId = data.TemplateId;
        placement.Year = data.Year;
        placement.Name = data.Name.Trim();
        placement.Objective = result!.Objective;
        placement.AssetType = Clean(data.AssetType);
        placement.OsCode = Clean(data.OsCode);
        placement.UtmUrl = Clean(data.UtmUrl);
        placement.ArtworkUrl = Clean(data.ArtworkUrl);
        placement.Comments = Clean(data.Comments);
        placement.Notes = Clean(data.Notes);
        placement.LiveMonths = result.LiveMonths;
        placement.StartDate = result.StartDate;
        placement.EndDate = result.EndDate;
        placement.EdmSubcategory = result.EdmSubcategory;
        placement.EducationSubcategory = result.EducationSubcategory;
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

        // Artwork delete is best-effort — R2 failure must not block the DB row removal.
        if (!string.IsNullOrWhiteSpace(placement.ArtworkUrl))
        {
            try { await _r2.DeleteAsync(placement.ArtworkUrl, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete artwork {Key} for placement {Id}", placement.ArtworkUrl, id); }
        }

        _context.Placements.Remove(placement);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

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
        // Education actuals can span multiple years; all other templates are pinned to their reporting year.
        bool isRange = placement.StartDate is not null && placement.EndDate is not null;
        int rangeFromOrd = isRange ? PeriodWindow.Ord(placement.StartDate!.Value) : 0;
        int rangeToOrd = isRange ? PeriodWindow.Ord(placement.EndDate!.Value) : 0;

        foreach (var row in data.Actuals)
        {
            if (row.Month is < 1 or > 12) return await BadRequest(req, $"Invalid month: {row.Month}");
            var key = (row.MetricKey ?? "").Trim().ToLowerInvariant();
            if (key.Length == 0) return await BadRequest(req, "Each actual requires a metric key");
            if (!validKeys.Contains(key)) return await BadRequest(req, $"Metric '{key}' is not part of this placement's template");

            if (isRange)
            {
                var o = PeriodWindow.Ord(row.Year, row.Month);
                if (o < rangeFromOrd || o > rangeToOrd)
                    return await BadRequest(req, $"{row.Year}-{row.Month:D2} is outside this placement's date range");
            }
            else if (row.Year != placement.Year)
            {
                return await BadRequest(req, $"Actuals must be in the placement's reporting year ({placement.Year})");
            }
        }

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

    // ── Duplicate a placement ────────────────────────────────────────────────

    [Function("ManageDuplicatePlacement")]
    public async Task<HttpResponseData> DuplicatePlacement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/placements/{placementId}/duplicate")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string placementId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(placementId, out var id)) return await BadRequest(req, "Invalid placement id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var source = await _context.Placements
            .AsNoTracking()
            .Include(p => p.Template)
            .Include(p => p.Kpis)
            .FirstOrDefaultAsync(p => p.Id == id && p.Brand.ClientId == client.Id, ct);
        if (source is null) return await NotFound(req);

        var now = DateTime.UtcNow;
        var userId = req.GetUserId(context);
        // Duplicated sends share the source's group (the source anchors the group).
        // eDM clones clear the send date so the editor sets the new one; actuals
        // are never copied.
        var clone = new Placement
        {
            Id = Guid.NewGuid(),
            BrandId = source.BrandId,
            AudienceId = source.AudienceId,
            PublisherId = source.PublisherId,
            TemplateId = source.TemplateId,
            Year = source.Year,
            Name = source.Name,
            Objective = source.Objective,
            AssetType = source.AssetType,
            OsCode = source.OsCode,
            UtmUrl = source.UtmUrl,
            ArtworkUrl = source.ArtworkUrl,        // same creative
            Comments = source.Comments,
            Notes = source.Notes,
            LiveMonths = source.LiveMonths,
            StartDate = source.Template.Code == MetricTemplateCode.Edm ? null : source.StartDate,
            EndDate = source.EndDate,
            EdmSubcategory = source.EdmSubcategory,
            EducationSubcategory = source.EducationSubcategory,
            GroupId = source.GroupId ?? source.Id,
            MediaCost = source.MediaCost,
            PlannedMediaCost = source.PlannedMediaCost,
            CpdInvestmentCost = source.CpdInvestmentCost,
            IsBonus = source.IsBonus,
            IsCpdPackage = source.IsCpdPackage,
            Circulation = source.Circulation,
            PlacementsCount = source.PlacementsCount,
            TargetCourseId = source.TargetCourseId,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
            UpdatedBy = userId,
        };
        _context.Placements.Add(clone);

        foreach (var k in source.Kpis)
        {
            _context.PlacementKpis.Add(new PlacementKpi
            {
                Id = Guid.NewGuid(),
                PlacementId = clone.Id,
                MetricKey = k.MetricKey,
                TargetValue = k.TargetValue,
            });
        }

        await _context.SaveChangesAsync(ct);

        var dto = await LoadDetail(clone.Id, client.Id, ct);
        var resp = req.CreateResponse(HttpStatusCode.Created);
        await resp.WriteAsJsonAsync(dto);
        return resp;
    }

    // ── Clone a year forward ─────────────────────────────────────────────────

    [Function("ManageClonePlacementYear")]
    public async Task<HttpResponseData> ClonePlacementYear(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/placements/clone-year")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var data = await ReadJson<ClonePlacementYearRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");
        if (data.FromYear is < 2000 or > 2100 || data.ToYear is < 2000 or > 2100)
            return await BadRequest(req, "Invalid year");
        if (data.FromYear == data.ToYear)
            return await BadRequest(req, "Source and target year must differ");

        var source = await _context.Placements
            .AsNoTracking()
            .Include(p => p.Kpis)
            .Where(p => p.Brand.ClientId == client.Id && p.Year == data.FromYear)
            .ToListAsync(ct);

        // Existing twins in the target year (match on brand+audience+publisher+name)
        // so re-running the clone is idempotent.
        var existing = await _context.Placements
            .AsNoTracking()
            .Where(p => p.Brand.ClientId == client.Id && p.Year == data.ToYear)
            .Select(p => new { p.BrandId, p.AudienceId, p.PublisherId, p.Name })
            .ToListAsync(ct);
        var taken = existing
            .Select(e => $"{e.BrandId}|{e.AudienceId}|{e.PublisherId}|{e.Name}")
            .ToHashSet();

        var now = DateTime.UtcNow;
        var userId = req.GetUserId(context);
        var yearDelta = data.ToYear - data.FromYear;
        int created = 0, skipped = 0;

        foreach (var p in source)
        {
            // A multi-year education buy already shows in the target year via its
            // range — cloning it would double-count.
            if (p.EndDate is { } end && end.Year > data.FromYear)
            {
                skipped++;
                continue;
            }

            if (!taken.Add($"{p.BrandId}|{p.AudienceId}|{p.PublisherId}|{p.Name}"))
            {
                skipped++;
                continue;
            }

            var clone = new Placement
            {
                Id = Guid.NewGuid(),
                BrandId = p.BrandId,
                AudienceId = p.AudienceId,
                PublisherId = p.PublisherId,
                TemplateId = p.TemplateId,
                Year = data.ToYear,
                Name = p.Name,
                Objective = p.Objective,
                AssetType = p.AssetType,
                OsCode = p.OsCode,
                UtmUrl = p.UtmUrl,
                ArtworkUrl = p.ArtworkUrl,             // same creative
                Comments = p.Comments,
                Notes = p.Notes,
                LiveMonths = p.LiveMonths,
                StartDate = p.StartDate?.AddYears(yearDelta),  // shift send/range into the new year
                EndDate = p.EndDate?.AddYears(yearDelta),
                EdmSubcategory = p.EdmSubcategory,
                EducationSubcategory = p.EducationSubcategory,
                GroupId = null,                         // a new year's sends are a new group
                MediaCost = 0m,                         // costs reset for the new year
                PlannedMediaCost = p.PlannedMediaCost,  // carry the budget estimate
                CpdInvestmentCost = null,
                IsBonus = p.IsBonus,
                IsCpdPackage = p.IsCpdPackage,
                Circulation = p.Circulation,
                PlacementsCount = p.PlacementsCount,
                TargetCourseId = p.TargetCourseId,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = userId,
                UpdatedBy = userId,
            };
            _context.Placements.Add(clone);

            // Carry KPI targets forward; no actuals.
            foreach (var k in p.Kpis)
            {
                _context.PlacementKpis.Add(new PlacementKpi
                {
                    Id = Guid.NewGuid(),
                    PlacementId = clone.Id,
                    MetricKey = k.MetricKey,
                    TargetValue = k.TargetValue,
                });
            }
            created++;
        }

        await _context.SaveChangesAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new ClonePlacementYearResponse(created, skipped));
        return resp;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolved, normalised placement fields shared by create and update. Date
    /// shape and sub-categories are reconciled to the template: eDM carries a send
    /// date + sub-category (no LiveMonths); Education carries a date range + a
    /// sub-category; everything else carries LiveMonths only.
    /// </summary>
    private sealed record ValidatedPlacement(
        PlacementObjective Objective,
        int[] LiveMonths,
        DateOnly? StartDate,
        DateOnly? EndDate,
        EdmSubcategory? EdmSubcategory,
        EducationSubcategory? EducationSubcategory);

    /// <summary>
    /// Validates the foreign keys and scalar fields shared by create and update,
    /// applying per-template date/sub-category rules. Returns an error message (or
    /// null) plus the resolved fields.
    /// </summary>
    private async Task<(string? error, ValidatedPlacement? result)> ValidateWrite(
        Guid clientId, PlacementWriteRequest data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.Name))
            return ("Name is required", null);

        if (data.Year is < 2000 or > 2100)
            return ($"Invalid year: {data.Year}", null);

        if (!Enum.TryParse<PlacementObjective>(data.Objective, ignoreCase: true, out var objective))
            return ($"Invalid objective '{data.Objective}'", null);

        if (data.LiveMonths.Any(m => m is < 1 or > 12))
            return ("Live months must be between 1 and 12", null);
        var liveMonths = data.LiveMonths.Distinct().OrderBy(m => m).ToArray();

        var brandOk = await _context.Brands.AnyAsync(b => b.Id == data.BrandId && b.ClientId == clientId, ct);
        if (!brandOk) return ("Unknown brand for this client", null);

        var audienceOk = await _context.Audiences.AnyAsync(a => a.Id == data.AudienceId && a.ClientId == clientId, ct);
        if (!audienceOk) return ("Unknown audience for this client", null);

        var publisherOk = await _context.Publishers.AnyAsync(p => p.Id == data.PublisherId, ct);
        if (!publisherOk) return ("Unknown publisher", null);

        var template = await _context.MetricTemplates.FirstOrDefaultAsync(t => t.Id == data.TemplateId, ct);
        if (template is null) return ("Unknown template", null);

        var publisherSupportsTemplate = await _context.PublisherTemplates
            .AnyAsync(pt => pt.PublisherId == data.PublisherId && pt.TemplateId == data.TemplateId, ct);
        if (!publisherSupportsTemplate)
            return ("This publisher does not offer the selected template", null);

        if (data.TargetCourseId is { } courseId)
        {
            var courseOk = await _context.EducationCourses
                .AnyAsync(c => c.Id == courseId && c.Brand.ClientId == clientId, ct);
            if (!courseOk) return ("Unknown target course for this client", null);
        }

        // Per-template date + sub-category rules. Fields that don't apply to the
        // template are normalised away rather than rejected.
        DateOnly? startDate = null, endDate = null;
        EdmSubcategory? edmSub = null;
        EducationSubcategory? eduSub = null;
        switch (template.Code)
        {
            case MetricTemplateCode.Edm:
                if (data.StartDate is null) return ("An eDM placement needs a send date", null);
                if (!PlacementEnumNames.TryParseEdm(data.EdmSubcategory, out var e))
                    return ("Select an eDM type: solus, sponsored content or banner", null);
                startDate = data.StartDate;
                edmSub = e;
                liveMonths = Array.Empty<int>();
                break;

            case MetricTemplateCode.Education:
                if (data.StartDate is null || data.EndDate is null)
                    return ("An education placement needs a start and end date", null);
                if (data.EndDate < data.StartDate)
                    return ("End date must be on or after the start date", null);
                if (!PlacementEnumNames.TryParseEducation(data.EducationSubcategory, out var ed))
                    return ("Select an education type", null);
                startDate = data.StartDate;
                endDate = data.EndDate;
                eduSub = ed;
                liveMonths = Array.Empty<int>();
                break;
        }

        return (null, new ValidatedPlacement(objective, liveMonths, startDate, endDate, edmSub, eduSub));
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
            p.Year,
            p.Name,
            p.Objective.ToString().ToLower(),
            p.AssetType,
            p.OsCode,
            p.UtmUrl,
            p.ArtworkUrl,
            artworkViewUrl,
            p.Comments,
            p.Notes,
            p.LiveMonths,
            p.StartDate?.ToString("yyyy-MM-dd"),
            p.EndDate?.ToString("yyyy-MM-dd"),
            p.EdmSubcategory.HasValue ? PlacementEnumNames.ToName(p.EdmSubcategory.Value) : null,
            p.EducationSubcategory.HasValue ? PlacementEnumNames.ToName(p.EducationSubcategory.Value) : null,
            p.GroupId,
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
