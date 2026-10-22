using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Timesheets;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Timesheets;

public class TsCalendarFeedFunction
{
    private readonly ITsViewerService _viewers;
    private readonly ITsCalendarFeedService _feeds;
    private readonly IConfiguration _configuration;

    public TsCalendarFeedFunction(ITsViewerService viewers, ITsCalendarFeedService feeds, IConfiguration configuration)
    {
        _viewers = viewers;
        _feeds = feeds;
        _configuration = configuration;
    }

    [Function("TsCalendarFeedLink")]
    public Task<HttpResponseData> Link(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/me/calendar-feed")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Member, async (viewer, ct) =>
        {
            var token = await _feeds.TokenAsync(viewer.PersonId, ct);
            return await TsHttp.Ok(req, new { url = $"{req.ApiBase(_configuration)}/api/ts/calendar/{token}.ics" });
        });

    [Function("TsCalendarFeed")]
    public async Task<HttpResponseData> Feed(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/calendar/{token}")] HttpRequestData req, string token)
    {
        var ics = await _feeds.IcsAsync(Path.GetFileNameWithoutExtension(token));
        if (ics is null) return req.CreateResponse(HttpStatusCode.NotFound);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "text/calendar; charset=utf-8");
        await resp.WriteStringAsync(ics);
        return resp;
    }
}
