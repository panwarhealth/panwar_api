using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly IImportProgress _progress;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ManageImportFunction> _logger;

    public ManageImportFunction(
        AppDbContext context,
        ICloudflareR2Service r2,
        IImportService import,
        IImportProgress progress,
        IServiceScopeFactory scopeFactory,
        ILogger<ManageImportFunction> logger)
    {
        _context = context;
        _r2 = r2;
        _import = import;
        _progress = progress;
        _scopeFactory = scopeFactory;
        _logger = logger;
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

        // The build can run for minutes (the AI reads note-heavy files), which is longer
        // than any HTTP request may stay open (Azure hard-caps around 230s). So it runs
        // as a background job; the frontend polls the progress endpoint for status + result.
        var jobId = data.JobId ?? Guid.NewGuid();
        var request = data with { JobId = jobId };
        var userId = req.GetUserId(context);
        _progress.Report(jobId, "Getting started...");
        _ = Task.Run(() => RunPreviewJobAsync(clientSlug, request, userId, jobId));

        var resp = req.CreateResponse(HttpStatusCode.Accepted);
        await resp.WriteAsJsonAsync(new { jobId });
        return resp;
    }

    // Runs outside the request: its own DI scope (the request's is disposed once the
    // 202 goes back) and no request cancellation token.
    private async Task RunPreviewJobAsync(string clientSlug, ImportPreviewRequest request, Guid? userId, Guid jobId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var import = scope.ServiceProvider.GetRequiredService<IImportService>();
            var preview = await import.BuildPreviewAsync(clientSlug, request, userId, CancellationToken.None);
            _progress.Complete(jobId, preview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import preview job {JobId} failed", jobId);
            _progress.Fail(jobId, ex is ImportConflictException ? ex.Message : "Something went wrong building the preview - try again.");
        }
    }

    // Polled by the frontend while the preview job runs: live status while running,
    // then the finished preview (or the error).
    [Function("ManageImportProgress")]
    public async Task<HttpResponseData> Progress(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/clients/{clientSlug}/import/progress/{jobId:guid}")] HttpRequestData req,
        FunctionContext context,
        string clientSlug,
        Guid jobId)
    {
        if (!CanManage(req, context)) return await req.CreateForbiddenResponseAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(_progress.Get(jobId) ?? new ImportJobState("unknown", null, null, null));
        return resp;
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
