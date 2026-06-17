using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Shared.Extensions;

namespace Panwar.Api.Functions.Tracking;

public class TrackViewFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDbContext _context;

    public TrackViewFunction(AppDbContext context)
    {
        _context = context;
    }

    [Function("TrackView")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "track/view")] HttpRequestData req,
        FunctionContext context)
    {
        var userId = req.GetUserId(context);
        if (userId is null) return await req.CreateUnauthorizedResponseAsync();

        var ct = context.CancellationToken;
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var data = JsonSerializer.Deserialize<TrackViewRequest>(body, JsonOptions);
        if (data is null || string.IsNullOrWhiteSpace(data.Token))
            return req.CreateResponse(HttpStatusCode.NoContent);

        var invite = await _context.ReportInvites.FirstOrDefaultAsync(i => i.Token == data.Token, ct);
        if (invite is null) return req.CreateResponse(HttpStatusCode.NoContent);

        var hasAccess = invite.RecipientUserId == userId.Value
            || await _context.UserClients.AnyAsync(uc => uc.ClientId == invite.ClientId && uc.UserId == userId.Value, ct);
        if (!hasAccess) return req.CreateResponse(HttpStatusCode.NoContent);

        var ua = req.Headers.TryGetValues("User-Agent", out var uaVals) ? uaVals.FirstOrDefault() : null;

        _context.InviteEvents.Add(new InviteEvent
        {
            Id = Guid.NewGuid(),
            InviteId = invite.Id,
            Type = "view",
            At = DateTime.UtcNow,
            UserAgent = ua,
            IsBot = false,
        });

        invite.ViewCount += 1;
        invite.ViewedAt ??= DateTime.UtcNow;
        invite.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}
