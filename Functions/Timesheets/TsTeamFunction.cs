using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Timesheets;

namespace Panwar.Api.Functions.Timesheets;

public class TsTeamFunction
{
    private readonly ITsViewerService _viewers;
    private readonly ITsPeopleService _people;
    private readonly ITsTeamService _team;

    public TsTeamFunction(ITsViewerService viewers, ITsPeopleService people, ITsTeamService team)
    {
        _viewers = viewers;
        _people = people;
        _team = team;
    }

    [Function("TsListPeople")]
    public Task<HttpResponseData> ListPeople(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/people")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.Ok(req, new { people = await _people.ListAsync(ct) }));

    [Function("TsCreatePerson")]
    public Task<HttpResponseData> CreatePerson(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/people")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Admin, async (_, ct) =>
            await TsHttp.Ok(req, await _people.CreateAsync(await TsHttp.Body<TsPersonWriteRequest>(req), ct), HttpStatusCode.Created));

    [Function("TsUpdatePerson")]
    public Task<HttpResponseData> UpdatePerson(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/people/{personId:guid}")] HttpRequestData req, FunctionContext context, Guid personId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Admin, async (_, ct) =>
            await TsHttp.OkOrNotFound(req, await _people.UpdateAsync(personId, await TsHttp.Body<TsPersonWriteRequest>(req), ct)));

    [Function("TsSetCapacity")]
    public Task<HttpResponseData> SetCapacity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/people/{personId:guid}/capacity")] HttpRequestData req, FunctionContext context, Guid personId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Admin, async (_, ct) =>
            await TsHttp.OkOrNotFound(req, await _people.SetCapacityAsync(personId, await TsHttp.Body<TsCapacityWriteRequest>(req), ct)));

    [Function("TsTeamWeek")]
    public Task<HttpResponseData> TeamWeek(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/team")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (viewer, ct) =>
            await TsHttp.Ok(req, new { people = await _team.WeekAsync(viewer, TsCalendar.WeekStart(TsHttp.RequiredDate(req, "weekStart")), ct) }));
}
