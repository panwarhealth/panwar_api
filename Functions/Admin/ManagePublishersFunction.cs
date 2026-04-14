using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// Publishers are a shared registry across all clients (real-world media
/// outlets). Each publisher supports one or more metric templates, which
/// determines what fields placements on that publisher capture.
/// </summary>
public class ManagePublishersFunction
{
    private static readonly Regex SlugPattern = new("^[a-z0-9](?:[a-z0-9-]{0,98}[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<ManagePublishersFunction> _logger;
    private readonly AppDbContext _context;

    public ManagePublishersFunction(ILogger<ManagePublishersFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Function("ManageListPublishers")]
    public async Task<HttpResponseData> ListPublishers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/publishers")] HttpRequestData req,
        FunctionContext context)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var publishers = await _context.Publishers
            .AsNoTracking()
            .Include(p => p.PublisherTemplates).ThenInclude(pt => pt.Template)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var dtos = publishers.Select(p => new PublisherDto(
            p.Id, p.Name, p.Slug, p.Website,
            p.PublisherTemplates.Select(pt => new PublisherTemplateDto(
                pt.Template.Id,
                pt.Template.Code.ToString().ToLowerInvariant(),
                pt.Template.Name
            )).ToList())).ToList();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { publishers = dtos });
        return resp;
    }

    [Function("ManageListTemplates")]
    public async Task<HttpResponseData> ListTemplates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/templates")] HttpRequestData req,
        FunctionContext context)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var templates = await _context.MetricTemplates
            .AsNoTracking()
            .Include(t => t.Fields)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        var dtos = templates.Select(t => new MetricTemplateDto(
            t.Id, t.Code.ToString().ToLowerInvariant(), t.Name,
            t.Fields.Select(f => new MetricFieldDto(f.Key, f.Label, f.Unit)).ToList()
        )).ToList();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { templates = dtos });
        return resp;
    }

    [Function("ManageCreatePublisher")]
    public async Task<HttpResponseData> CreatePublisher(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/publishers")] HttpRequestData req,
        FunctionContext context)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var data = await ReadJson<PublisherWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var name = (data.Name ?? "").Trim();
        var slug = (data.Slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return await BadRequest(req, "Name is required");
        if (!SlugPattern.IsMatch(slug)) return await BadRequest(req, "Invalid slug");

        if (await _context.Publishers.AnyAsync(p => p.Slug == slug, ct))
            return await BadRequest(req, "A publisher with that slug already exists");

        var publisher = new Publisher
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            Website = string.IsNullOrWhiteSpace(data.Website) ? null : data.Website.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.Publishers.Add(publisher);

        foreach (var templateId in data.TemplateIds.Distinct())
        {
            _context.Add(new PublisherTemplate
            {
                PublisherId = publisher.Id,
                TemplateId = templateId,
            });
        }

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Created publisher {Slug}", slug);

        return await LoadAndReturn(req, publisher.Id, HttpStatusCode.Created, ct);
    }

    [Function("ManageUpdatePublisher")]
    public async Task<HttpResponseData> UpdatePublisher(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/publishers/{publisherId}")] HttpRequestData req,
        FunctionContext context,
        string publisherId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(publisherId, out var id)) return await BadRequest(req, "Invalid publisher id");

        var ct = context.CancellationToken;
        var publisher = await _context.Publishers
            .Include(p => p.PublisherTemplates)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (publisher is null) return await NotFound(req);

        var data = await ReadJson<PublisherWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var name = (data.Name ?? "").Trim();
        var slug = (data.Slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return await BadRequest(req, "Name is required");
        if (!SlugPattern.IsMatch(slug)) return await BadRequest(req, "Invalid slug");

        if (slug != publisher.Slug &&
            await _context.Publishers.AnyAsync(p => p.Slug == slug, ct))
            return await BadRequest(req, "A publisher with that slug already exists");

        publisher.Name = name;
        publisher.Slug = slug;
        publisher.Website = string.IsNullOrWhiteSpace(data.Website) ? null : data.Website.Trim();
        publisher.UpdatedAt = DateTime.UtcNow;

        // Replace template assignments
        var desiredTemplates = data.TemplateIds.Distinct().ToHashSet();
        var existing = publisher.PublisherTemplates.ToList();
        foreach (var pt in existing.Where(pt => !desiredTemplates.Contains(pt.TemplateId)))
            _context.Remove(pt);
        foreach (var tid in desiredTemplates.Where(tid => existing.All(pt => pt.TemplateId != tid)))
            _context.Add(new PublisherTemplate { PublisherId = publisher.Id, TemplateId = tid });

        await _context.SaveChangesAsync(ct);
        return await LoadAndReturn(req, publisher.Id, HttpStatusCode.OK, ct);
    }

    [Function("ManageDeletePublisher")]
    public async Task<HttpResponseData> DeletePublisher(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/publishers/{publisherId}")] HttpRequestData req,
        FunctionContext context,
        string publisherId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(publisherId, out var id)) return await BadRequest(req, "Invalid publisher id");

        var ct = context.CancellationToken;
        var publisher = await _context.Publishers.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (publisher is null) return req.CreateResponse(HttpStatusCode.NoContent);

        if (await _context.Placements.AnyAsync(p => p.PublisherId == publisher.Id, ct))
            return await BadRequest(req, "This publisher has placements — remove them before deleting");

        _context.Publishers.Remove(publisher);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private async Task<HttpResponseData> LoadAndReturn(HttpRequestData req, Guid id, HttpStatusCode status, CancellationToken ct)
    {
        var publisher = await _context.Publishers
            .AsNoTracking()
            .Include(p => p.PublisherTemplates).ThenInclude(pt => pt.Template)
            .FirstAsync(p => p.Id == id, ct);

        var dto = new PublisherDto(
            publisher.Id, publisher.Name, publisher.Slug, publisher.Website,
            publisher.PublisherTemplates.Select(pt => new PublisherTemplateDto(
                pt.Template.Id,
                pt.Template.Code.ToString().ToLowerInvariant(),
                pt.Template.Name
            )).ToList());

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
