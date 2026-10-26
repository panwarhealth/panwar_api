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
    Task<CourseMediaDto?> UploadMediaAsync(Guid id, string fileName, byte[] bytes, CancellationToken cancellationToken = default);
    Task<(byte[] Bytes, string ContentType)?> GetMediaAsync(Guid id, string url, CancellationToken cancellationToken = default);
}

public class CourseValidationException(string message) : RequestValidationException(message);

public class CourseService : ICourseService
{
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
    };

    private readonly AppDbContext _context;
    private readonly IPublicAssetStore _assets;

    public CourseService(AppDbContext context, IPublicAssetStore assets)
    {
        _context = context;
        _assets = assets;
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
