using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Timesheets;

namespace Panwar.Api.Functions.Timesheets;

public class TsSetupFunction
{
    private readonly ITsViewerService _viewers;
    private readonly ITsClientService _clients;
    private readonly ITsTaskService _tasks;

    public TsSetupFunction(ITsViewerService viewers, ITsClientService clients, ITsTaskService tasks)
    {
        _viewers = viewers;
        _clients = clients;
        _tasks = tasks;
    }

    [Function("TsListClients")]
    public Task<HttpResponseData> ListClients(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/clients")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.Ok(req, new { clients = await _clients.ListAsync(ct) }));

    [Function("TsCreateClient")]
    public Task<HttpResponseData> CreateClient(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/clients")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.Ok(req, await _clients.CreateAsync(await TsHttp.Body<TsClientWriteRequest>(req), ct), HttpStatusCode.Created));

    [Function("TsUpdateClient")]
    public Task<HttpResponseData> UpdateClient(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/clients/{clientId:guid}")] HttpRequestData req, FunctionContext context, Guid clientId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.OkOrNotFound(req, await _clients.UpdateAsync(clientId, await TsHttp.Body<TsClientWriteRequest>(req), ct)));

    [Function("TsListTasks")]
    public Task<HttpResponseData> ListTasks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ts/tasks")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.Ok(req, new { tasks = await _tasks.ListAsync(ct) }));

    [Function("TsCreateTask")]
    public Task<HttpResponseData> CreateTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ts/tasks")] HttpRequestData req, FunctionContext context)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.Ok(req, await _tasks.CreateAsync(await TsHttp.Body<TsTaskWriteRequest>(req), ct), HttpStatusCode.Created));

    [Function("TsUpdateTask")]
    public Task<HttpResponseData> UpdateTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ts/tasks/{taskId:guid}")] HttpRequestData req, FunctionContext context, Guid taskId)
        => TsHttp.Handle(req, context, _viewers, TsLevel.Manager, async (_, ct) =>
            await TsHttp.OkOrNotFound(req, await _tasks.UpdateAsync(taskId, await TsHttp.Body<TsTaskWriteRequest>(req), ct)));
}
