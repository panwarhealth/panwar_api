using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Timesheets;

namespace Panwar.Api.Functions.Timesheets;

public class TsJobsFunction
{
    private readonly ITsViewerService _viewers;
    private readonly ITsJobService _jobs;

    public TsJobsFunction(ITsViewerService viewers, ITsJobService jobs)
    {
        _viewers = viewers;
        _jobs = jobs;
    }

    [Function("TsColorLabels")]
    public Task<HttpResponseData> ColorLabels(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/color-labels")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (_, ct) =>
            await TsHttp.Ok(req, new { labels = await _jobs.ColorLabelsAsync(ct) }));

    [Function("TsListJobs")]
    public Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/jobs")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.Ok(req, new { jobs = await _jobs.ListAsync(TsHttp.Enum(req, "status", TsJobStatus.Open), ct) }));

    [Function("TsGetJob")]
    public Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/jobs/{jobId:guid}")] HttpRequestData req, FunctionContext context, Guid jobId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.OkOrNotFound(req, await _jobs.GetAsync(jobId, TsHttp.Date(req, "from"), TsHttp.Date(req, "to"), ct)));

    [Function("TsNextJobCode")]
    public Task<HttpResponseData> NextCode(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/clients/{clientId:guid}/next-code")] HttpRequestData req, FunctionContext context, Guid clientId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Admin, async (_, ct) =>
            await TsHttp.Ok(req, await _jobs.NextCodeAsync(clientId, ct)));

    [Function("TsCreateJob")]
    public Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/jobs")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Admin, async (_, ct) =>
            await TsHttp.Ok(req, await _jobs.CreateAsync(await TsHttp.Body<TsJobWriteRequest>(req), ct), HttpStatusCode.Created));

    [Function("TsUpdateJob")]
    public Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/jobs/{jobId:guid}")] HttpRequestData req, FunctionContext context, Guid jobId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Admin, async (_, ct) =>
            await TsHttp.OkOrNotFound(req, await _jobs.UpdateAsync(jobId, await TsHttp.Body<TsJobWriteRequest>(req), ct)));

    [Function("TsArchiveJob")]
    public Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/jobs/{jobId:guid}/archived")] HttpRequestData req, FunctionContext context, Guid jobId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Admin, async (_, ct) =>
            await TsHttp.NoContentOrNotFound(req, await _jobs.SetArchivedAsync(jobId, (await TsHttp.Body<TsArchiveRequest>(req)).Archived, ct)));
}
