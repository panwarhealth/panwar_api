using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

/// <summary>
/// Education dashboard CRUD for the employee portal. Staff build named pages
/// (e.g. "Pharmacy Education"), add any number of completion bar charts to a
/// page, add series (one coloured bar per module) with monthly completion data,
/// and annotate specific bars with floating notes. Everything is tenancy-scoped
/// to the client through the page.
/// </summary>
public class ManageEducationFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<ManageEducationFunction> _logger;
    private readonly AppDbContext _context;

    public ManageEducationFunction(ILogger<ManageEducationFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    [Function("ManageListEducationPages")]
    public async Task<HttpResponseData> ListPages(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/education")] HttpRequestData req,
        FunctionContext context, string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var pages = await _context.EducationPages
            .AsNoTracking()
            .Where(p => p.ClientId == client.Id)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            // Admin picker doesn't need the overview aggregates - pass 0s
            // (optional defaults aren't allowed inside an EF expression tree).
            .Select(p => new EducationPageSummaryDto(p.Id, p.Name, p.Slug, p.SortOrder, p.Charts.Count, 0, 0, 0))
            .ToListAsync(ct);

        return await Ok(req, new EducationPagesResponse(pages));
    }

    [Function("ManageGetEducationPage")]
    public async Task<HttpResponseData> GetPage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/education/{pageId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string pageId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(pageId, out var id)) return await BadRequest(req, "Invalid page id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var tree = await LoadTree(id, client.Id, ct);
        if (tree is null) return await NotFound(req);
        return await Ok(req, tree);
    }

    [Function("ManageCreateEducationPage")]
    public async Task<HttpResponseData> CreatePage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/education")] HttpRequestData req,
        FunctionContext context, string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var data = await ReadJson<EducationPageWriteRequest>(req);
        if (data is null || string.IsNullOrWhiteSpace(data.Name)) return await BadRequest(req, "Page name required");

        var slug = await UniquePageSlug(client.Id, data.Slug, data.Name, ct);
        var maxOrder = await _context.EducationPages.Where(p => p.ClientId == client.Id)
            .Select(p => (int?)p.SortOrder).MaxAsync(ct) ?? -1;

        var page = new EducationPage
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            Name = data.Name.Trim(),
            Slug = slug,
            SortOrder = data.SortOrder ?? maxOrder + 1,
        };
        _context.EducationPages.Add(page);
        await _context.SaveChangesAsync(ct);

        var tree = await LoadTree(page.Id, client.Id, ct);
        return await Ok(req, tree!, HttpStatusCode.Created);
    }

    [Function("ManageUpdateEducationPage")]
    public async Task<HttpResponseData> UpdatePage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/education/{pageId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string pageId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(pageId, out var id)) return await BadRequest(req, "Invalid page id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var page = await _context.EducationPages.FirstOrDefaultAsync(p => p.Id == id && p.ClientId == client.Id, ct);
        if (page is null) return await NotFound(req);

        var data = await ReadJson<EducationPageWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        if (!string.IsNullOrWhiteSpace(data.Name)) page.Name = data.Name.Trim();
        if (data.Slug is not null) page.Slug = await UniquePageSlug(client.Id, data.Slug, page.Name, ct, page.Id);
        if (data.SortOrder.HasValue) page.SortOrder = data.SortOrder.Value;
        page.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return await Ok(req, (await LoadTree(page.Id, client.Id, ct))!);
    }

    [Function("ManageDeleteEducationPage")]
    public async Task<HttpResponseData> DeletePage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/education/{pageId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string pageId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(pageId, out var id)) return await BadRequest(req, "Invalid page id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var page = await _context.EducationPages.FirstOrDefaultAsync(p => p.Id == id && p.ClientId == client.Id, ct);
        if (page is null) return await NotFound(req);
        _context.EducationPages.Remove(page);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── Charts ───────────────────────────────────────────────────────────────

    [Function("ManageCreateEducationChart")]
    public async Task<HttpResponseData> CreateChart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/education/{pageId}/charts")] HttpRequestData req,
        FunctionContext context, string clientSlug, string pageId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(pageId, out var pid)) return await BadRequest(req, "Invalid page id");
        var (client, page, err) = await ResolvePage(req, context, clientSlug, pid);
        if (err is not null) return err;
        var ct = context.CancellationToken;

        var data = await ReadJson<EducationChartWriteRequest>(req);
        if (data is null || string.IsNullOrWhiteSpace(data.Title)) return await BadRequest(req, "Chart title required");

        var maxOrder = await _context.EducationCharts.Where(c => c.EducationPageId == pid)
            .Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? -1;
        var chart = new EducationChart
        {
            Id = Guid.NewGuid(),
            EducationPageId = pid,
            Title = data.Title.Trim(),
            Subtitle = Clean(data.Subtitle),
            SortOrder = data.SortOrder ?? maxOrder + 1,
        };
        _context.EducationCharts.Add(chart);
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(page!.Id, client!.Id, ct))!, HttpStatusCode.Created);
    }

    [Function("ManageUpdateEducationChart")]
    public async Task<HttpResponseData> UpdateChart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/education/charts/{chartId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string chartId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(chartId, out var cid)) return await BadRequest(req, "Invalid chart id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var chart = await _context.EducationCharts
            .FirstOrDefaultAsync(c => c.Id == cid && c.Page.ClientId == client.Id, ct);
        if (chart is null) return await NotFound(req);

        var data = await ReadJson<EducationChartWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");
        if (!string.IsNullOrWhiteSpace(data.Title)) chart.Title = data.Title.Trim();
        if (data.Subtitle is not null) chart.Subtitle = Clean(data.Subtitle);
        if (data.SortOrder.HasValue) chart.SortOrder = data.SortOrder.Value;
        chart.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(chart.EducationPageId, client.Id, ct))!);
    }

    [Function("ManageDeleteEducationChart")]
    public async Task<HttpResponseData> DeleteChart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/education/charts/{chartId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string chartId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(chartId, out var cid)) return await BadRequest(req, "Invalid chart id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var chart = await _context.EducationCharts
            .FirstOrDefaultAsync(c => c.Id == cid && c.Page.ClientId == client.Id, ct);
        if (chart is null) return await NotFound(req);
        _context.EducationCharts.Remove(chart);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── Series ───────────────────────────────────────────────────────────────

    [Function("ManageCreateEducationSeries")]
    public async Task<HttpResponseData> CreateSeries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/education/charts/{chartId}/series")] HttpRequestData req,
        FunctionContext context, string clientSlug, string chartId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(chartId, out var cid)) return await BadRequest(req, "Invalid chart id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var chart = await _context.EducationCharts
            .FirstOrDefaultAsync(c => c.Id == cid && c.Page.ClientId == client.Id, ct);
        if (chart is null) return await NotFound(req);

        var data = await ReadJson<EducationSeriesWriteRequest>(req);
        if (data is null || string.IsNullOrWhiteSpace(data.Label)) return await BadRequest(req, "Series label required");

        var maxOrder = await _context.EducationSeries.Where(s => s.EducationChartId == cid)
            .Select(s => (int?)s.SortOrder).MaxAsync(ct) ?? -1;
        var series = new EducationSeries
        {
            Id = Guid.NewGuid(),
            EducationChartId = cid,
            Label = data.Label.Trim(),
            Color = Clean(data.Color),
            SortOrder = data.SortOrder ?? maxOrder + 1,
        };
        _context.EducationSeries.Add(series);
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(chart.EducationPageId, client.Id, ct))!, HttpStatusCode.Created);
    }

    [Function("ManageUpdateEducationSeries")]
    public async Task<HttpResponseData> UpdateSeries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/education/series/{seriesId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string seriesId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(seriesId, out var sid)) return await BadRequest(req, "Invalid series id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var series = await _context.EducationSeries
            .FirstOrDefaultAsync(s => s.Id == sid && s.Chart.Page.ClientId == client.Id, ct);
        if (series is null) return await NotFound(req);

        var data = await ReadJson<EducationSeriesWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");
        if (!string.IsNullOrWhiteSpace(data.Label)) series.Label = data.Label.Trim();
        if (data.Color is not null) series.Color = Clean(data.Color);
        if (data.SortOrder.HasValue) series.SortOrder = data.SortOrder.Value;
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(series.EducationChartId, client.Id, ct, byChart: true))!);
    }

    [Function("ManageDeleteEducationSeries")]
    public async Task<HttpResponseData> DeleteSeries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/education/series/{seriesId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string seriesId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(seriesId, out var sid)) return await BadRequest(req, "Invalid series id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var series = await _context.EducationSeries
            .FirstOrDefaultAsync(s => s.Id == sid && s.Chart.Page.ClientId == client.Id, ct);
        if (series is null) return await NotFound(req);
        _context.EducationSeries.Remove(series);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("ManageSetEducationSeriesData")]
    public async Task<HttpResponseData> SetSeriesData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/clients/{clientSlug}/education/series/{seriesId}/data")] HttpRequestData req,
        FunctionContext context, string clientSlug, string seriesId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(seriesId, out var sid)) return await BadRequest(req, "Invalid series id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var series = await _context.EducationSeries
            .Include(s => s.DataPoints)
            .FirstOrDefaultAsync(s => s.Id == sid && s.Chart.Page.ClientId == client.Id, ct);
        if (series is null) return await NotFound(req);

        var data = await ReadJson<EducationSeriesDataRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        // Collapse to one value per (year, month); last write wins.
        var byMonth = new Dictionary<(int, int), decimal>();
        foreach (var p in data.Points)
        {
            if (p.Month is < 1 or > 12) return await BadRequest(req, $"Invalid month {p.Month}");
            byMonth[(p.Year, p.Month)] = p.Value;
        }

        // Full replace of this series' points.
        _context.EducationDataPoints.RemoveRange(series.DataPoints);
        _context.EducationDataPoints.AddRange(byMonth.Select(kv => new EducationDataPoint
        {
            Id = Guid.NewGuid(),
            EducationSeriesId = sid,
            Year = kv.Key.Item1,
            Month = kv.Key.Item2,
            Value = kv.Value,
        }));
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(series.EducationChartId, client.Id, ct, byChart: true))!);
    }

    // ── Annotations ──────────────────────────────────────────────────────────

    [Function("ManageCreateEducationAnnotation")]
    public async Task<HttpResponseData> CreateAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/education/charts/{chartId}/annotations")] HttpRequestData req,
        FunctionContext context, string clientSlug, string chartId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(chartId, out var cid)) return await BadRequest(req, "Invalid chart id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var chart = await _context.EducationCharts
            .FirstOrDefaultAsync(c => c.Id == cid && c.Page.ClientId == client.Id, ct);
        if (chart is null) return await NotFound(req);

        var data = await ReadJson<EducationAnnotationWriteRequest>(req);
        if (data is null || string.IsNullOrWhiteSpace(data.Text)) return await BadRequest(req, "Annotation text required");
        if (data.Month is < 1 or > 12) return await BadRequest(req, "Invalid month");

        // The annotated bar's series must belong to this chart.
        var seriesOk = await _context.EducationSeries
            .AnyAsync(s => s.Id == data.SeriesId && s.EducationChartId == cid, ct);
        if (!seriesOk) return await BadRequest(req, "Series does not belong to this chart");

        var annotation = new EducationAnnotation
        {
            Id = Guid.NewGuid(),
            EducationChartId = cid,
            EducationSeriesId = data.SeriesId,
            Year = data.Year,
            Month = data.Month,
            Text = data.Text.Trim(),
            CreatedByUserId = req.GetUserId(context),
        };
        _context.EducationAnnotations.Add(annotation);
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(chart.EducationPageId, client.Id, ct))!, HttpStatusCode.Created);
    }

    [Function("ManageUpdateEducationAnnotation")]
    public async Task<HttpResponseData> UpdateAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/education/annotations/{annotationId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string annotationId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(annotationId, out var aid)) return await BadRequest(req, "Invalid annotation id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var annotation = await _context.EducationAnnotations
            .FirstOrDefaultAsync(a => a.Id == aid && a.Chart.Page.ClientId == client.Id, ct);
        if (annotation is null) return await NotFound(req);

        var data = await ReadJson<EducationAnnotationWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");
        if (!string.IsNullOrWhiteSpace(data.Text)) annotation.Text = data.Text.Trim();
        if (data.Month is >= 1 and <= 12) annotation.Month = data.Month;
        if (data.Year > 0) annotation.Year = data.Year;
        if (data.SeriesId != Guid.Empty)
        {
            var seriesOk = await _context.EducationSeries
                .AnyAsync(s => s.Id == data.SeriesId && s.EducationChartId == annotation.EducationChartId, ct);
            if (!seriesOk) return await BadRequest(req, "Series does not belong to this chart");
            annotation.EducationSeriesId = data.SeriesId;
        }
        annotation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(annotation.EducationChartId, client.Id, ct, byChart: true))!);
    }

    [Function("ManageDeleteEducationAnnotation")]
    public async Task<HttpResponseData> DeleteAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/education/annotations/{annotationId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string annotationId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(annotationId, out var aid)) return await BadRequest(req, "Invalid annotation id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var annotation = await _context.EducationAnnotations
            .FirstOrDefaultAsync(a => a.Id == aid && a.Chart.Page.ClientId == client.Id, ct);
        if (annotation is null) return await NotFound(req);
        _context.EducationAnnotations.Remove(annotation);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── Assets (the page's detail table) ─────────────────────────────────────

    [Function("ManageCreateEducationAsset")]
    public async Task<HttpResponseData> CreateAsset(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/education/{pageId}/assets")] HttpRequestData req,
        FunctionContext context, string clientSlug, string pageId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(pageId, out var pid)) return await BadRequest(req, "Invalid page id");
        var (client, page, err) = await ResolvePage(req, context, clientSlug, pid);
        if (err is not null) return err;
        var ct = context.CancellationToken;

        var data = await ReadJson<EducationAssetWriteRequest>(req);
        if (data is null || string.IsNullOrWhiteSpace(data.Title)) return await BadRequest(req, "Asset title required");
        if (string.IsNullOrWhiteSpace(data.GroupLabel)) return await BadRequest(req, "Group label required");

        var maxOrder = await _context.EducationAssets.Where(a => a.EducationPageId == pid)
            .Select(a => (int?)a.SortOrder).MaxAsync(ct) ?? -1;
        var asset = new EducationAsset
        {
            Id = Guid.NewGuid(),
            EducationPageId = pid,
            GroupLabel = data.GroupLabel.Trim(),
            Brand = Clean(data.Brand),
            Type = Clean(data.Type),
            Title = data.Title.Trim(),
            Author = Clean(data.Author),
            Expiry = data.Expiry,
            SortOrder = data.SortOrder ?? maxOrder + 1,
        };
        _context.EducationAssets.Add(asset);
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(page!.Id, client!.Id, ct))!, HttpStatusCode.Created);
    }

    [Function("ManageUpdateEducationAsset")]
    public async Task<HttpResponseData> UpdateAsset(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/education/assets/{assetId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string assetId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(assetId, out var aid)) return await BadRequest(req, "Invalid asset id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var asset = await _context.EducationAssets
            .FirstOrDefaultAsync(a => a.Id == aid && a.Page.ClientId == client.Id, ct);
        if (asset is null) return await NotFound(req);

        var data = await ReadJson<EducationAssetWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");
        if (!string.IsNullOrWhiteSpace(data.GroupLabel)) asset.GroupLabel = data.GroupLabel.Trim();
        if (!string.IsNullOrWhiteSpace(data.Title)) asset.Title = data.Title.Trim();
        if (data.Brand is not null) asset.Brand = Clean(data.Brand);
        if (data.Type is not null) asset.Type = Clean(data.Type);
        if (data.Author is not null) asset.Author = Clean(data.Author);
        if (data.Expiry.HasValue) asset.Expiry = data.Expiry;
        if (data.ClearExpiry == true) asset.Expiry = null;
        if (data.SortOrder.HasValue) asset.SortOrder = data.SortOrder.Value;
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(asset.EducationPageId, client.Id, ct))!);
    }

    [Function("ManageDeleteEducationAsset")]
    public async Task<HttpResponseData> DeleteAsset(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/education/assets/{assetId}")] HttpRequestData req,
        FunctionContext context, string clientSlug, string assetId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(assetId, out var aid)) return await BadRequest(req, "Invalid asset id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var asset = await _context.EducationAssets
            .FirstOrDefaultAsync(a => a.Id == aid && a.Page.ClientId == client.Id, ct);
        if (asset is null) return await NotFound(req);
        _context.EducationAssets.Remove(asset);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("ManageSetEducationAssetValues")]
    public async Task<HttpResponseData> SetAssetValues(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/clients/{clientSlug}/education/assets/{assetId}/values")] HttpRequestData req,
        FunctionContext context, string clientSlug, string assetId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(assetId, out var aid)) return await BadRequest(req, "Invalid asset id");
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var asset = await _context.EducationAssets
            .Include(a => a.Values)
            .FirstOrDefaultAsync(a => a.Id == aid && a.Page.ClientId == client.Id, ct);
        if (asset is null) return await NotFound(req);

        var data = await ReadJson<EducationAssetValuesRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        // Collapse to one value per (status, year, month); last write wins.
        var byKey = new Dictionary<(string, int, int), decimal>();
        foreach (var v in data.Values)
        {
            if (string.IsNullOrWhiteSpace(v.Status)) return await BadRequest(req, "Value status required");
            if (v.Month is < 1 or > 12) return await BadRequest(req, $"Invalid month {v.Month}");
            byKey[(v.Status.Trim(), v.Year, v.Month)] = v.Value;
        }

        // Full replace of this asset's values.
        _context.EducationAssetValues.RemoveRange(asset.Values);
        _context.EducationAssetValues.AddRange(byKey.Select(kv => new EducationAssetValue
        {
            Id = Guid.NewGuid(),
            EducationAssetId = aid,
            Status = kv.Key.Item1,
            Year = kv.Key.Item2,
            Month = kv.Key.Item3,
            Value = kv.Value,
        }));
        await _context.SaveChangesAsync(ct);
        return await Ok(req, (await LoadTree(asset.EducationPageId, client.Id, ct))!);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Loads + maps a page tree (unwindowed). When byChart is true, pageOrChartId is a chart id.</summary>
    private async Task<EducationPageResponse?> LoadTree(Guid pageOrChartId, Guid clientId, CancellationToken ct, bool byChart = false)
    {
        var pageId = pageOrChartId;
        if (byChart)
        {
            var chart = await _context.EducationCharts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == pageOrChartId, ct);
            if (chart is null) return null;
            pageId = chart.EducationPageId;
        }

        // Three sibling collection includes - split queries avoid the
        // cartesian row explosion a single join query produces.
        var page = await _context.EducationPages
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Charts).ThenInclude(c => c.Series).ThenInclude(s => s.DataPoints)
            .Include(p => p.Charts).ThenInclude(c => c.Annotations)
            .Include(p => p.Assets).ThenInclude(a => a.Values)
            .FirstOrDefaultAsync(p => p.Id == pageId && p.ClientId == clientId, ct);
        if (page is null) return null;
        return EducationMapper.Build(page, null, null);
    }

    private async Task<(Client? client, EducationPage? page, HttpResponseData? error)> ResolvePage(
        HttpRequestData req, FunctionContext context, string clientSlug, Guid pageId)
    {
        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return (null, null, await NotFound(req));
        var page = await _context.EducationPages.FirstOrDefaultAsync(p => p.Id == pageId && p.ClientId == client.Id, ct);
        if (page is null) return (null, null, await NotFound(req));
        return (client, page, null);
    }

    private async Task<string> UniquePageSlug(Guid clientId, string? requested, string fallbackName, CancellationToken ct, Guid? excludeId = null)
    {
        var baseSlug = Slugify(!string.IsNullOrWhiteSpace(requested) ? requested : fallbackName);
        if (baseSlug.Length == 0) baseSlug = "page";
        var slug = baseSlug;
        var n = 2;
        while (await _context.EducationPages.AnyAsync(
            p => p.ClientId == clientId && p.Slug == slug && (excludeId == null || p.Id != excludeId), ct))
        {
            slug = $"{baseSlug}-{n++}";
        }
        return slug;
    }

    private static string Slugify(string input)
    {
        var sb = new StringBuilder(input.Length);
        var prevDash = false;
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); prevDash = false; }
            else if (!prevDash) { sb.Append('-'); prevDash = true; }
        }
        return sb.ToString().Trim('-');
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool CanManage(HttpRequestData req, FunctionContext context)
        => req.HasRole(context, "panwar-admin") || req.HasRole(context, "dashboard-editor");

    private static async Task<T?> ReadJson<T>(HttpRequestData req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static async Task<HttpResponseData> Ok(HttpRequestData req, object payload, HttpStatusCode status = HttpStatusCode.OK)
    {
        var resp = req.CreateResponse(status);
        await resp.WriteAsJsonAsync(payload);
        return resp;
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
