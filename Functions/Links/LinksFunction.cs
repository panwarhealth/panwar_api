using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Links;

public class LinksFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILinkService _linkService;

    public LinksFunction(ILinkService linkService)
    {
        _linkService = linkService;
    }

    [Function("ListLinks")]
    public async Task<HttpResponseData> ListLinks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "links")] HttpRequestData req,
        FunctionContext context)
    {
        if (GetEmployeeId(req, context) is null) return await req.CreateForbiddenResponseAsync();

        var links = await _linkService.ListLinksAsync(context.CancellationToken);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { links });
        return resp;
    }

    [Function("ListJobClients")]
    public async Task<HttpResponseData> ListJobClients(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "job-clients")] HttpRequestData req,
        FunctionContext context)
    {
        if (GetEmployeeId(req, context) is null) return await req.CreateForbiddenResponseAsync();

        var clients = await _linkService.ListJobClientsAsync(context.CancellationToken);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { clients });
        return resp;
    }

    [Function("CreateLink")]
    public async Task<HttpResponseData> CreateLink(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "links")] HttpRequestData req,
        FunctionContext context)
    {
        if (GetEmployeeId(req, context) is not { } userId) return await req.CreateForbiddenResponseAsync();

        var data = await ReadJson<TrackedLinkWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        try
        {
            var link = await _linkService.CreateLinkAsync(data, userId, context.CancellationToken);
            var resp = req.CreateResponse(HttpStatusCode.Created);
            await resp.WriteAsJsonAsync(link);
            return resp;
        }
        catch (LinkValidationException ex)
        {
            return await BadRequest(req, ex.Message);
        }
    }

    [Function("SetLinkContent")]
    public async Task<HttpResponseData> SetLinkContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "links/{linkId}")] HttpRequestData req,
        FunctionContext context,
        string linkId)
    {
        if (GetEmployeeId(req, context) is null) return await req.CreateForbiddenResponseAsync();
        if (!Guid.TryParse(linkId, out var id)) return await BadRequest(req, "Invalid link id");

        var data = await ReadJson<TrackedLinkContentRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        try
        {
            var link = await _linkService.SetContentAsync(id, data.Content, context.CancellationToken);
            if (link is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Not found" });
                return notFound;
            }

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(link);
            return resp;
        }
        catch (LinkValidationException ex)
        {
            return await BadRequest(req, ex.Message);
        }
    }

    [Function("ListQrCodes")]
    public async Task<HttpResponseData> ListQrCodes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "qr-codes")] HttpRequestData req,
        FunctionContext context)
    {
        if (GetEmployeeId(req, context) is null) return await req.CreateForbiddenResponseAsync();

        var codes = await _linkService.ListQrCodesAsync(context.CancellationToken);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { codes });
        return resp;
    }

    [Function("SaveQrCode")]
    public async Task<HttpResponseData> SaveQrCode(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "qr-codes")] HttpRequestData req,
        FunctionContext context)
    {
        if (GetEmployeeId(req, context) is not { } userId) return await req.CreateForbiddenResponseAsync();

        var data = await ReadJson<QrCodeWriteRequest>(req);
        if (data is null) return await BadRequest(req, "Request body required");

        try
        {
            var code = await _linkService.SaveQrCodeAsync(data, userId, context.CancellationToken);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(code);
            return resp;
        }
        catch (LinkValidationException ex)
        {
            return await BadRequest(req, ex.Message);
        }
    }

    private static Guid? GetEmployeeId(HttpRequestData req, FunctionContext context)
        => req.GetUserType(context) == UserType.Employee ? req.GetUserId(context) : null;

    private static async Task<T?> ReadJson<T>(HttpRequestData req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var resp = req.CreateResponse(HttpStatusCode.BadRequest);
        await resp.WriteAsJsonAsync(new { error = message });
        return resp;
    }
}
