using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.CloudflareR2;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services.Import;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Admin;

public class ManageImportFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", // .xlsx
        "application/vnd.ms-excel",                                          // .xls
    };

    private readonly AppDbContext _context;
    private readonly ICloudflareR2Service _r2;
    private readonly IImportService _import;

    public ManageImportFunction(AppDbContext context, ICloudflareR2Service r2, IImportService import)
    {
        _context = context;
        _r2 = r2;
        _import = import;
    }

    [Function("ManageImportUploadUrl")]
    public async Task<HttpResponseData> UploadUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/import/upload-url")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        if (!await ClientExists(clientSlug, ct)) return await NotFound(req);

        var data = await ReadJson<ImportUploadUrlRequest>(req);
        if (data is null || string.IsNullOrWhiteSpace(data.FileName)) return await BadRequest(req, "File name is required");
        if (!AllowedContentTypes.Contains(data.ContentType))
            return await BadRequest(req, "Unsupported file type - upload an Excel (.xlsx or .xls) workbook");

        var (uploadUrl, objectKey) = await _r2.GenerateUploadUrlAsync(data.FileName.Trim(), data.ContentType, "imports", ct);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new ImportUploadUrlResponse(uploadUrl, objectKey));
        return resp;
    }

    [Function("ManageImportPreview")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/import/preview")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        if (!await ClientExists(clientSlug, ct)) return await NotFound(req);

        var data = await ReadJson<ImportPreviewRequest>(req);
        if (data is null || data.Files is null || data.Files.Count == 0) return await BadRequest(req, "At least one file is required");

        try
        {
            var preview = await _import.BuildPreviewAsync(clientSlug, data, ct);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(preview);
            return resp;
        }
        catch (ImportConflictException ex)
        {
            return await BadRequest(req, ex.Message);
        }
    }

    [Function("ManageImportCommit")]
    public async Task<HttpResponseData> Commit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/clients/{clientSlug}/import/commit")] HttpRequestData req,
        FunctionContext context,
        string clientSlug)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var ct = context.CancellationToken;
        if (!await ClientExists(clientSlug, ct)) return await NotFound(req);

        var data = await ReadJson<ImportCommitRequest>(req);
        if (data is null || data.Files is null || data.Files.Count == 0) return await BadRequest(req, "At least one file is required");

        try
        {
            var result = await _import.CommitAsync(clientSlug, data, req.GetUserId(context), ct);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(result);
            return resp;
        }
        catch (ImportConflictException ex)
        {
            var resp = req.CreateResponse(HttpStatusCode.Conflict);
            await resp.WriteAsJsonAsync(new { error = ex.Message });
            return resp;
        }
    }

    private async Task<bool> ClientExists(string clientSlug, CancellationToken ct)
        => await _context.Clients.AnyAsync(c => c.Slug == clientSlug, ct);

    private static bool CanManage(HttpRequestData req, FunctionContext context)
        => req.HasRole(context, "panwar-admin") || req.HasRole(context, "dashboard-editor");

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

    private static async Task<HttpResponseData> NotFound(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.NotFound);
        await resp.WriteAsJsonAsync(new { error = "Not found" });
        return resp;
    }
}
