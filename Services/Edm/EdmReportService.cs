using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Edm;

public interface IEdmReportService
{
    Task<EdmReportDto?> ReportAsync(Guid id, CancellationToken ct = default);
    Task<string?> ReportCsvAsync(Guid id, CancellationToken ct = default);
}

public class EdmReportService : IEdmReportService
{
    private readonly AppDbContext _context;
    private readonly EdmCampaignQueries _queries;

    public EdmReportService(AppDbContext context, EdmCampaignQueries queries)
    {
        _context = context;
        _queries = queries;
    }

    public async Task<EdmReportDto?> ReportAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await _queries.LoadAsync(id, tracking: false, ct);
        if (campaign is null) return null;

        var rows = await _context.EdmRecipients.AsNoTracking()
            .Where(r => r.CampaignId == id
                && (r.Status == EdmRecipientStatus.Bounced || r.Status == EdmRecipientStatus.Failed || r.UnsubscribedAt != null))
            .OrderBy(r => r.Email)
            .ToListAsync(ct);

        static EdmReportRow Row(EdmRecipient r, string status, DateTime? at) => new(r.Email, r.FirstName, r.LastName, status, at, r.Error);
        return new EdmReportDto(
            await _queries.ToDtoAsync(campaign, ct),
            rows.Where(r => r.Status == EdmRecipientStatus.Bounced).Select(r => Row(r, "Bounced", r.DeliveredAt ?? r.SentAt)).ToList(),
            rows.Where(r => r.UnsubscribedAt != null).Select(r => Row(r, "Unsubscribed", r.UnsubscribedAt)).ToList(),
            rows.Where(r => r.Status == EdmRecipientStatus.Failed).Select(r => Row(r, "Failed", r.SentAt)).ToList());
    }

    public async Task<string?> ReportCsvAsync(Guid id, CancellationToken ct = default)
    {
        if (!await _context.EdmCampaigns.AnyAsync(c => c.Id == id, ct)) return null;

        var rows = await _context.EdmRecipients.AsNoTracking()
            .Where(r => r.CampaignId == id)
            .OrderBy(r => r.Email)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Email,First name,Last name,Status,Sent (Sydney),Delivered (Sydney),First opened (Sydney),Opens,Unsubscribed (Sydney),Detail");
        foreach (var r in rows)
        {
            sb.AppendJoin(',',
                Csv(r.Email), Csv(r.FirstName), Csv(r.LastName), r.Status.ToString(),
                Csv(EdmTime.FormatSydney(r.SentAt)), Csv(EdmTime.FormatSydney(r.DeliveredAt)),
                Csv(EdmTime.FormatSydney(r.FirstOpenedAt)), r.OpenCount.ToString(CultureInfo.InvariantCulture),
                Csv(EdmTime.FormatSydney(r.UnsubscribedAt)), Csv(r.Error));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Neutralise spreadsheet formulas, then quote anything with separators.
        if (s[0] is '=' or '+' or '-' or '@') s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
