using System.Net;
using System.Net.Sockets;

namespace Panwar.Api.Services;

public class UrlCheckService : IUrlCheckService
{
    public const string HttpClientName = "url-check";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly IHttpClientFactory _httpClientFactory;

    public UrlCheckService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<UrlCheckResult> CheckAsync(Uri url, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(url.DnsSafeHost, timeout.Token);
            if (addresses.Length == 0 || !addresses.All(IsPublic)) return new UrlCheckResult(false, null);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; PanwarLinkCheck/1.0)");

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            return new UrlCheckResult(status < 400, status);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new UrlCheckResult(false, null);
        }
    }

    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal);

        var b = address.GetAddressBytes();
        return !(b[0] == 0
            || b[0] == 10
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168));
    }
}
