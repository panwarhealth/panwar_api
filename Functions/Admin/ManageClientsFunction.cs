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
/// Client CRUD endpoints for the employee portal. Gated to panwar-admin and
/// dashboard-editor roles (medical writers can edit dashboards but don't manage
/// client onboarding).
/// </summary>
public class ManageClientsFunction
{
    private static readonly Regex SlugPattern = new("^[a-z0-9](?:[a-z0-9-]{0,98}[a-z0-9])?$", RegexOptions.Compiled);

    private readonly ILogger<ManageClientsFunction> _logger;
    private readonly AppDbContext _context;

    public ManageClientsFunction(ILogger<ManageClientsFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Function("ManageListClients")]
    public async Task<HttpResponseData> ListClients(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients")] HttpRequestData req,
        FunctionContext context)
    {
        if (!CanManageClients(req, context))
            return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var items = await _context.Clients
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClientListItemDto(
                c.Id, c.Name, c.Slug, c.LogoUrl, c.PrimaryColor, c.AccentColor,
                _context.UserClients.Count(uc => uc.ClientId == c.Id)))
            .ToListAsync(ct);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { clients = items });
        return response;
    }

    [Function("ManageCreateClient")]
    public async Task<HttpResponseData> CreateClient(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients")] HttpRequestData req,
        FunctionContext context)
    {
        if (!CanManageClients(req, context))
            return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var data = JsonSerializer.Deserialize<CreateClientRequest>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data is null)
            return await BadRequest(req, "Request body is required");

        var name = (data.Name ?? "").Trim();
        var slug = (data.Slug ?? "").Trim().ToLowerInvariant();

        if (name.Length == 0) return await BadRequest(req, "Name is required");
        if (!SlugPattern.IsMatch(slug))
            return await BadRequest(req, "Slug must be lowercase letters, numbers, or hyphens (2–100 chars)");

        if (await _context.Clients.AnyAsync(c => c.Slug == slug, ct))
            return await BadRequest(req, "A client with that slug already exists");

        var client = new Client
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            LogoUrl = string.IsNullOrWhiteSpace(data.LogoUrl) ? null : data.LogoUrl.Trim(),
            PrimaryColor = string.IsNullOrWhiteSpace(data.PrimaryColor) ? null : data.PrimaryColor.Trim(),
            AccentColor = string.IsNullOrWhiteSpace(data.AccentColor) ? null : data.AccentColor.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.Clients.Add(client);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created client {Slug} ({Id})", slug, client.Id);

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new ClientListItemDto(
            client.Id, client.Name, client.Slug, client.LogoUrl, client.PrimaryColor, client.AccentColor, 0));
        return response;
    }

    private static bool CanManageClients(HttpRequestData req, FunctionContext context)
        => req.HasRole(context, "panwar-admin") || req.HasRole(context, "dashboard-editor");

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var resp = req.CreateResponse(HttpStatusCode.BadRequest);
        await resp.WriteAsJsonAsync(new { error = message });
        return resp;
    }
}
