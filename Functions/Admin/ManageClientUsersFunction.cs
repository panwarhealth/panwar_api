using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

/// <summary>
/// Manage which client users have access to a specific client. Gated to
/// panwar-admin and dashboard-editor. Staff emails (@panwarhealth.com.au) are
/// rejected — staff have blanket access via their Entra role, not via user_client.
/// </summary>
public class ManageClientUsersFunction
{
    private const string StaffDomainSuffix = "@panwarhealth.com.au";

    private readonly ILogger<ManageClientUsersFunction> _logger;
    private readonly AppDbContext _context;

    public ManageClientUsersFunction(ILogger<ManageClientUsersFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Function("ManageListClientUsers")]
    public async Task<HttpResponseData> ListUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/users")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context))
            return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var users = await _context.UserClients
            .AsNoTracking()
            .Where(uc => uc.ClientId == client.Id)
            .Join(_context.Users, uc => uc.UserId, u => u.Id, (uc, u) => u)
            .OrderBy(u => u.Email)
            .Select(u => new ClientUserDto(u.Id, u.Email, u.Name, u.LastLoginAt, u.CreatedAt))
            .ToListAsync(ct);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { users });
        return response;
    }

    [Function("ManageAddClientUser")]
    public async Task<HttpResponseData> AddUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/users")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context))
            return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var data = JsonSerializer.Deserialize<AddClientUserRequest>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var email = (data?.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0 || !email.Contains('@'))
            return await BadRequest(req, "A valid email is required");

        if (email.EndsWith(StaffDomainSuffix, StringComparison.Ordinal))
            return await BadRequest(req, "Staff emails already have access via their role — no need to assign them here");

        // Find-or-create the client user
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                Type = UserType.Client,
                Email = email,
                Name = string.IsNullOrWhiteSpace(data?.Name) ? null : data!.Name!.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _context.Users.Add(user);
        }
        else if (user.Type != UserType.Client)
        {
            return await BadRequest(req, "That email belongs to a non-client account");
        }

        // Ensure membership row exists
        var alreadyMember = await _context.UserClients
            .AnyAsync(uc => uc.UserId == user.Id && uc.ClientId == client.Id, ct);
        if (!alreadyMember)
        {
            _context.UserClients.Add(new UserClient
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ClientId = client.Id,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Granted {Email} access to {ClientSlug}", email, clientSlug);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new ClientUserDto(
            user.Id, user.Email, user.Name, user.LastLoginAt, user.CreatedAt));
        return response;
    }

    [Function("ManageRemoveClientUser")]
    public async Task<HttpResponseData> RemoveUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/clients/{clientSlug}/users/{userId}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        string userId)
    {
        if (!CanManage(req, context))
            return await req.CreateForbiddenResponseAsync();

        if (!Guid.TryParse(userId, out var userGuid))
            return await BadRequest(req, "Invalid user id");

        var ct = context.CancellationToken;
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct);
        if (client is null) return await NotFound(req);

        var row = await _context.UserClients
            .FirstOrDefaultAsync(uc => uc.UserId == userGuid && uc.ClientId == client.Id, ct);
        if (row is null) return req.CreateResponse(HttpStatusCode.NoContent);

        _context.UserClients.Remove(row);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Revoked user {UserId} access to {ClientSlug}", userGuid, clientSlug);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private static bool CanManage(HttpRequestData req, FunctionContext context)
        => req.HasRole(context, "panwar-admin") || req.HasRole(context, "dashboard-editor");

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var resp = req.CreateResponse(HttpStatusCode.BadRequest);
        await resp.WriteAsJsonAsync(new { error = message });
        return resp;
    }

    private static async Task<HttpResponseData> NotFound(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.NotFound);
        await resp.WriteAsJsonAsync(new { error = "Client not found" });
        return resp;
    }
}
