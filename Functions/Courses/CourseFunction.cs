using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Courses;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Courses;

public class CourseFunction
{
    public const string CourseEditorRole = "course-editor";
    private static readonly string[] Roles = [CourseEditorRole, "panwar-admin"];
    private readonly ICourseService _courses;
    private readonly ICourseAssistantService _assistant;
    private readonly ICourseHistoryService _history;
    private readonly ICoursePresenceService _presence;
    private readonly ICoursePublishService _publish;

    public CourseFunction(
        ICourseService courses,
        ICourseAssistantService assistant,
        ICourseHistoryService history,
        ICoursePresenceService presence,
        ICoursePublishService publish)
    {
        _courses = courses;
        _assistant = assistant;
        _history = history;
        _presence = presence;
        _publish = publish;
    }

    [Function("CourseList")]
    public Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "courses")] HttpRequestData req, FunctionContext context)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonAsync(new { courses = await _courses.ListAsync(ct) }));

    [Function("CourseGet")]
    public Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "courses/{courseId:guid}")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _courses.GetAsync(courseId, ct)));

    [Function("CourseCreate")]
    public Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses")] HttpRequestData req, FunctionContext context)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
            await req.JsonAsync(await _courses.CreateAsync(await req.ReadJsonAsync<CourseWriteRequest>(), userId, ct), HttpStatusCode.Created));

    [Function("CourseUpdate")]
    public Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "courses/{courseId:guid}")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
            await req.JsonOrNotFoundAsync(await _courses.UpdateAsync(courseId, await req.ReadJsonAsync<CourseWriteRequest>(), userId, ct)));

    [Function("CourseDelete")]
    public Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "courses/{courseId:guid}")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await _courses.DeleteAsync(courseId, System.Web.HttpUtility.ParseQueryString(req.Url.Query)["title"], ct) ?req.CreateResponse(HttpStatusCode.NoContent) : await req.NotFoundJsonAsync());

    [Function("CourseVersions")]
    public Task<HttpResponseData> Versions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "courses/{courseId:guid}/versions")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
        {
            var versions = await _history.ListAsync(courseId, ct);
            return versions is null ? await req.NotFoundJsonAsync() : await req.JsonAsync(new { versions });
        });

    [Function("CourseRestoreVersion")]
    public Task<HttpResponseData> RestoreVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/versions/{versionId:guid}/restore")] HttpRequestData req, FunctionContext context, Guid courseId, Guid versionId)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
            await req.JsonOrNotFoundAsync(await _courses.RestoreAsync(courseId, versionId, userId, ct)));

    [Function("CoursePresence")]
    public Task<HttpResponseData> Presence(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/presence")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
        {
            var body = await req.ReadJsonAsync<CoursePresenceRequest>();
            var people = await _presence.BeatAsync(courseId, userId, body.PageId, ct);
            return people is null ? await req.NotFoundJsonAsync() : await req.JsonAsync(new { people });
        });

    [Function("CourseMarkExported")]
    public Task<HttpResponseData> MarkExported(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/exported")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _courses.MarkExportedAsync(courseId, ct)));

    [Function("CourseNextBuild")]
    public Task<HttpResponseData> NextBuild(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/build")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _courses.NextBuildAsync(courseId, ct)));

    [Function("CourseCreateClinicalStudioCourse")]
    public Task<HttpResponseData> CreateClinicalStudioCourse(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/clinical-studio")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
            await req.JsonOrNotFoundAsync(await _publish.CreateLmsCourseAsync(courseId, await req.ReadJsonAsync<CourseClinicalStudioCreateRequest>(), userId, ct), System.Net.HttpStatusCode.Created));

    [Function("CourseSetClinicalStudioCourse")]
    public Task<HttpResponseData> SetClinicalStudioCourse(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "courses/{courseId:guid}/clinical-studio")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await req.JsonOrNotFoundAsync(await _courses.SetClinicalStudioCourseAsync(courseId, (await req.ReadJsonAsync<CourseClinicalStudioRequest>()).CourseId, ct)));

    [Function("CoursePublishStart")]
    public Task<HttpResponseData> PublishStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/publish")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
            await _publish.StartAsync(courseId, await req.ReadJsonAsync<CoursePublishStartRequest>(), userId, ct) is { } upload
                ? await req.JsonAsync(upload)
                : await req.NotFoundJsonAsync());

    [Function("CoursePublishComplete")]
    public Task<HttpResponseData> PublishComplete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/publish/complete")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
            await _publish.CompleteAsync(courseId, await req.ReadJsonAsync<CoursePublishCompleteRequest>(), userId, ct) is { } published
                ? await req.JsonAsync(published)
                : await req.NotFoundJsonAsync());

    [Function("CourseUploadMedia")]
    public Task<HttpResponseData> UploadMedia(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "courses/{courseId:guid}/media")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
        {
            var fileName = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["filename"];
            if (string.IsNullOrWhiteSpace(fileName)) throw new CourseValidationException("filename is required");

            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms, ct);
            if (ms.Length == 0) throw new CourseValidationException("The file is empty");
            if (ms.Length > CourseService.MaxMediaBytes) throw new CourseValidationException("Uploads are limited to 90 MB");

            return await req.JsonOrNotFoundAsync(await _courses.UploadMediaAsync(courseId, fileName, ms.ToArray(), ct));
        });

    [Function("CourseImportMedia")]
    public Task<HttpResponseData> ImportMedia(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/media/import")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await req.JsonOrNotFoundAsync(await _courses.ImportMediaAsync(courseId, (await req.ReadJsonAsync<CourseMediaImportRequest>()).Url, ct)));

    [Function("CourseAssistant")]
    public Task<HttpResponseData> Assistant(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/assistant")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await _assistant.StartTurnAsync(courseId, await req.ReadJsonAsync<CourseAssistantRequest>(), ct) is { } jobId
                ? await req.JsonAsync(new { jobId }, HttpStatusCode.Accepted)
                : await req.NotFoundJsonAsync());

    [Function("CourseAssistantTurn")]
    public Task<HttpResponseData> AssistantTurn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "courses/{courseId:guid}/assistant/{jobId:guid}")] HttpRequestData req, FunctionContext context, Guid courseId, Guid jobId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, _) =>
            await req.JsonAsync(await _assistant.GetTurnAsync(jobId) ?? new CourseAssistantJobState("unknown", null, null, null)));

    [Function("CourseGetMedia")]
    public Task<HttpResponseData> GetMedia(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "courses/{courseId:guid}/media")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
        {
            var url = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["url"];
            if (string.IsNullOrWhiteSpace(url)) throw new CourseValidationException("url is required");
            if (await _courses.GetMediaAsync(courseId, url, ct) is not { } media) return await req.NotFoundJsonAsync();

            var resp = req.CreateResponse(HttpStatusCode.OK);
            resp.Headers.Add("Content-Type", media.ContentType);
            await resp.WriteBytesAsync(media.Bytes);
            return resp;
        });
}
