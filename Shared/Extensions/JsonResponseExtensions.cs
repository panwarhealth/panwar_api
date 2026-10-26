using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Shared.Extensions;

public class RequestValidationException(string message) : Exception(message);

public static class JsonResponseExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<HttpResponseData> RunAsEmployeeAsync(this HttpRequestData req, FunctionContext context,
        IReadOnlyCollection<string> roles, Func<Guid, CancellationToken, Task<HttpResponseData>> action)
    {
        if (req.GetUserType(context) != UserType.Employee || req.GetUserId(context) is not { } userId)
            return await req.CreateForbiddenResponseAsync();
        if (!roles.Any(r => req.HasRole(context, r)))
            return await req.CreateForbiddenResponseAsync();

        try
        {
            return await action(userId, context.CancellationToken);
        }
        catch (RequestValidationException ex)
        {
            return await req.JsonAsync(new { error = ex.Message }, HttpStatusCode.BadRequest);
        }
    }

    public static async Task<T> ReadJsonAsync<T>(this HttpRequestData req) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(req.Body, JsonOptions) ?? throw new RequestValidationException("Request body required");
        }
        catch (JsonException)
        {
            throw new RequestValidationException("Request body isn't valid JSON");
        }
    }

    public static async Task<HttpResponseData> JsonAsync<T>(this HttpRequestData req, T body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var resp = req.CreateResponse(status);
        await resp.WriteAsJsonAsync(body);
        return resp;
    }

    public static Task<HttpResponseData> JsonOrNotFoundAsync<T>(this HttpRequestData req, T? body, HttpStatusCode status = HttpStatusCode.OK) where T : class
        => body is null ? req.NotFoundJsonAsync() : req.JsonAsync(body, status);

    public static Task<HttpResponseData> NotFoundJsonAsync(this HttpRequestData req)
        => req.JsonAsync(new { error = "Not found" }, HttpStatusCode.NotFound);
}
