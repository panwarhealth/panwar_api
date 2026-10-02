using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Panwar.Api.Services;

public class JobClientService : IJobClientService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    private static readonly Regex JobFolder = new("^([A-Za-z]{2,5})[0-9]{4,5}(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private readonly IGraphService _graphService;
    private readonly ILogger<JobClientService> _logger;
    private readonly string _siteId;
    private readonly string _jobsPath;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private IReadOnlyDictionary<string, string>? _cache;
    private DateTime _cachedAt;

    public JobClientService(IGraphService graphService, IConfiguration configuration, ILogger<JobClientService> logger)
    {
        _graphService = graphService;
        _logger = logger;
        _siteId = configuration["SHAREPOINT_SITE_ID"]
            ?? throw new InvalidOperationException("SHAREPOINT_SITE_ID not configured");
        _jobsPath = configuration["SHAREPOINT_JOBS_PATH"]
            ?? throw new InvalidOperationException("SHAREPOINT_JOBS_PATH not configured");
    }

    public async Task<IReadOnlyDictionary<string, string>> GetClientsByPrefixAsync(CancellationToken cancellationToken = default)
    {
        if (IsFresh()) return _cache!;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (IsFresh()) return _cache!;

            _cache = await LoadAsync(cancellationToken);
            _cachedAt = DateTime.UtcNow;
            return _cache;
        }
        catch (Exception ex) when (ex is GraphApiException or HttpRequestException)
        {
            _logger.LogError(ex, "Could not read job folders from SharePoint");
            return _cache ?? Empty;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsFresh() => _cache is not null && DateTime.UtcNow - _cachedAt < CacheLifetime;

    private async Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        var clients = await _graphService.GetFolderNamesAsync(_siteId, _jobsPath, cancellationToken);
        var jobFolders = await Task.WhenAll(clients.Select(async client =>
            (Client: client, Jobs: await _graphService.GetFolderNamesAsync(_siteId, $"{_jobsPath}/{client}", cancellationToken))));

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (client, jobs) in jobFolders)
        {
            foreach (var job in jobs)
            {
                var match = JobFolder.Match(job);
                if (match.Success) map.TryAdd(match.Groups[1].Value.ToUpperInvariant(), client);
            }
        }

        _logger.LogInformation("Loaded {Count} job prefixes from SharePoint", map.Count);
        return map;
    }
}
