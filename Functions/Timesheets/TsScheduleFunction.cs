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
    private readonly ITsScheduleExportService _export;

    public TsScheduleFunction(ITsViewerService viewers, ITsScheduleService schedule, ITsScheduleExportService export)
    {
        _viewers = viewers;
        _schedule = schedule;
        _export = export;
    }

    [Function("TsGetSchedule")]
    public Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/schedule")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
        {
            var weeks = int.TryParse(req.Query["weeks"], out var w) ? Math.Clamp(w, 1, MaxWeeks) : 3;
            return await TsHttp.Ok(req, await _schedule.GetAsync(TsHttp.RequiredDate(req, "weekStart"), weeks, ct));
        });

    [Function("TsExportSchedule")]
    public Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/schedule/export.csv")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
        {
            var request = new TsScheduleExportRequest(
                TsHttp.Enum(req, "kind", TsScheduleExportKind.Projects),
                TsHttp.Flag(req, "tasks"),
                TsHttp.Enum(req, "period", TsScheduleExportPeriod.Week),
                TsHttp.Flag(req, "rows"),
                TsHttp.RequiredDate(req, "from"),
                TsHttp.RequiredDate(req, "to"));
            return await TsHttp.File(req, await _export.CsvAsync(request, ct), "text/csv", "schedule-export.csv");
        });

    [Function("TsMoveBooking")]
    public Task<HttpResponseData> Move(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/bookings/{bookingId:guid}/dates")] HttpRequestData req, FunctionContext context, Guid bookingId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (viewer, ct) =>
        {
            var body = await TsHttp.Body<TsBookingMoveRequest>(req);
            return await TsHttp.OkOrNotFound(req, await _schedule.MoveAsync(bookingId, body.StartDate, body.EndDate, viewer.PersonId, ct));
        });

    [Function("TsCreateBooking")]
    public Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/bookings")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (viewer, ct) =>
            await TsHttp.Ok(req, await _schedule.CreateAsync(await TsHttp.Body<TsBookingWriteRequest>(req), viewer.PersonId, ct), HttpStatusCode.Created));

    [Function("TsUpdateBooking")]
    public Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/bookings/{bookingId:guid}")] HttpRequestData req, FunctionContext context, Guid bookingId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (viewer, ct) =>
            await TsHttp.OkOrNotFound(req, await _schedule.UpdateAsync(bookingId, await TsHttp.Body<TsBookingWriteRequest>(req), viewer.PersonId, ct)));

    [Function("TsDeleteBooking")]
    public Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ts/bookings/{bookingId:guid}")] HttpRequestData req, FunctionContext context, Guid bookingId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.NoContentOrNotFound(req, await _schedule.DeleteAsync(bookingId, TsHttp.Flag(req, "following"), ct)));
}
