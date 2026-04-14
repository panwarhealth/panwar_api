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
/// Brand + Audience CRUD for a specific client. Both entities are client-scoped
/// taxonomy (a brand belongs to one client; audiences like "Pharmacists" / "GPs"
/// are also per-client because different pharma companies target different HCP
/// segments).
/// </summary>
public class ManageTaxonomyFunction
{
    private static readonly Regex SlugPattern = new("^[a-z0-9](?:[a-z0-9-]{0,98}[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<ManageTaxonomyFunction> _logger;
    private readonly AppDbContext _context;

    public ManageTaxonomyFunction(ILogger<ManageTaxonomyFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    // ── Brands ───────────────────────────────────────────────────────────────

    [Function("ManageListBrands")]
    public async Task<HttpResponseData> ListBrands(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/brands")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var brands = await _context.Brands
            .AsNoTracking()
            .Where(b => b.ClientId == client.Id)
            .OrderBy(b => b.Name)
            .Select(b => new BrandDto(
                b.Id, b.Name, b.Slug,
                _context.Placements.Count(p => p.BrandId == b.Id)))
            .ToListAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { brands });
        return resp;
    }

    [Function("ManageCreateBrand")]
    public async Task<HttpResponseData> CreateBrand(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/brands")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var data = await ReadJson<BrandWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var name = (data.Name ?? "").Trim();
        var slug = (data.Slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return await BadRequest(req, "Name is required");
        if (!SlugPattern.IsMatch(slug)) return await BadRequest(req, "Invalid slug");

        if (await _context.Brands.AnyAsync(b => b.ClientId == client.Id && b.Slug == slug, ct))
            return await BadRequest(req, "A brand with that slug already exists for this client");

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            Name = name,
            Slug = slug,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.Brands.Add(brand);
        await _context.SaveChangesAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.Created);
        await resp.WriteAsJsonAsync(new BrandDto(brand.Id, brand.Name, brand.Slug, 0));
        return resp;
    }

    [Function("ManageUpdateBrand")]
    public async Task<HttpResponseData> UpdateBrand(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/brands/{brandId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string brandId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(brandId, out var id)) return await BadRequest(req, "Invalid brand id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == id && b.ClientId == client.Id, ct);
        if (brand is null) return await NotFound(req);

        var data = await ReadJson<BrandWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var name = (data.Name ?? "").Trim();
        var slug = (data.Slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return await BadRequest(req, "Name is required");
        if (!SlugPattern.IsMatch(slug)) return await BadRequest(req, "Invalid slug");

        if (slug != brand.Slug &&
            await _context.Brands.AnyAsync(b => b.ClientId == client.Id && b.Slug == slug, ct))
            return await BadRequest(req, "A brand with that slug already exists for this client");

        brand.Name = name;
        brand.Slug = slug;
        brand.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var placementCount = await _context.Placements.CountAsync(p => p.BrandId == brand.Id, ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new BrandDto(brand.Id, brand.Name, brand.Slug, placementCount));
        return resp;
    }

    [Function("ManageDeleteBrand")]
    public async Task<HttpResponseData> DeleteBrand(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/brands/{brandId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string brandId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(brandId, out var id)) return await BadRequest(req, "Invalid brand id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == id && b.ClientId == client.Id, ct);
        if (brand is null) return req.CreateResponse(HttpStatusCode.NoContent);

        var hasPlacements = await _context.Placements.AnyAsync(p => p.BrandId == brand.Id, ct);
        if (hasPlacements)
            return await BadRequest(req, "This brand has placements — remove them before deleting");

        _context.Brands.Remove(brand);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── Audiences ────────────────────────────────────────────────────────────

    [Function("ManageListAudiences")]
    public async Task<HttpResponseData> ListAudiences(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/audiences")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var audiences = await _context.Audiences
            .AsNoTracking()
            .Where(a => a.ClientId == client.Id)
            .OrderBy(a => a.Name)
            .Select(a => new AudienceDto(
                a.Id, a.Name, a.Slug,
                _context.Placements.Count(p => p.AudienceId == a.Id)))
            .ToListAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { audiences });
        return resp;
    }

    [Function("ManageCreateAudience")]
    public async Task<HttpResponseData> CreateAudience(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/audiences")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var data = await ReadJson<AudienceWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var name = (data.Name ?? "").Trim();
        var slug = (data.Slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return await BadRequest(req, "Name is required");
        if (!SlugPattern.IsMatch(slug)) return await BadRequest(req, "Invalid slug");

        if (await _context.Audiences.AnyAsync(a => a.ClientId == client.Id && a.Slug == slug, ct))
            return await BadRequest(req, "An audience with that slug already exists for this client");

        var audience = new Audience
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            Name = name,
            Slug = slug,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.Audiences.Add(audience);
        await _context.SaveChangesAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.Created);
        await resp.WriteAsJsonAsync(new AudienceDto(audience.Id, audience.Name, audience.Slug, 0));
        return resp;
    }

    [Function("ManageUpdateAudience")]
    public async Task<HttpResponseData> UpdateAudience(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/audiences/{audienceId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string audienceId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(audienceId, out var id)) return await BadRequest(req, "Invalid audience id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var audience = await _context.Audiences.FirstOrDefaultAsync(a => a.Id == id && a.ClientId == client.Id, ct);
        if (audience is null) return await NotFound(req);

        var data = await ReadJson<AudienceWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var name = (data.Name ?? "").Trim();
        var slug = (data.Slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return await BadRequest(req, "Name is required");
        if (!SlugPattern.IsMatch(slug)) return await BadRequest(req, "Invalid slug");

        if (slug != audience.Slug &&
            await _context.Audiences.AnyAsync(a => a.ClientId == client.Id && a.Slug == slug, ct))
            return await BadRequest(req, "An audience with that slug already exists for this client");

        audience.Name = name;
        audience.Slug = slug;
        audience.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var placementCount = await _context.Placements.CountAsync(p => p.AudienceId == audience.Id, ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new AudienceDto(audience.Id, audience.Name, audience.Slug, placementCount));
        return resp;
    }

    [Function("ManageDeleteAudience")]
    public async Task<HttpResponseData> DeleteAudience(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/audiences/{audienceId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string audienceId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(audienceId, out var id)) return await BadRequest(req, "Invalid audience id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var audience = await _context.Audiences.FirstOrDefaultAsync(a => a.Id == id && a.ClientId == client.Id, ct);
        if (audience is null) return req.CreateResponse(HttpStatusCode.NoContent);

        var hasPlacements = await _context.Placements.AnyAsync(p => p.AudienceId == audience.Id, ct);
        if (hasPlacements)
            return await BadRequest(req, "This audience has placements — remove them before deleting");

        _context.Audiences.Remove(audience);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

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
