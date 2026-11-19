using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.ClinicalStudio;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Courses;

public interface ICoursePublishService
{
    Task<JsonElement?> StartAsync(Guid id, CoursePublishStartRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<JsonElement?> CompleteAsync(Guid id, CoursePublishCompleteRequest request, Guid userId, CancellationToken cancellationToken = default);
}

public class CoursePublishService : ICoursePublishService
{
    private readonly AppDbContext _context;
    private readonly IClinicalStudioPublisher _publisher;

    public CoursePublishService(AppDbContext context, IClinicalStudioPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task<JsonElement?> StartAsync(Guid id, CoursePublishStartRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        if (request.FilePaths is not { Length: > 0 }) throw new CourseValidationException("There are no files to publish");
        if (await FindAsync(id, cancellationToken) is not { } course) return null;

        return await _publisher.RequestUploadAsync(Target(course), request.FilePaths, await NameAsync(userId, cancellationToken), cancellationToken);
    }

    public async Task<JsonElement?> CompleteAsync(Guid id, CoursePublishCompleteRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UploadId)) throw new CourseValidationException("uploadId is required");
        if (await FindAsync(id, cancellationToken) is not { } course) return null;

        var package = new ClinicalStudioPackage(request.UploadId, request.TotalFiles, course.Title, course.UpdatedAt.ToString("o"), request.ConfirmCourseTitle);
        return await _publisher.CompleteUploadAsync(Target(course), package, await NameAsync(userId, cancellationToken), cancellationToken);
    }

    private Task<Course?> FindAsync(Guid id, CancellationToken cancellationToken)
        => _context.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    private static Guid Target(Course course)
        => course.ClinicalStudioCourseId ?? throw new CourseValidationException("Set the Clinical Studio course ID first");

    private async Task<string> NameAsync(Guid userId, CancellationToken cancellationToken)
        => await _context.Users.Where(u => u.Id == userId).Select(u => u.Name ?? u.Email).FirstAsync(cancellationToken);
}
