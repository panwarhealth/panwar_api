namespace Panwar.Api.Infrastructure.CloudflareR2;

public interface ICloudflareR2Service
{
    /// <summary>
    /// Generate a presigned PUT URL the browser can upload artwork to directly.
    /// Returns the URL to PUT to and the canonical key to store on the placement.
    /// </summary>
    Task<(string uploadUrl, string objectKey)> GenerateUploadUrlAsync(string fileName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a presigned GET URL the browser can fetch the artwork from.
    /// Used by the client dashboard to display placement artwork. Short-lived
    /// (15 min) so leaked URLs expire quickly. The bucket has no public access
    /// — these signed URLs are the only way to read artwork.
    /// </summary>
    Task<string> GenerateDownloadUrlAsync(string objectKey, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
}
