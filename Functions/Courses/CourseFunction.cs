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
    private const int MaxUploadBytes = 90 * 1024 * 1024;

    private readonly ICourseService _courses;

    public CourseFunction(ICourseService courses)
    {
        _courses = courses;
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

    [Function("CourseMarkExported")]
    public Task<HttpResponseData> MarkExported(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "courses/{courseId:guid}/exported")] HttpRequestData req, FunctionContext context, Guid courseId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _courses.MarkExportedAsync(courseId, ct)));

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
            if (ms.Length > MaxUploadBytes) throw new CourseValidationException("Uploads are limited to 90 MB");

            return await req.JsonOrNotFoundAsync(await _courses.UploadMediaAsync(courseId, fileName, ms.ToArray(), ct));
        });

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
