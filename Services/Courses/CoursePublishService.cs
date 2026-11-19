using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.ClinicalStudio;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Courses;

public interface ICoursePublishService
{
    Task<CourseClinicalStudioCreatedDto?> CreateLmsCourseAsync(Guid id, CourseClinicalStudioCreateRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<JsonElement?> StartAsync(Guid id, CoursePublishStartRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<JsonElement?> CompleteAsync(Guid id, CoursePublishCompleteRequest request, Guid userId, CancellationToken cancellationToken = default);
}

public class CoursePublishService : ICoursePublishService
{
    private static readonly string[] Difficulties = ["Beginner", "Intermediate", "Advanced"];

    private readonly AppDbContext _context;
    private readonly IClinicalStudioPublisher _publisher;

    public CoursePublishService(AppDbContext context, IClinicalStudioPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task<CourseClinicalStudioCreatedDto?> CreateLmsCourseAsync(Guid id, CourseClinicalStudioCreateRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) throw new CourseValidationException("Give the course a title");
        if (string.IsNullOrWhiteSpace(request.ShortDescription)) throw new CourseValidationException("Add a short description");
        if (string.IsNullOrWhiteSpace(request.LongDescription)) throw new CourseValidationException("Add a long description");
        if (!Difficulties.Contains(request.Difficulty)) throw new CourseValidationException("Pick a difficulty");

        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null) return null;

        var created = await _publisher.CreateCourseAsync(
            new ClinicalStudioNewCourse(request.Title.Trim(), request.ShortDescription.Trim(), request.LongDescription.Trim(), request.Difficulty),
            await NameAsync(userId, cancellationToken), cancellationToken);
        if (created.ValueKind != JsonValueKind.Object || !created.TryGetProperty("id", out var lmsId) || !lmsId.TryGetGuid(out var target))
            throw new CourseValidationException("Clinical Studio created the course but did not return its ID. Check Clinical Studio admin before trying again.");

        course.ClinicalStudioCourseId = target;
        await _context.SaveChangesAsync(cancellationToken);
        return new CourseClinicalStudioCreatedDto(target);
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
        if (request.Build < 1 || request.Build > course.BuildNumber) throw new CourseValidationException("That build number does not exist");

        var package = new ClinicalStudioPackage(request.UploadId, request.TotalFiles, course.Title, request.Build.ToString(), request.ConfirmCourseTitle);
        var published = await _publisher.CompleteUploadAsync(Target(course), package, await NameAsync(userId, cancellationToken), cancellationToken);

        int? version = published.ValueKind == JsonValueKind.Object && published.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var number) ? number : null;
        await _context.Courses.Where(c => c.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(c => c.PublishedBuild, request.Build).SetProperty(c => c.PublishedVersion, version).SetProperty(c => c.PublishedAt, DateTime.UtcNow), cancellationToken);
        return published;
    }

    private Task<Course?> FindAsync(Guid id, CancellationToken cancellationToken)
        => _context.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    private static Guid Target(Course course)
        => course.ClinicalStudioCourseId ?? throw new CourseValidationException("Set the Clinical Studio course ID first");

    private async Task<string> NameAsync(Guid userId, CancellationToken cancellationToken)
        => await _context.Users.Where(u => u.Id == userId).Select(u => u.Name ?? u.Email).FirstAsync(cancellationToken);
}
