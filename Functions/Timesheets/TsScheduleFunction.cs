using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Timesheets;

namespace Panwar.Api.Functions.Timesheets;

public class TsScheduleFunction
{
    private const int MaxWeeks = 12;

    private readonly ITsViewerService _viewers;
    private readonly ITsScheduleService _schedule;

    public TsScheduleFunction(ITsViewerService viewers, ITsScheduleService schedule)
    {
        _viewers = viewers;
        _schedule = schedule;
    }

    [Function("TsGetSchedule")]
    public Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/schedule")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
        {
            var weeks = int.TryParse(req.Query["weeks"], out var w) ? Math.Clamp(w, 1, MaxWeeks) : 3;
            return await TsHttp.Ok(req, await _schedule.GetAsync(TsHttp.RequiredDate(req, "weekStart"), weeks, ct));
        });

    [Function("TsCreateBooking")]
    public Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/bookings")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.Ok(req, await _schedule.CreateAsync(await TsHttp.Body<TsBookingWriteRequest>(req), ct), HttpStatusCode.Created));

    [Function("TsUpdateBooking")]
    public Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/bookings/{bookingId:guid}")] HttpRequestData req, FunctionContext context, Guid bookingId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.OkOrNotFound(req, await _schedule.UpdateAsync(bookingId, await TsHttp.Body<TsBookingWriteRequest>(req), ct)));

    [Function("TsDeleteBooking")]
    public Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ts/bookings/{bookingId:guid}")] HttpRequestData req, FunctionContext context, Guid bookingId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.NoContentOrNotFound(req, await _schedule.DeleteAsync(bookingId, ct)));
}
