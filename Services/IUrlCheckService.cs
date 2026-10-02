namespace Panwar.Api.Services;

public sealed record UrlCheckResult(bool Found, int? StatusCode);

public interface IUrlCheckService
{
    Task<UrlCheckResult> CheckAsync(Uri url, CancellationToken cancellationToken = default);
}
