using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;

namespace Panwar.Api.Services.Timesheets;

public interface ITsBudgetAlertService
{
    Task CheckAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default);
}

public class TsBudgetAlertService : ITsBudgetAlertService
{
    private readonly AppDbContext _context;
    private readonly IEmailService _email;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TsBudgetAlertService> _logger;

    public TsBudgetAlertService(AppDbContext context, IEmailService email, IConfiguration configuration, ILogger<TsBudgetAlertService> logger)
    {
        _context = context;
        _email = email;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task CheckAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default)
    {
        var ids = jobIds.Distinct().ToList();
        var jobs = await _context.TsJobs
            .Where(j => ids.Contains(j.Id) && j.AlertPercent != null)
            .Select(j => new
            {
                Job = j,
                ClientName = j.Client.Name,
                Budget = j.Tasks.Sum(t => t.BudgetHours),
                Spent = _context.TsEntries.Where(e => e.JobId == j.Id).Sum(e => (decimal?)e.Hours) ?? 0,
                Managers = j.People.Where(p => p.IsManager && p.Person.Email != null).Select(p => p.Person.Email!).ToList()
            })
            .ToListAsync(cancellationToken);

        foreach (var row in jobs)
        {
            var job = row.Job;
            if (row.Budget is not > 0) continue;

            var reached = row.Spent >= row.Budget.Value * job.AlertPercent!.Value / 100m;
            if (!reached)
            {
                job.AlertSentAt = null;
                continue;
            }
            if (job.AlertSentAt is not null) continue;

            var recipients = row.Managers.Concat(AlwaysNotify()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var subject = $"{job.Code} has used {job.AlertPercent}% of its budget";
            var body = Body(job.Id, job.Code, job.Title, row.ClientName, row.Spent, row.Budget.Value, job.AlertPercent.Value);
            try
            {
                foreach (var to in recipients) await _email.SendHtmlEmailAsync(to, subject, body, cancellationToken);
                job.AlertSentAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Budget alert for job {JobId} failed; it will be retried on the next time entry", job.Id);
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private IEnumerable<string> AlwaysNotify()
        => (_configuration["TIMESHEETS_ALERT_EMAILS"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string Body(Guid jobId, string code, string title, string client, decimal spent, decimal budget, int percent)
    {
        var url = $"{_configuration["EMPLOYEE_PORTAL_URL"]}/app/timesheets/jobs/{jobId}";
        string E(string s) => WebUtility.HtmlEncode(s);
        return $"""
            <div style="font-family:Arial,sans-serif;font-size:15px;color:#454646;line-height:1.5">
              <p><b>[{E(code)}] {E(title)}</b> ({E(client)}) has used {percent}% of its budget.</p>
              <p>{spent:0.00} of {budget:0.00} hours spent, {budget - spent:0.00} hours left.</p>
              <p><a href="{url}" style="color:#702f8f">Open the job</a></p>
            </div>
            """;
    }
}
