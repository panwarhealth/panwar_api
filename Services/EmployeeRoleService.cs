using Microsoft.Extensions.Logging;

namespace Panwar.Api.Services;

public interface IEmployeeRoleService
{
    Task<string[]> GetRolesAsync(string entraObjectId, CancellationToken cancellationToken = default);
}

public class EmployeeRoleService : IEmployeeRoleService
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);

    private readonly IGraphService _graph;
    private readonly ILogger<EmployeeRoleService> _logger;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private Dictionary<string, string[]>? _rolesByObjectId;
    private DateTime _loadedAt;

    public EmployeeRoleService(IGraphService graph, ILogger<EmployeeRoleService> logger)
    {
        _graph = graph;
        _logger = logger;
    }

    public async Task<string[]> GetRolesAsync(string entraObjectId, CancellationToken cancellationToken = default)
    {
        if (IsStale())
            await RefreshAsync(cancellationToken);
        return _rolesByObjectId!.GetValueOrDefault(entraObjectId) ?? [];
    }

    private bool IsStale() => _rolesByObjectId is null || DateTime.UtcNow - _loadedAt > MaxAge;

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (!IsStale()) return;
            var users = await _graph.GetUsersWithRolesAsync(cancellationToken);
            _rolesByObjectId = users.ToDictionary(u => u.Id, u => u.Roles.Select(r => r.RoleValue).ToArray());
            _loadedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (_rolesByObjectId is not null)
        {
            _logger.LogError(ex, "Refreshing employee roles from Graph failed, keeping roles loaded at {LoadedAt}", _loadedAt);
            _loadedAt = DateTime.UtcNow;
        }
        finally
        {
            _refresh.Release();
        }
    }
}
