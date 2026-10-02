namespace Panwar.Api.Services;

public interface IJobClientService
{
    Task<IReadOnlyDictionary<string, string>> GetClientsByPrefixAsync(CancellationToken cancellationToken = default);
}
