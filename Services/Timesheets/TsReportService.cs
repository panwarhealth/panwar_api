using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public enum TsGroupBy
{
    Client,
    Job,
    Task,
    Person
}

public sealed record TsReportFilter(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyCollection<Guid> ClientIds,
    IReadOnlyCollection<Guid> JobIds,
    IReadOnlyCollection<Guid> TaskIds,
    IReadOnlyCollection<Guid> PersonIds,
    bool OpenOnly);

public interface ITsReportService
{
    Task<TsSummaryDto> SummaryAsync(TsViewer viewer, TsReportFilter filter, TsGroupBy groupBy, CancellationToken cancellationToken = default);
    Task<TsDetailedDto> DetailedAsync(TsViewer viewer, TsReportFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TsDetailedRow>> DetailedRowsAsync(TsViewer viewer, TsReportFilter filter, CancellationToken cancellationToken = default);
    Task<TsReportOptionsDto> OptionsAsync(TsViewer viewer, CancellationToken cancellationToken = default);
}

public class TsReportService : ITsReportService
{
    private const int MaxShownRows = 500;

    private readonly AppDbContext _context;
    private readonly ITsAccessService _access;

    public TsReportService(AppDbContext context, ITsAccessService access)
    {
        _context = context;
        _access = access;
    }

    public async Task<TsSummaryDto> SummaryAsync(TsViewer viewer, TsReportFilter filter, TsGroupBy groupBy, CancellationToken cancellationToken = default)
    {
        var entries = Filtered(viewer, filter);
        var rows = groupBy switch
        {
            TsGroupBy.Client => await entries.GroupBy(e => new { e.Job.ClientId, e.Job.Client.Name })
                .Select(g => new TsSummaryRowDto(g.Key.ClientId, g.Key.Name, g.Sum(e => e.Hours))).ToListAsync(cancellationToken),
            TsGroupBy.Job => await entries.GroupBy(e => new { e.JobId, e.Job.Code, e.Job.Title })
                .Select(g => new TsSummaryRowDto(g.Key.JobId, "[" + g.Key.Code + "] " + g.Key.Title, g.Sum(e => e.Hours))).ToListAsync(cancellationToken),
            TsGroupBy.Task => await entries.GroupBy(e => new { e.TaskId, e.Task.Name })
                .Select(g => new TsSummaryRowDto(g.Key.TaskId, g.Key.Name, g.Sum(e => e.Hours))).ToListAsync(cancellationToken),
            _ => await entries.GroupBy(e => new { e.PersonId, e.Person.FirstName, e.Person.LastName })
                .Select(g => new TsSummaryRowDto(g.Key.PersonId, (g.Key.FirstName + " " + g.Key.LastName).Trim(), g.Sum(e => e.Hours))).ToListAsync(cancellationToken)
        };
        var sorted = rows.OrderBy(r => r.Name).ToList();
        return new TsSummaryDto(sorted.Sum(r => r.Hours), sorted);
    }

    public async Task<TsDetailedDto> DetailedAsync(TsViewer viewer, TsReportFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await DetailedRowsAsync(viewer, filter, cancellationToken);
        return new TsDetailedDto(
            rows.Sum(r => r.Hours),
            rows.Count,
            TsDetailedReport.Columns.Select(c => c.Header).ToList(),
            rows.Take(MaxShownRows).Select(r => (IReadOnlyList<string>)TsDetailedReport.Columns.Select(c => TsDetailedReport.Display(c.Value(r))).ToList()).ToList());
    }

    public async Task<IReadOnlyList<TsDetailedRow>> DetailedRowsAsync(TsViewer viewer, TsReportFilter filter, CancellationToken cancellationToken = default)
        => await Filtered(viewer, filter)
            .OrderBy(e => e.Date).ThenBy(e => e.Person.FirstName).ThenBy(e => e.CreatedAt)
            .Select(e => new TsDetailedRow(
                e.Date, e.Job.Client.Name, e.Job.Title, e.Job.Code, e.Task.Name, e.Notes, e.Hours,
                e.Person.FirstName, e.Person.LastName, e.Person.Title, e.Person.IsContractor))
            .ToListAsync(cancellationToken);

    public async Task<TsReportOptionsDto> OptionsAsync(TsViewer viewer, CancellationToken cancellationToken = default)
    {
        var entries = Filtered(viewer, new TsReportFilter(null, null, [], [], [], [], false));
        var clients = await entries.Select(e => new { e.Job.ClientId, e.Job.Client.Name }).Distinct()
            .OrderBy(x => x.Name).Select(x => new TsNamedDto(x.ClientId, x.Name)).ToListAsync(cancellationToken);
        var jobs = await entries.Select(e => new { e.JobId, e.Job.Code, e.Job.Title }).Distinct()
            .OrderBy(x => x.Code).Select(x => new TsNamedDto(x.JobId, "[" + x.Code + "] " + x.Title)).ToListAsync(cancellationToken);
        var tasks = await entries.Select(e => new { e.TaskId, e.Task.Name }).Distinct()
            .OrderBy(x => x.Name).Select(x => new TsNamedDto(x.TaskId, x.Name)).ToListAsync(cancellationToken);
        var people = await entries.Select(e => new { e.PersonId, e.Person.FirstName, e.Person.LastName }).Distinct()
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Select(x => new TsNamedDto(x.PersonId, (x.FirstName + " " + x.LastName).Trim())).ToListAsync(cancellationToken);
        return new TsReportOptionsDto(clients, jobs, tasks, people);
    }

    private IQueryable<TsEntry> Filtered(TsViewer viewer, TsReportFilter filter)
    {
        var entries = _access.Visible(viewer, _context.TsEntries.AsNoTracking());

        if (filter.From is { } from) entries = entries.Where(e => e.Date >= from);
        if (filter.To is { } to) entries = entries.Where(e => e.Date <= to);
        if (filter.ClientIds.Count > 0) entries = entries.Where(e => filter.ClientIds.Contains(e.Job.ClientId));
        if (filter.JobIds.Count > 0) entries = entries.Where(e => filter.JobIds.Contains(e.JobId));
        if (filter.TaskIds.Count > 0) entries = entries.Where(e => filter.TaskIds.Contains(e.TaskId));
        if (filter.PersonIds.Count > 0) entries = entries.Where(e => filter.PersonIds.Contains(e.PersonId));
        if (filter.OpenOnly) entries = entries.Where(e => !e.Job.Archived);
        return entries;
    }
}
