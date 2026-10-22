using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Timesheets;

namespace Panwar.Api.Functions.Timesheets;

public class TsTimeFunction
{
    private readonly ITsViewerService _viewers;
    private readonly ITsTimeService _time;

    public TsTimeFunction(ITsViewerService viewers, ITsTimeService time)
    {
        _viewers = viewers;
        _time = time;
    }

    [Function("TsMe")]
    public Task<HttpResponseData> Me(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/me")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, (viewer, _) =>
            TsHttp.Ok(req, new TsViewerDto(viewer.PersonId, viewer.Name, viewer.Level.ToString())));

    [Function("TsGetTime")]
    public Task<HttpResponseData> GetTime(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/time")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.Ok(req, await _time.GetAsync(viewer, TsHttp.Id(req, "personId") ?? viewer.PersonId,
                TsHttp.RequiredDate(req, "from"), TsHttp.RequiredDate(req, "to"), ct)));

    [Function("TsTeammates")]
    public Task<HttpResponseData> Teammates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/teammates")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.Ok(req, new { people = await _time.TeammatesAsync(viewer, ct) }));

    [Function("TsPickableJobs")]
    public Task<HttpResponseData> PickableJobs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/pickable-jobs")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.Ok(req, new { jobs = await _time.PickableJobsAsync(viewer, TsHttp.Id(req, "personId") ?? viewer.PersonId, ct) }));

    [Function("TsCreateEntry")]
    public Task<HttpResponseData> CreateEntry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/entries")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.Ok(req, await _time.CreateAsync(viewer, await TsHttp.Body<TsEntryWriteRequest>(req), ct), System.Net.HttpStatusCode.Created));

    [Function("TsUpdateEntry")]
    public Task<HttpResponseData> UpdateEntry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/entries/{entryId:guid}")] HttpRequestData req, FunctionContext context, Guid entryId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.OkOrNotFound(req, await _time.UpdateAsync(viewer, entryId, await TsHttp.Body<TsEntryWriteRequest>(req), ct)));

    [Function("TsDeleteEntry")]
    public Task<HttpResponseData> DeleteEntry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ts/entries/{entryId:guid}")] HttpRequestData req, FunctionContext context, Guid entryId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.NoContentOrNotFound(req, await _time.DeleteAsync(viewer, entryId, ct)));

    [Function("TsStartTimer")]
    public Task<HttpResponseData> StartTimer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/entries/{entryId:guid}/start")] HttpRequestData req, FunctionContext context, Guid entryId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.OkOrNotFound(req, await _time.StartAsync(viewer, entryId, ct)));

    [Function("TsStopTimer")]
    public Task<HttpResponseData> StopTimer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/entries/{entryId:guid}/stop")] HttpRequestData req, FunctionContext context, Guid entryId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.OkOrNotFound(req, await _time.StopAsync(viewer, entryId, (await TsHttp.Body<TsStopTimerRequest>(req)).Notes, ct)));

    [Function("TsSetStar")]
    public Task<HttpResponseData> SetStar(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/stars")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
        {
            await _time.SetStarAsync(viewer, await TsHttp.Body<TsStarRequest>(req), ct);
            return req.CreateResponse(System.Net.HttpStatusCode.NoContent);
        });

    [Function("TsDeleteRow")]
    public Task<HttpResponseData> DeleteRow(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ts/rows")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
        {
            var jobId = TsHttp.Id(req, "jobId") ?? throw new TsValidationException("jobId is required");
            var taskId = TsHttp.Id(req, "taskId") ?? throw new TsValidationException("taskId is required");
            await _time.DeleteRowAsync(viewer, TsHttp.RequiredDate(req, "weekStart"), jobId, taskId, ct);
            return req.CreateResponse(System.Net.HttpStatusCode.NoContent);
        });
}
