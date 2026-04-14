using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services.Authorization;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Dashboards;

/// <summary>
/// GET /api/clients/{clientSlug}/brands — brands + audiences for a client.
/// Policy-gated: caller must have access to the client.
/// </summary>
public class GetClientBrandsFunction
{
    private readonly ILogger<GetClientBrandsFunction> _logger;
    private readonly AppDbContext _context;
    private readonly IDashboardAccessResolver _accessResolver;

    public GetClientBrandsFunction(
        ILogger<GetClientBrandsFunction> logger,
        AppDbContext context,
        IDashboardAccessResolver accessResolver)
    {
        _logger = logger;
        _context = context;
        _accessResolver = accessResolver;
    }

    [Function("GetClientBrands")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "clients/{clientSlug}/brands")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        try
        {
            var userId = req.GetUserId(context);
            var userType = req.GetUserType(context);
            if (userId is null || userType is null)
                return await req.CreateUnauthorizedResponseAsync();

            var ct = context.CancellationToken;

            var client = await _context.Clients
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
            if (client is null)
                return NotFoundResponse(req);

            var canAccess = await _accessResolver.CanAccessClientAsync(
                userId.Value, userType.Value, req.GetRoles(context), client.Id, ct);
            if (!canAccess)
                return NotFoundResponse(req); // 404 not 403 — don't leak existence

            var brands = await _context.Brands
                .AsNoTracking()
                .Where(b => b.ClientId == client.Id)
                .OrderBy(b => b.Name)
                .Select(b => new BrandSummaryDto(b.Id, b.Name, b.Slug))
                .ToListAsync(ct);

            var audiences = await _context.Audiences
                .AsNoTracking()
                .Where(a => a.ClientId == client.Id)
                .OrderBy(a => a.Name)
                .Select(a => new AudienceSummaryDto(a.Id, a.Name, a.Slug))
                .ToListAsync(ct);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new ClientBrandsResponse(
                Client: new ClientSummaryDto(
                    client.Id, client.Name, client.Slug,
                    client.LogoUrl, client.PrimaryColor, client.AccentColor),
                Brands: brands,
                Audiences: audiences));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load brands for client {ClientSlug}", clientSlug);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to load brands" });
            return error;
        }
    }

    private static HttpResponseData NotFoundResponse(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.NotFound);
        resp.WriteAsJsonAsync(new { error = "Not found" }).GetAwaiter().GetResult();
        return resp;
    }
}
