using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.AppConfig;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Infrastructure.ClinicalStudio;

public record ClinicalStudioPackage(string UploadId, int TotalFiles, string Title, string Version, string? ConfirmCourseTitle);

public class ClinicalStudioException(string message) : RequestValidationException(message);

public interface IClinicalStudioPublisher
{
    Task<JsonElement> RequestUploadAsync(Guid lmsCourseId, IReadOnlyList<string> filePaths, string publishedBy, CancellationToken cancellationToken = default);
    Task<JsonElement> CompleteUploadAsync(Guid lmsCourseId, ClinicalStudioPackage package, string publishedBy, CancellationToken cancellationToken = default);
}

public class ClinicalStudioPublisher : IClinicalStudioPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly IManagedIdentityApplication _identity =
        ManagedIdentityApplicationBuilder.Create(ManagedIdentityId.SystemAssigned).Build();

    public ClinicalStudioPublisher(IHttpClientFactory http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    public Task<JsonElement> RequestUploadAsync(Guid lmsCourseId, IReadOnlyList<string> filePaths, string publishedBy, CancellationToken cancellationToken = default)
        => PostAsync($"manage/courses/{lmsCourseId}/scorm/upload-urls", new { filePaths }, publishedBy, cancellationToken);

    public Task<JsonElement> CompleteUploadAsync(Guid lmsCourseId, ClinicalStudioPackage package, string publishedBy, CancellationToken cancellationToken = default)
        => PostAsync($"manage/courses/{lmsCourseId}/scorm/complete", new
        {
            uploadId = package.UploadId,
            entryPoint = "index.html",
            totalFiles = package.TotalFiles,
            packageTitle = package.Title,
            packageVersion = package.Version,
            scormVersion = "1.2",
            confirmCourseTitle = package.ConfirmCourseTitle,
        }, publishedBy, cancellationToken);

    private async Task<JsonElement> PostAsync(string path, object body, string publishedBy, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Setting("CLINICAL_STUDIO_API_URL").TrimEnd('/')}/{path}")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancellationToken));
        request.Headers.TryAddWithoutValidation("X-Published-By", new string(publishedBy.Where(c => c is >= ' ' and <= '~').ToArray()));

        using var response = await _http.CreateClient().SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = Parse(text);
        if (response.IsSuccessStatusCode && json is { } ok) return ok;

        var error = json is { ValueKind: JsonValueKind.Object } failed && failed.TryGetProperty("error", out var message)
            ? message.GetString()
            : null;
        throw new ClinicalStudioException($"Clinical Studio: {error ?? $"request failed with status {(int)response.StatusCode}"}");
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _identity.AcquireTokenForManagedIdentity(Setting("CLINICAL_STUDIO_API_RESOURCE")).ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }
        catch (MsalException)
        {
            throw new ClinicalStudioException("Could not sign in to Clinical Studio");
        }
    }

    private string Setting(string name)
        => _configuration[name] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{name} not configured");

    private static JsonElement? Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
