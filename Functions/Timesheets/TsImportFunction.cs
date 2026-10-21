using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Timesheets;

namespace Panwar.Api.Functions.Timesheets;

public class TsImportFunction
{
    private readonly ITsViewerService _viewers;
    private readonly ITsImportService _import;

    public TsImportFunction(ITsViewerService viewers, ITsImportService import)
    {
        _viewers = viewers;
        _import = import;
    }

    [Function("TsImportHarvest")]
    public Task<HttpResponseData> Harvest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/import/harvest")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Admin, async (_, ct) =>
            await TsHttp.Ok(req, await _import.ImportHarvestAsync((await TsHttp.Body<TsHarvestImportRequest>(req)).LeaveJobCodes, ct)));

    [Function("TsImportForecast")]
    public Task<HttpResponseData> Forecast(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/import/forecast")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Admin, async (_, ct) =>
            await TsHttp.Ok(req, await _import.ImportForecastAsync((await TsHttp.Body<TsForecastImportRequest>(req)).TimeOffJobCode, ct)));
}
