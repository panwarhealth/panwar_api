using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Timesheets;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Timesheets;

public static class TsHttp
{
    public static async Task<HttpResponseData> Handle(HttpRequestData req, FunctionContext context, ITsViewerService viewers,
        TsLevel minimum, Func<TsViewer, CancellationToken, Task<HttpResponseData>> action)
    {
        if (req.GetUserType(context) != UserType.Employee || req.GetUserId(context) is not { } userId)
            return await req.CreateForbiddenResponseAsync();

        try
        {
            var viewer = await viewers.GetAsync(userId, req.GetRoles(context), context.CancellationToken);
            if (viewer.Level < minimum) return await req.CreateForbiddenResponseAsync();
            return await action(viewer, context.CancellationToken);
        }
        catch (RequestValidationException ex)
        {
            return await Ok(req, new { error = ex.Message }, HttpStatusCode.BadRequest);
        }
        catch (TsForbiddenException)
        {
            return await req.CreateForbiddenResponseAsync();
        }
    }

    public static Task<T> Body<T>(HttpRequestData req) where T : class => req.ReadJsonAsync<T>();

    public static Task<HttpResponseData> Ok<T>(HttpRequestData req, T body, HttpStatusCode status = HttpStatusCode.OK)
        => req.JsonAsync(body, status);

    public static Task<HttpResponseData> OkOrNotFound<T>(HttpRequestData req, T? body) where T : class
        => req.JsonOrNotFoundAsync(body);

    public static Task<HttpResponseData> NotFound(HttpRequestData req) => req.NotFoundJsonAsync();

    public static Task<HttpResponseData> NoContentOrNotFound(HttpRequestData req, bool found)
        => found ? Task.FromResult(req.CreateResponse(HttpStatusCode.NoContent)) : NotFound(req);

    public static async Task<HttpResponseData> File(HttpRequestData req, byte[] content, string contentType, string fileName)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", contentType);
        resp.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        await resp.WriteBytesAsync(content);
        return resp;
    }

    public static DateOnly? Date(HttpRequestData req, string name)
    {
        var value = req.Query[name];
        if (string.IsNullOrEmpty(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new TsValidationException($"{name} must be a date like 2026-10-21");
    }

    public static DateOnly RequiredDate(HttpRequestData req, string name)
        => Date(req, name) ?? throw new TsValidationException($"{name} is required");

    public static Guid? Id(HttpRequestData req, string name)
    {
        var value = req.Query[name];
        if (string.IsNullOrEmpty(value)) return null;
        return Guid.TryParse(value, out var id) ? id : throw new TsValidationException($"{name} isn't a valid id");
    }

    public static IReadOnlyCollection<Guid> Ids(HttpRequestData req, string name)
        => (req.Query[name] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => Guid.TryParse(v, out var id) ? id : throw new TsValidationException($"{name} has an invalid id"))
            .ToList();

    public static bool Flag(HttpRequestData req, string name) => string.Equals(req.Query[name], "true", StringComparison.OrdinalIgnoreCase);

    public static TEnum Enum<TEnum>(HttpRequestData req, string name, TEnum fallback) where TEnum : struct, Enum
    {
        var value = req.Query[name];
        if (string.IsNullOrEmpty(value)) return fallback;
        return System.Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : throw new TsValidationException($"{name} isn't valid");
    }
}
