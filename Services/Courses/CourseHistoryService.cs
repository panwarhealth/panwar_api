using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Courses;

public interface ICourseHistoryService
{
    Task SnapshotAsync(Course course, bool force, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CourseVersionDto>?> ListAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<CourseVersion?> GetAsync(Guid courseId, Guid versionId, CancellationToken cancellationToken = default);
}

public class CourseHistoryService : ICourseHistoryService
{
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KeepAll = TimeSpan.FromDays(7);

    private readonly AppDbContext _context;

    public CourseHistoryService(AppDbContext context)
    {
        _context = context;
    }

    public async Task SnapshotAsync(Course course, bool force, CancellationToken cancellationToken = default)
    {
        var latest = await _context.CourseVersions.AsNoTracking()
            .Where(v => v.CourseId == course.Id)
            .OrderByDescending(v => v.SavedAt)
            .Select(v => (DateTime?)v.SavedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest == course.UpdatedAt) return;
        if (!force && latest is not null && course.UpdatedAt - latest < Gap) return;

        _context.CourseVersions.Add(new CourseVersion
        {
            Id = Guid.NewGuid(),
            CourseId = course.Id,
            Title = course.Title,
            Content = course.Content,
            SavedBy = course.UpdatedBy,
            SavedAt = course.UpdatedAt,
        });
        await ThinAsync(course.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<CourseVersionDto>?> ListAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        if (!await _context.Courses.AnyAsync(c => c.Id == courseId, cancellationToken)) return null;
        return await _context.CourseVersions.AsNoTracking()
            .Where(v => v.CourseId == courseId)
            .OrderByDescending(v => v.SavedAt)
            .Select(v => new CourseVersionDto(v.Id, v.SavedAt, v.Saver.Name ?? v.Saver.Email, v.Title))
            .ToListAsync(cancellationToken);
    }

    public Task<CourseVersion?> GetAsync(Guid courseId, Guid versionId, CancellationToken cancellationToken = default)
        => _context.CourseVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.CourseId == courseId && v.Id == versionId, cancellationToken);

    private async Task ThinAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - KeepAll;
        var old = await _context.CourseVersions.AsNoTracking()
            .Where(v => v.CourseId == courseId && v.SavedAt < cutoff)
            .Select(v => new { v.Id, v.SavedAt })
            .ToListAsync(cancellationToken);
        var drop = old
            .GroupBy(v => v.SavedAt.Date)
            .SelectMany(g => g.OrderByDescending(v => v.SavedAt).Skip(1))
            .Select(v => v.Id)
            .ToList();
        if (drop.Count == 0) return;
        await _context.CourseVersions.Where(v => drop.Contains(v.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
