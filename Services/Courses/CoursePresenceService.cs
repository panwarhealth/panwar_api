using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Courses;

public interface ICoursePresenceService
{
    Task<IReadOnlyList<CoursePresenceDto>?> BeatAsync(Guid courseId, Guid userId, string? pageId, CancellationToken cancellationToken = default);
}

public class CoursePresenceService : ICoursePresenceService
{
    private static readonly TimeSpan Away = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Forget = TimeSpan.FromHours(1);
    private const int MaxPageIdLength = 100;

    private readonly AppDbContext _context;

    public CoursePresenceService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CoursePresenceDto>?> BeatAsync(Guid courseId, Guid userId, string? pageId, CancellationToken cancellationToken = default)
    {
        if (!await _context.Courses.AnyAsync(c => c.Id == courseId, cancellationToken)) return null;
        var page = pageId is { Length: > 0 and <= MaxPageIdLength } ? pageId : null;
        var now = DateTime.UtcNow;

        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO panwar_portals.course_presence ("CourseId", "UserId", "PageId", "SeenAt")
            VALUES ({courseId}, {userId}, {page}, {now})
            ON CONFLICT ("CourseId", "UserId")
            DO UPDATE SET "PageId" = EXCLUDED."PageId", "SeenAt" = EXCLUDED."SeenAt"
            """, cancellationToken);

        var forget = now - Forget;
        await _context.CoursePresences
            .Where(p => p.CourseId == courseId && p.SeenAt < forget)
            .ExecuteDeleteAsync(cancellationToken);

        var away = now - Away;
        return await _context.CoursePresences.AsNoTracking()
            .Where(p => p.CourseId == courseId && p.UserId != userId && p.SeenAt > away)
            .OrderBy(p => p.User.Name ?? p.User.Email)
            .Select(p => new CoursePresenceDto(p.UserId, p.User.Name ?? p.User.Email, p.PageId))
            .ToListAsync(cancellationToken);
    }
}
