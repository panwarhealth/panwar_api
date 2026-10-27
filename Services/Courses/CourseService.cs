using Panwar.Api.Shared.Extensions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.PublicAssets;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Courses;

public interface ICourseService
{
    Task<IReadOnlyList<CourseSummaryDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<CourseDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CourseDto> CreateAsync(CourseWriteRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<CourseDto?> UpdateAsync(Guid id, CourseWriteRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<CourseDto?> MarkExportedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, string? confirmTitle, CancellationToken cancellationToken = default);
    Task<CourseMediaDto?> UploadMediaAsync(Guid id, string fileName, byte[] bytes, CancellationToken cancellationToken = default);
    Task<(byte[] Bytes, string ContentType)?> GetMediaAsync(Guid id, string url, CancellationToken cancellationToken = default);
    Task<CourseMediaDto?> ImportMediaAsync(Guid id, string url, CancellationToken cancellationToken = default);
}

public class CourseValidationException(string message) : RequestValidationException(message);

public class CourseService : ICourseService
{
    public const int MaxMediaBytes = 90 * 1024 * 1024;
    public const string MediaDownloadClient = "course-media-download";
    private const int MaxTitleLength = 200;

    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".mp4"] = "video/mp4",
        [".mp3"] = "audio/mpeg",
        [".vtt"] = "text/vtt",
        [".json"] = "application/json",
        [".woff2"] = "font/woff2",
        [".woff"] = "font/woff",
        [".ttf"] = "font/ttf",
    };

    private readonly AppDbContext _context;
    private readonly IPublicAssetStore _assets;
    private readonly IHttpClientFactory _httpFactory;

    public CourseService(AppDbContext context, IPublicAssetStore assets, IHttpClientFactory httpFactory)
    {
        _context = context;
        _assets = assets;
        _httpFactory = httpFactory;
    }

    public async Task<IReadOnlyList<CourseSummaryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Courses.AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new CourseSummaryDto(c.Id, c.Title, c.UpdatedAt, c.Updater.Name ?? c.Updater.Email, c.LastExportedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<CourseDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var course = await _context.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return course is null ? null : ToDto(course);
    }

    public async Task<CourseDto> CreateAsync(CourseWriteRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var course = new Course
        {
            Id = Guid.NewGuid(),
            Title = ValidTitle(request.Title),
            Content = ValidContent(request.Content),
            CreatedBy = userId,
            UpdatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.Courses.Add(course);
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(course);
    }

    public async Task<CourseDto?> UpdateAsync(Guid id, CourseWriteRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null) return null;

        course.Title = ValidTitle(request.Title);
        course.Content = ValidContent(request.Content);
        course.UpdatedBy = userId;
        course.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(course);
    }

    public async Task<CourseDto?> MarkExportedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null) return null;

        course.LastExportedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(course);
    }

    public async Task<bool> DeleteAsync(Guid id, string? confirmTitle, CancellationToken cancellationToken = default)
    {
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null) return false;
        if (confirmTitle?.Trim() != course.Title)
            throw new CourseValidationException("Type the course title exactly to delete it");

        _context.Courses.Remove(course);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CourseMediaDto?> UploadMediaAsync(Guid id, string fileName, byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (!await _context.Courses.AnyAsync(c => c.Id == id, cancellationToken)) return null;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!MediaTypes.TryGetValue(ext, out var contentType))
            throw new CourseValidationException("That file type can't be used in a course");

        var url = await _assets.PutAsync($"courses/{id:N}/{Guid.NewGuid():N}{ext}", bytes, contentType, cancellationToken);
        return new CourseMediaDto(url);
    }

    public async Task<(byte[] Bytes, string ContentType)?> GetMediaAsync(Guid id, string url, CancellationToken cancellationToken = default)
    {
        var key = _assets.KeyFromUrl(url);
        if (key is null || !key.StartsWith($"courses/{id:N}/", StringComparison.Ordinal)) return null;
        return await _assets.GetAsync(key, cancellationToken);
    }

    public async Task<CourseMediaDto?> ImportMediaAsync(Guid id, string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new CourseValidationException("That isn't a web address");

        var http = _httpFactory.CreateClient(MediaDownloadClient);
        HttpResponseMessage? resp = null;
        for (var hop = 0; hop < 5; hop++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                throw new CourseValidationException("That isn't a web address");
            if (!await IsPublicHostAsync(uri.DnsSafeHost, cancellationToken))
                throw new CourseValidationException("That address isn't on the public internet");

            resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)resp.StatusCode is < 300 or >= 400 || resp.Headers.Location is null) break;
            uri = new Uri(uri, resp.Headers.Location);
            resp.Dispose();
            resp = null;
        }
        if (resp is null) throw new CourseValidationException("That link redirects too many times");

        using var _ = resp;
        if (!resp.IsSuccessStatusCode)
            throw new CourseValidationException($"That link returned an error ({(int)resp.StatusCode})");

        var mediaType = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            throw new CourseValidationException("That link is a web page, not a file. Download the file from the page and upload it instead.");
        if (resp.Content.Headers.ContentLength > MaxMediaBytes)
            throw new CourseValidationException("That file is over 90 MB");

        var fileName = Path.GetFileName(uri.LocalPath);
        if (!MediaTypes.ContainsKey(Path.GetExtension(fileName)))
        {
            var ext = MediaTypes.FirstOrDefault(m => m.Value.Equals(mediaType, StringComparison.OrdinalIgnoreCase)).Key;
            if (ext is null) throw new CourseValidationException("That link isn't an image, video, audio or font file");
            fileName = $"download{ext}";
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (ms.Length + read > MaxMediaBytes) throw new CourseValidationException("That file is over 90 MB");
            ms.Write(buffer, 0, read);
        }

        return await UploadMediaAsync(id, fileName, ms.ToArray(), cancellationToken);
    }

    private static async Task<bool> IsPublicHostAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = await System.Net.Dns.GetHostAddressesAsync(host, cancellationToken);
        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(System.Net.IPAddress address)
    {
        if (System.Net.IPAddress.IsLoopback(address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal);
        var b = address.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
    }

    private static string ValidTitle(string? title)
    {
        var trimmed = title?.Trim() ?? "";
        if (trimmed.Length == 0) throw new CourseValidationException("The course needs a title");
        if (trimmed.Length > MaxTitleLength) throw new CourseValidationException($"Titles are limited to {MaxTitleLength} characters");
        return trimmed;
    }

    private static string ValidContent(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) throw new CourseValidationException("Course content is missing");
        return content.GetRawText();
    }

    private static CourseDto ToDto(Course c)
    {
        using var doc = JsonDocument.Parse(c.Content);
        return new CourseDto(c.Id, c.Title, doc.RootElement.Clone(), c.UpdatedAt, c.LastExportedAt);
    }
}
