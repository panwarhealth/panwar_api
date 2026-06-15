using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace Panwar.Api.Services;

public class GraphApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public GraphApiException(HttpStatusCode statusCode, string message) : base(message)
        => StatusCode = statusCode;
}

public class GraphService : IGraphService
{
    private readonly ILogger<GraphService> _logger;
    private readonly IConfidentialClientApplication _msalApp;
    private readonly HttpClient _http;
    private readonly string _clientId;

    private string? _servicePrincipalId;
    private Dictionary<string, (string Value, string DisplayName)>? _appRoles;

    private static readonly string[] GraphScope = ["https://graph.microsoft.com/.default"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GraphService(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<GraphService> logger)
    {
        _logger = logger;
        _http = httpClientFactory.CreateClient();

        var tenantId = configuration["ENTRA_TENANT_ID"]
            ?? throw new InvalidOperationException("ENTRA_TENANT_ID not configured");
        _clientId = configuration["ENTRA_EMPLOYEE_SSO_CLIENT_ID"]
            ?? throw new InvalidOperationException("ENTRA_EMPLOYEE_SSO_CLIENT_ID not configured");
        var clientSecret = configuration["ENTRA_CLIENT_SECRET"]
            ?? throw new InvalidOperationException("ENTRA_CLIENT_SECRET not configured");

        _msalApp = ConfidentialClientApplicationBuilder
            .Create(_clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .Build();
    }

    public async Task<List<GraphUser>> GetUsersWithRolesAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        await EnsureAppMetadataAsync(token, cancellationToken);

        var users = new List<GraphUser>();
        var url = "https://graph.microsoft.com/v1.0/users?$select=id,displayName,mail,userPrincipalName&$filter=accountEnabled eq true and userType eq 'Member' and endsWith(userPrincipalName,'panwarhealth.com.au')&$count=true&$top=999";

        while (!string.IsNullOrEmpty(url))
        {
            var json = await GraphGetAsync(token, url, cancellationToken);
            var result = JsonSerializer.Deserialize<GraphListResponse<GraphUserDto>>(json, JsonOptions);
            if (result?.Value is not null)
            {
                foreach (var u in result.Value)
                {
                    users.Add(new GraphUser
                    {
                        Id = u.Id,
                        DisplayName = u.DisplayName ?? "",
                        Email = u.Mail ?? u.UserPrincipalName ?? "",
                    });
                }
            }
            url = result?.OdataNextLink;
        }

        var assignments = new List<GraphRoleAssignmentDto>();
        var assignUrl = $"https://graph.microsoft.com/v1.0/servicePrincipals/{_servicePrincipalId}/appRoleAssignedTo?$top=999";

        while (!string.IsNullOrEmpty(assignUrl))
        {
            var json = await GraphGetAsync(token, assignUrl, cancellationToken);
            var result = JsonSerializer.Deserialize<GraphListResponse<GraphRoleAssignmentDto>>(json, JsonOptions);
            if (result?.Value is not null)
                assignments.AddRange(result.Value);
            assignUrl = result?.OdataNextLink;
        }

        var assignmentsByUser = assignments
            .Where(a => a.PrincipalType == "User")
            .GroupBy(a => a.PrincipalId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var user in users)
        {
            if (assignmentsByUser.TryGetValue(user.Id, out var userAssignments))
            {
                user.Roles = userAssignments
                    .Where(a => _appRoles!.ContainsKey(a.AppRoleId))
                    .Select(a => new GraphRoleAssignment
                    {
                        AssignmentId = a.Id,
                        RoleValue = _appRoles![a.AppRoleId].Value,
                        RoleDisplayName = _appRoles[a.AppRoleId].DisplayName,
                    })
                    .ToList();
            }
        }

        return users;
    }

    public async Task<string> AssignRoleAsync(string userObjectId, string roleValue, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        await EnsureAppMetadataAsync(token, cancellationToken);

        var roleEntry = _appRoles!.FirstOrDefault(r => r.Value.Value == roleValue);
        if (roleEntry.Key is null)
            throw new InvalidOperationException($"Unknown app role: {roleValue}");

        var body = new
        {
            principalId = userObjectId,
            resourceId = _servicePrincipalId,
            appRoleId = roleEntry.Key
        };

        try
        {
            var json = await GraphPostAsync(token,
                $"https://graph.microsoft.com/v1.0/servicePrincipals/{_servicePrincipalId}/appRoleAssignedTo",
                body, cancellationToken);

            var result = JsonSerializer.Deserialize<GraphRoleAssignmentDto>(json, JsonOptions);
            _logger.LogInformation("Assigned role {Role} to user {UserId}", roleValue, userObjectId);
            return result?.Id ?? "";
        }
        catch (GraphApiException ex)
            when (ex.StatusCode == HttpStatusCode.BadRequest
                  && ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotent: role already assigned, return the existing assignment id.
            _logger.LogInformation("Role {Role} already assigned to user {UserId}; returning existing assignment",
                roleValue, userObjectId);
            return await FindAssignmentIdAsync(token, userObjectId, roleEntry.Key!, cancellationToken) ?? "";
        }
    }

    public async Task RemoveRoleAsync(string userObjectId, string assignmentId, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        await EnsureAppMetadataAsync(token, cancellationToken);

        try
        {
            // User-centric endpoint avoids Graph rejecting self-referential SP deletes.
            await GraphDeleteAsync(token,
                $"https://graph.microsoft.com/v1.0/users/{userObjectId}/appRoleAssignments/{assignmentId}",
                cancellationToken);
            _logger.LogInformation("Removed role assignment {AssignmentId} from user {UserId}", assignmentId, userObjectId);
        }
        catch (GraphApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Role assignment {AssignmentId} already absent; nothing to remove", assignmentId);
        }
    }

    // Scans appRoleAssignedTo to recover an existing assignment id (small staff set, scan is fine).
    private async Task<string?> FindAssignmentIdAsync(
        string token, string userObjectId, string appRoleId, CancellationToken cancellationToken)
    {
        var url = $"https://graph.microsoft.com/v1.0/servicePrincipals/{_servicePrincipalId}/appRoleAssignedTo?$top=999";
        while (!string.IsNullOrEmpty(url))
        {
            var json = await GraphGetAsync(token, url, cancellationToken);
            var result = JsonSerializer.Deserialize<GraphListResponse<GraphRoleAssignmentDto>>(json, JsonOptions);
            var match = result?.Value?.FirstOrDefault(a => a.PrincipalId == userObjectId && a.AppRoleId == appRoleId);
            if (match is not null) return match.Id;
            url = result?.OdataNextLink;
        }
        return null;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var result = await _msalApp.AcquireTokenForClient(GraphScope).ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }

    private async Task EnsureAppMetadataAsync(string token, CancellationToken cancellationToken)
    {
        if (_servicePrincipalId is not null && _appRoles is not null) return;

        var spJson = await GraphGetAsync(token,
            $"https://graph.microsoft.com/v1.0/servicePrincipals?$filter=appId eq '{_clientId}'",
            cancellationToken);

        var spResult = JsonSerializer.Deserialize<GraphListResponse<GraphServicePrincipalDto>>(spJson, JsonOptions);
        var sp = spResult?.Value?.FirstOrDefault()
            ?? throw new InvalidOperationException("Service principal not found for app");

        _servicePrincipalId = sp.Id;
        _appRoles = sp.AppRoles
            .Where(r => r.IsEnabled)
            .ToDictionary(r => r.Id, r => (r.Value, r.DisplayName));

        _logger.LogInformation("Loaded {Count} app roles for service principal {SpId}",
            _appRoles.Count, _servicePrincipalId);
    }

    private async Task<string> GraphGetAsync(string token, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("ConsistencyLevel", "eventual"); // required for endsWith/$count filters
        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Graph GET {Url} failed: {Status} {Body}", url, resp.StatusCode, body);
            throw new GraphApiException(resp.StatusCode, ExtractGraphMessage(body));
        }
        return body;
    }

    private async Task<string> GraphPostAsync(string token, string url, object payload, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Graph POST {Url} failed: {Status} {Body}", url, resp.StatusCode, body);
            throw new GraphApiException(resp.StatusCode, ExtractGraphMessage(body));
        }
        return body;
    }

    private async Task GraphDeleteAsync(string token, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Graph DELETE {Url} failed: {Status} {Body}", url, resp.StatusCode, body);
            throw new GraphApiException(resp.StatusCode, ExtractGraphMessage(body));
        }
    }

    private static string ExtractGraphMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.TryGetProperty("message", out var msg))
            {
                return msg.GetString() ?? "Microsoft Graph request failed.";
            }
        }
        catch
        {
            // non-JSON or empty body
        }
        return "Microsoft Graph request failed.";
    }

    private class GraphListResponse<T>
    {
        public List<T>? Value { get; set; }
        public string? OdataNextLink { get; set; }
    }

    private class GraphUserDto
    {
        public string Id { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? Mail { get; set; }
        public string? UserPrincipalName { get; set; }
    }

    private class GraphServicePrincipalDto
    {
        public string Id { get; set; } = "";
        public List<GraphAppRoleDto> AppRoles { get; set; } = [];
    }

    private class GraphAppRoleDto
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IsEnabled { get; set; }
    }

    private class GraphRoleAssignmentDto
    {
        public string Id { get; set; } = "";
        public string PrincipalId { get; set; } = "";
        public string PrincipalType { get; set; } = "";
        public string AppRoleId { get; set; } = "";
    }
}
