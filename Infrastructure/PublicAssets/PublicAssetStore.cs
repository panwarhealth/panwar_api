using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace Panwar.Api.Infrastructure.PublicAssets;

public interface IPublicAssetStore
{
    /// <summary>Uploads to the public asset bucket and returns the URL anyone can load it from.</summary>
    Task<string> PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken = default);

    Task<(byte[] Bytes, string ContentType)?> GetAsync(string key, CancellationToken cancellationToken = default);

    string? KeyFromUrl(string url);
}

/// <summary>
/// eDM images and course editor media have to be publicly fetchable, so they live in a separate public R2
/// bucket rather than the private panwar-portals-media one. Credentials default to the shared
/// CLOUDFLARE_R2_* token; set EDM_R2_ACCESS_KEY / EDM_R2_SECRET_KEY if that token isn't scoped to it.
/// </summary>
public class PublicAssetStore : IPublicAssetStore
{
    private readonly IConfiguration _configuration;
    private AmazonS3Client? _client;

    public PublicAssetStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<string> PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        await Client().PutObjectAsync(new PutObjectRequest
        {
            BucketName = Require("EDM_R2_BUCKET"),
            Key = key,
            InputStream = new MemoryStream(bytes),
            ContentType = contentType,
            Headers = { CacheControl = "public, max-age=31536000, immutable" },
            // R2 doesn't accept the SDK's streaming-signature or trailing-checksum uploads.
            DisablePayloadSigning = true,
        }, cancellationToken);

        return $"{PublicBase()}/{key}";
    }

    public async Task<(byte[] Bytes, string ContentType)?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var obj = await Client().GetObjectAsync(Require("EDM_R2_BUCKET"), key, cancellationToken);
            using var ms = new MemoryStream();
            await obj.ResponseStream.CopyToAsync(ms, cancellationToken);
            return (ms.ToArray(), obj.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public string? KeyFromUrl(string url)
    {
        var prefix = PublicBase() + "/";
        return url.StartsWith(prefix, StringComparison.Ordinal) ? url[prefix.Length..] : null;
    }

    private string PublicBase() => Require("EDM_R2_PUBLIC_BASE_URL").TrimEnd('/');

    private AmazonS3Client Client()
    {
        if (_client is not null) return _client;
        var accountId = Require("CLOUDFLARE_R2_ACCOUNT_ID");
        var accessKey = _configuration["EDM_R2_ACCESS_KEY"] ?? Require("CLOUDFLARE_R2_ACCESS_KEY");
        var secretKey = _configuration["EDM_R2_SECRET_KEY"] ?? Require("CLOUDFLARE_R2_SECRET_KEY");
        _client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
        return _client;
    }

    private string Require(string key) =>
        _configuration[key] is { Length: > 0 } v ? v : throw new InvalidOperationException($"Public asset hosting isn't configured ({key})");
}
