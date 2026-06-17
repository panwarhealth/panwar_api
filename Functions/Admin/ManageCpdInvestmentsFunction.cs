using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

public class ManageCpdInvestmentsFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDbContext _context;

    public ManageCpdInvestmentsFunction(AppDbContext context)
    {
        _context = context;
    }

    [Function("ManageListCpdInvestments")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/cpd-investments")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var baseQuery = _context.CpdInvestments.AsNoTracking().Where(c => c.Brand.ClientId == client.Id);

        var years = await baseQuery.Select(c => c.Year).Distinct().OrderBy(y => y).ToListAsync(ct);

        var filters = HttpUtility.ParseQueryString(req.Url.Query);
        var query = baseQuery;
        if (int.TryParse(filters["year"], out var year))
            query = query.Where(c => c.Year == year);

        var items = await query
            .OrderBy(c => c.Brand.Name).ThenBy(c => c.Audience.Name).ThenBy(c => c.Publisher.Name).ThenBy(c => c.Title)
            .Select(c => new CpdInvestmentListItemDto(
                c.Id,
                c.BrandId, c.Brand.Name,
                c.AudienceId, c.Audience.Name,
                c.PublisherId, c.Publisher.Name,
                c.Year,
                c.StartMonth,
                c.EndMonth,
                c.Title,
                c.Format,
                c.Cost,
                c.Notes))
            .ToListAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new CpdInvestmentListResponse(items, years));
        return resp;
    }

    [Function("ManageCreateCpdInvestment")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/cpd-investments")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var data = await ReadJson<CpdInvestmentWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var error = await Validate(client.Id, data, ct);
        if (error is not null) return await BadRequest(req, error);

        var now = DateTime.UtcNow;
        var userId = req.GetUserId(context);
        var row = new CpdInvestment
        {
            Id = Guid.NewGuid(),
            BrandId = data.BrandId,
            AudienceId = data.AudienceId,
            PublisherId = data.PublisherId,
            Year = data.Year,
            StartMonth = data.StartMonth ?? data.EndMonth,
            EndMonth = data.EndMonth ?? data.StartMonth,
            Title = data.Title.Trim(),
            Format = data.Format.Trim().ToLowerInvariant(),
            Cost = data.Cost,
            Notes = Clean(data.Notes),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
            UpdatedBy = userId,
        };
        _context.CpdInvestments.Add(row);
        await _context.SaveChangesAsync(ct);

        var resp = req.CreateResponse(HttpStatusCode.Created);
        await resp.WriteAsJsonAsync(new { id = row.Id });
        return resp;
    }

    [Function("ManageUpdateCpdInvestment")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "manage/clients/{clientSlug}/cpd-investments/{cpdId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string cpdId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(cpdId, out var id)) return await BadRequest(req, "Invalid id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var row = await _context.CpdInvestments.FirstOrDefaultAsync(c => c.Id == id && c.Brand.ClientId == client.Id, ct);
        if (row is null) return await NotFound(req);

        var data = await ReadJson<CpdInvestmentWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        var error = await Validate(client.Id, data, ct);
        if (error is not null) return await BadRequest(req, error);

        row.BrandId = data.BrandId;
        row.AudienceId = data.AudienceId;
        row.PublisherId = data.PublisherId;
        row.Year = data.Year;
        row.StartMonth = data.StartMonth ?? data.EndMonth;
        row.EndMonth = data.EndMonth ?? data.StartMonth;
        row.Title = data.Title.Trim();
        row.Format = data.Format.Trim().ToLowerInvariant();
        row.Cost = data.Cost;
        row.Notes = Clean(data.Notes);
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = req.GetUserId(context);
        await _context.SaveChangesAsync(ct);

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("ManageDeleteCpdInvestment")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/cpd-investments/{cpdId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string cpdId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(cpdId, out var id)) return await BadRequest(req, "Invalid id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var row = await _context.CpdInvestments.FirstOrDefaultAsync(c => c.Id == id && c.Brand.ClientId == client.Id, ct);
        if (row is null) return req.CreateResponse(HttpStatusCode.NoContent);

        _context.CpdInvestments.Remove(row);
        await _context.SaveChangesAsync(ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private async Task<string?> Validate(Guid clientId, CpdInvestmentWriteRequest data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.Title)) return "Title is required";
        if (!CpdFormats.Allowed.Contains((data.Format ?? "").Trim())) return "Invalid format";
        if (data.Cost < 0) return "Cost must be >= 0";
        if (data.Year < 2000 || data.Year > 2100) return "Year is out of range";

        if (data.StartMonth is { } sm && (sm < 1 || sm > 12)) return "Start month is out of range";
        if (data.EndMonth is { } em && (em < 1 || em > 12)) return "End month is out of range";
        if (data.StartMonth is { } s && data.EndMonth is { } e && s > e) return "Start month must be on or before end month";

        var brandOk = await _context.Brands.AnyAsync(b => b.Id == data.BrandId && b.ClientId == clientId, ct);
        if (!brandOk) return "Brand not found for this client";
        var audienceOk = await _context.Audiences.AnyAsync(a => a.Id == data.AudienceId && a.ClientId == clientId, ct);
        if (!audienceOk) return "Audience not found for this client";
        var publisherOk = await _context.Publishers.AnyAsync(p => p.Id == data.PublisherId, ct);
        if (!publisherOk) return "Publisher not found";
        return null;
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
