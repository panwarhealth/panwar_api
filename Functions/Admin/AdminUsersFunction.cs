using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panwar.Api.Services;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

public class AdminUsersFunction
{
    private readonly ILogger<AdminUsersFunction> _logger;
    private readonly IGraphService _graphService;
    private readonly string? _secretExpiry;

    public AdminUsersFunction(
        ILogger<AdminUsersFunction> logger,
        IGraphService graphService,
        IConfiguration configuration)
    {
        _logger = logger;
        _graphService = graphService;
        _secretExpiry = configuration["ENTRA_CLIENT_SECRET_EXPIRY"];
    }

    /// <summary>
    /// GET /api/admin/users — list all tenant users with their app role assignments.
    /// </summary>
    [Function("AdminGetUsers")]
    public async Task<HttpResponseData> GetUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/users")] HttpRequestData req,
        FunctionContext context)
    {
        if (!req.HasRole(context, "panwar-admin"))
            return await req.CreateForbiddenResponseAsync();

        try
        {
            var users = await _graphService.GetUsersWithRolesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { users, secretExpiry = _secretExpiry });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch users from Graph API");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to fetch users" });
            return error;
        }
    }

    /// <summary>
    /// POST /api/admin/users/{userId}/roles  { "role": "panwar-admin" }
    /// </summary>
    [Function("AdminAssignRole")]
    public async Task<HttpResponseData> AssignRole(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/users/{userId}/roles")] HttpRequestData req,
        FunctionContext context,
        string userId)
    {
        if (!req.HasRole(context, "panwar-admin"))
            return await req.CreateForbiddenResponseAsync();

        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<RoleRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data is null || string.IsNullOrWhiteSpace(data.Role))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "role is required" });
                return bad;
            }

            var assignmentId = await _graphService.AssignRoleAsync(userId, data.Role);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { assignmentId });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign role to user {UserId}", userId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = ex.Message });
            return error;
        }
    }

    /// <summary>
    /// DELETE /api/admin/users/roles/{assignmentId}
    /// </summary>
    [Function("AdminRemoveRole")]
    public async Task<HttpResponseData> RemoveRole(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/users/roles/{assignmentId}")] HttpRequestData req,
        FunctionContext context,
        string assignmentId)
    {
        if (!req.HasRole(context, "panwar-admin"))
            return await req.CreateForbiddenResponseAsync();

        try
        {
            await _graphService.RemoveRoleAsync(assignmentId);
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove role assignment {AssignmentId}", assignmentId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = ex.Message });
            return error;
        }
    }

    private class RoleRequest
    {
        public string Role { get; set; } = "";
    }
}
