using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Timesheets;

namespace Panwar.Api.Functions.Timesheets;

public class TsReportsFunction
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ITsViewerService _viewers;
    private readonly ITsReportService _reports;
    private readonly ITsReportExporter _exporter;
    private readonly ITsSavedReportService _saved;

    public TsReportsFunction(ITsViewerService viewers, ITsReportService reports, ITsReportExporter exporter, ITsSavedReportService saved)
    {
        _viewers = viewers;
        _reports = reports;
        _exporter = exporter;
        _saved = saved;
    }

    [Function("TsReportSummary")]
    public Task<HttpResponseData> Summary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/reports/summary")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.Ok(req, await _reports.SummaryAsync(viewer, Filter(req), TsHttp.Enum(req, "groupBy", TsGroupBy.Client), ct)));

    [Function("TsReportDetailed")]
    public Task<HttpResponseData> Detailed(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/reports/detailed")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.Ok(req, await _reports.DetailedAsync(viewer, Filter(req), TsHttp.Enum(req, "groupBy", TsDetailedGroupBy.Date), ct)));

    [Function("TsReportOptions")]
    public Task<HttpResponseData> Options(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/reports/options")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.Ok(req, await _reports.OptionsAsync(viewer, ct)));

    [Function("TsReportExport")]
    public Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/reports/export.{format:regex(^(csv|xlsx)$)}")] HttpRequestData req, FunctionContext context, string format)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
        {
            var filter = Filter(req);
            var rows = await _reports.DetailedRowsAsync(viewer, filter, ct);
            var name = $"time_report_from{filter.From:yyyy-MM-dd}to{filter.To:yyyy-MM-dd}.{format}";
            return format == "csv"
                ? await TsHttp.File(req, _exporter.ToCsv(rows), "text/csv; charset=utf-8", name)
                : await TsHttp.File(req, _exporter.ToXlsx(rows), XlsxType, name);
        });

    [Function("TsListSavedReports")]
    public Task<HttpResponseData> ListSaved(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/saved-reports")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.Ok(req, new { reports = await _saved.ListAsync(viewer, ct) }));

    [Function("TsCreateSavedReport")]
    public Task<HttpResponseData> CreateSaved(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/saved-reports")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.Ok(req, await _saved.CreateAsync(viewer, await TsHttp.Body<TsSavedReportWriteRequest>(req), ct), HttpStatusCode.Created));

    [Function("TsDeleteSavedReport")]
    public Task<HttpResponseData> DeleteSaved(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ts/saved-reports/{reportId:guid}")] HttpRequestData req, FunctionContext context, Guid reportId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
            await TsHttp.NoContentOrNotFound(req, await _saved.DeleteAsync(viewer, reportId, ct)));

    private static TsReportFilter Filter(HttpRequestData req) => new(
        TsHttp.Date(req, "from"),
        TsHttp.Date(req, "to"),
        TsHttp.Ids(req, "clientIds"),
        TsHttp.Ids(req, "jobIds"),
        TsHttp.Ids(req, "taskIds"),
        TsHttp.Ids(req, "personIds"),
        (req.Query["roles"] ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        TsHttp.Flag(req, "openOnly"));
}
