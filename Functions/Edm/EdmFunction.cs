using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Edm;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Edm;

/// <summary>Staff endpoints for the eDM Mailer. Employees holding the mailer (or admin) role only.</summary>
public class EdmFunction
{
    public const string MailerRole = "mailer";
    private static readonly string[] Roles = [MailerRole, "panwar-admin"];
    private const int MaxUploadBytes = 25 * 1024 * 1024;

    private readonly IEdmListService _lists;
    private readonly IEdmCampaignService _campaigns;
    private readonly IEdmSendService _send;
    private readonly IEdmReportService _reports;
    private readonly IEdmSyncService _sync;

    public EdmFunction(IEdmListService lists, IEdmCampaignService campaigns, IEdmSendService send,
        IEdmReportService reports, IEdmSyncService sync)
    {
        _lists = lists;
        _campaigns = campaigns;
        _send = send;
        _reports = reports;
        _sync = sync;
    }

    // ---- senders ----

    [Function("EdmListSenders")]
    public Task<HttpResponseData> ListSenders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/senders")] HttpRequestData req, FunctionContext context)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonAsync(new { senders = await _lists.ListSendersAsync(ct) }));

    // Senders come from the ACS setup (seeded by migration); staff can only change their branding.
    [Function("EdmUpdateSender")]
    public Task<HttpResponseData> UpdateSender(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "edm/senders/{senderId:guid}")] HttpRequestData req, FunctionContext context, Guid senderId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await req.JsonOrNotFoundAsync(await _lists.UpdateSenderBrandingAsync(senderId, await req.ReadJsonAsync<EdmSenderBrandingRequest>(), ct)));

    // ---- lists ----

    [Function("EdmListLists")]
    public Task<HttpResponseData> ListLists(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/lists")] HttpRequestData req, FunctionContext context)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonAsync(new { lists = await _lists.ListListsAsync(ct) }));

    [Function("EdmGetList")]
    public Task<HttpResponseData> GetList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/lists/{listId:guid}")] HttpRequestData req, FunctionContext context, Guid listId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _lists.GetListAsync(listId, ct)));

    [Function("EdmCreateList")]
    public Task<HttpResponseData> CreateList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/lists")] HttpRequestData req, FunctionContext context)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
            await req.JsonAsync(await _lists.CreateListAsync(await req.ReadJsonAsync<EdmListWriteRequest>(), userId, ct), HttpStatusCode.Created));

    [Function("EdmUpdateList")]
    public Task<HttpResponseData> UpdateList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "edm/lists/{listId:guid}")] HttpRequestData req, FunctionContext context, Guid listId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await req.JsonOrNotFoundAsync(await _lists.UpdateListAsync(listId, await req.ReadJsonAsync<EdmListWriteRequest>(), ct)));

    [Function("EdmDeleteList")]
    public Task<HttpResponseData> DeleteList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "edm/lists/{listId:guid}")] HttpRequestData req, FunctionContext context, Guid listId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await _lists.DeleteListAsync(listId, ct) ? req.CreateResponse(HttpStatusCode.NoContent) : await req.NotFoundJsonAsync());

    [Function("EdmSyncList")]
    public Task<HttpResponseData> SyncList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/lists/{listId:guid}/sync")] HttpRequestData req, FunctionContext context, Guid listId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _sync.SyncListAsync(listId, ct)));

    [Function("EdmListContacts")]
    public Task<HttpResponseData> ListContacts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/lists/{listId:guid}/contacts")] HttpRequestData req, FunctionContext context, Guid listId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
        {
            var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var page = int.TryParse(q["page"], out var p) ? p : 1;
            return await req.JsonOrNotFoundAsync(await _lists.ListContactsAsync(listId, q["search"], q["status"], page, ct));
        });

    [Function("EdmAddContact")]
    public Task<HttpResponseData> AddContact(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/lists/{listId:guid}/contacts")] HttpRequestData req, FunctionContext context, Guid listId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await req.JsonOrNotFoundAsync(await _lists.AddContactAsync(listId, await req.ReadJsonAsync<EdmContactWriteRequest>(), ct), HttpStatusCode.Created));

    [Function("EdmSetContactStatus")]
    public Task<HttpResponseData> SetContactStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "edm/lists/{listId:guid}/contacts/{contactId:guid}")] HttpRequestData req,
        FunctionContext context, Guid listId, Guid contactId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await req.JsonOrNotFoundAsync(await _lists.SetContactStatusAsync(listId, contactId, (await req.ReadJsonAsync<EdmContactStatusRequest>()).Status, ct)));

    [Function("EdmImportContacts")]
    public Task<HttpResponseData> ImportContacts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/lists/{listId:guid}/import")] HttpRequestData req, FunctionContext context, Guid listId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await req.JsonOrNotFoundAsync(await _lists.ImportAsync(listId, await req.ReadJsonAsync<EdmImportRequest>(), ct)));

    // ---- campaigns ----

    [Function("EdmListCampaigns")]
    public Task<HttpResponseData> ListCampaigns(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/campaigns")] HttpRequestData req, FunctionContext context)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonAsync(new { campaigns = await _campaigns.ListAsync(ct) }));

    [Function("EdmGetCampaign")]
    public Task<HttpResponseData> GetCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/campaigns/{campaignId:guid}")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _campaigns.GetAsync(campaignId, ct)));

    [Function("EdmCreateCampaign")]
    public Task<HttpResponseData> CreateCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/campaigns")] HttpRequestData req, FunctionContext context)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
            await req.JsonAsync(await _campaigns.CreateAsync(await req.ReadJsonAsync<EdmCampaignCreateRequest>(), userId, ct), HttpStatusCode.Created));

    [Function("EdmUpdateCampaign")]
    public Task<HttpResponseData> UpdateCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "edm/campaigns/{campaignId:guid}")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await req.JsonOrNotFoundAsync(await _campaigns.UpdateAsync(campaignId, await req.ReadJsonAsync<EdmCampaignUpdateRequest>(), ct)));

    [Function("EdmDeleteCampaign")]
    public Task<HttpResponseData> DeleteCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "edm/campaigns/{campaignId:guid}")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
            await _campaigns.DeleteAsync(campaignId, ct) ? req.CreateResponse(HttpStatusCode.NoContent) : await req.NotFoundJsonAsync());

    /// <summary>Raw body is the .zip or .html; ?filename= carries the original name.</summary>
    [Function("EdmUploadContent")]
    public Task<HttpResponseData> UploadContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "edm/campaigns/{campaignId:guid}/content")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
        {
            var fileName = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["filename"];
            if (string.IsNullOrWhiteSpace(fileName)) throw new EdmValidationException("filename is required");

            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms, ct);
            if (ms.Length == 0) throw new EdmValidationException("The file is empty");
            if (ms.Length > MaxUploadBytes) throw new EdmValidationException("Uploads are limited to 25 MB");

            return await req.JsonOrNotFoundAsync(await _campaigns.UploadContentAsync(campaignId, ms.ToArray(), fileName, ct));
        });

    [Function("EdmPreviewCampaign")]
    public Task<HttpResponseData> PreviewCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/campaigns/{campaignId:guid}/preview")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
        {
            var contact = Guid.TryParse(System.Web.HttpUtility.ParseQueryString(req.Url.Query)["contactId"], out var c) ? c : (Guid?)null;
            return await req.JsonOrNotFoundAsync(await _campaigns.PreviewAsync(campaignId, contact, ct));
        });

    [Function("EdmCampaignAudience")]
    public Task<HttpResponseData> Audience(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/campaigns/{campaignId:guid}/audience")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _send.AudienceAsync(campaignId, ct)));

    [Function("EdmTestSend")]
    public Task<HttpResponseData> TestSend(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/campaigns/{campaignId:guid}/test")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
        {
            var data = await req.ReadJsonAsync<EdmTestSendRequest>();
            return await req.JsonOrNotFoundAsync(await _send.TestSendAsync(campaignId, data.Emails ?? [], userId, ct));
        });

    [Function("EdmSendCampaign")]
    public Task<HttpResponseData> SendCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/campaigns/{campaignId:guid}/send")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) =>
            await req.JsonOrNotFoundAsync(await _send.SendAsync(campaignId, await req.ReadJsonAsync<EdmSendRequest>(), userId, ct)));

    [Function("EdmCancelCampaign")]
    public Task<HttpResponseData> CancelCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/campaigns/{campaignId:guid}/cancel")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _send.CancelAsync(campaignId, ct)));

    [Function("EdmDuplicateCampaign")]
    public Task<HttpResponseData> DuplicateCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edm/campaigns/{campaignId:guid}/duplicate")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (userId, ct) => await req.JsonOrNotFoundAsync(await _campaigns.DuplicateAsync(campaignId, userId, ct), HttpStatusCode.Created));

    [Function("EdmCampaignReport")]
    public Task<HttpResponseData> Report(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/campaigns/{campaignId:guid}/report")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) => await req.JsonOrNotFoundAsync(await _reports.ReportAsync(campaignId, ct)));

    [Function("EdmCampaignReportCsv")]
    public Task<HttpResponseData> ReportCsv(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "edm/campaigns/{campaignId:guid}/report.csv")] HttpRequestData req, FunctionContext context, Guid campaignId)
        => req.RunAsEmployeeAsync(context, Roles, async (_, ct) =>
        {
            var csv = await _reports.ReportCsvAsync(campaignId, ct);
            if (csv is null) return await req.NotFoundJsonAsync();
            var resp = req.CreateResponse(HttpStatusCode.OK);
            resp.Headers.Add("Content-Type", "text/csv; charset=utf-8");
            resp.Headers.Add("Content-Disposition", $"attachment; filename=\"edm-report-{campaignId:N}.csv\"");
            // BOM so Excel opens accented names correctly.
            await resp.WriteBytesAsync(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());
            return resp;
        });

}
