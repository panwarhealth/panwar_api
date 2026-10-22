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

public enum TsDetailedGroupBy
{
    Date,
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
    IReadOnlyCollection<string> Roles,
    bool OpenOnly);

public interface ITsReportService
{
    Task<TsSummaryDto> SummaryAsync(TsViewer viewer, TsReportFilter filter, TsGroupBy groupBy, CancellationToken cancellationToken = default);
    Task<TsDetailedDto> DetailedAsync(TsViewer viewer, TsReportFilter filter, TsDetailedGroupBy groupBy, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TsDetailedRow>> DetailedRowsAsync(TsViewer viewer, TsReportFilter filter, CancellationToken cancellationToken = default);
    Task<TsReportOptionsDto> OptionsAsync(TsViewer viewer, CancellationToken cancellationToken = default);
}

public class TsReportService : ITsReportService
{
    private const int MaxShownRows = 1000;

    private readonly AppDbContext _context;
    private readonly ITsAccessService _access;

    public TsReportService(AppDbContext context, ITsAccessService access)
    {
        _context = context;
        _access = access;
    }

    public async Task<TsSummaryDto> SummaryAsync(TsViewer viewer, TsReportFilter filter, TsGroupBy groupBy, CancellationToken cancellationToken = default)
    {
        var entries = await FilteredAsync(viewer, filter, cancellationToken);
        var rows = groupBy switch
        {
            TsGroupBy.Client => await entries.GroupBy(e => new { e.Job.ClientId, e.Job.Client.Name })
                .Select(g => new TsSummaryRowDto(g.Key.ClientId, g.Key.Name, g.Sum(e => e.Hours))).ToListAsync(cancellationToken),
            TsGroupBy.Job => await entries.GroupBy(e => new { e.JobId, e.Job.Code, e.Job.Title })
                .Select(g => new TsSummaryRowDto(g.Key.JobId, g.Key.Code == "" ? g.Key.Title : "[" + g.Key.Code + "] " + g.Key.Title, g.Sum(e => e.Hours))).ToListAsync(cancellationToken),
            TsGroupBy.Task => await entries.GroupBy(e => new { e.TaskId, e.Task.Name })
                .Select(g => new TsSummaryRowDto(g.Key.TaskId, g.Key.Name, g.Sum(e => e.Hours))).ToListAsync(cancellationToken),
            _ => await entries.GroupBy(e => new { e.PersonId, e.Person.FirstName, e.Person.LastName })
                .Select(g => new TsSummaryRowDto(g.Key.PersonId, (g.Key.FirstName + " " + g.Key.LastName).Trim(), g.Sum(e => e.Hours))).ToListAsync(cancellationToken)
        };
        var sorted = rows.OrderBy(r => r.Name).ToList();
        return new TsSummaryDto(sorted.Sum(r => r.Hours), sorted);
    }

    public async Task<TsDetailedDto> DetailedAsync(TsViewer viewer, TsReportFilter filter, TsDetailedGroupBy groupBy, CancellationToken cancellationToken = default)
    {
        var rows = await (await FilteredAsync(viewer, filter, cancellationToken))
            .OrderBy(e => e.Date).ThenBy(e => e.Person.FirstName).ThenBy(e => e.CreatedAt)
            .Select(e => new TsDetailedEntryDto(
                e.Id, e.PersonId, e.Date, e.JobId, e.Job.Code, e.Job.Title, e.Job.Client.Name,
                e.TaskId, e.Task.Name, e.Hours, e.Notes, e.TimerStartedAt,
                (e.Person.FirstName + " " + e.Person.LastName).Trim(), e.Person.Title))
            .ToListAsync(cancellationToken);

        Func<TsDetailedEntryDto, string> label = groupBy switch
        {
            TsDetailedGroupBy.Client => r => r.ClientName,
            TsDetailedGroupBy.Job => r => r.JobCode == "" ? r.JobTitle : $"[{r.JobCode}] {r.JobTitle}",
            TsDetailedGroupBy.Task => r => r.TaskName,
            TsDetailedGroupBy.Person => r => r.PersonName,
            _ => r => r.Date.ToString("yyyy-MM-dd")
        };
        var shown = 0;
        var groups = new List<TsDetailedGroupDto>();
        foreach (var group in rows.GroupBy(label).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var take = group.Take(Math.Max(0, MaxShownRows - shown)).ToList();
            shown += take.Count;
            groups.Add(new TsDetailedGroupDto(group.Key, group.Sum(r => r.Hours), take));
        }
        return new TsDetailedDto(rows.Sum(r => r.Hours), rows.Count, shown, groups);
    }

    public async Task<IReadOnlyList<TsDetailedRow>> DetailedRowsAsync(TsViewer viewer, TsReportFilter filter, CancellationToken cancellationToken = default)
        => await (await FilteredAsync(viewer, filter, cancellationToken))
            .OrderBy(e => e.Date).ThenBy(e => e.Person.FirstName).ThenBy(e => e.CreatedAt)
            .Select(e => new TsDetailedRow(
                e.Date, e.Job.Client.Name, e.Job.Title, e.Job.Code, e.Task.Name, e.Notes, e.Hours,
                e.Person.FirstName, e.Person.LastName, e.Person.Title, e.Person.IsContractor))
            .ToListAsync(cancellationToken);

    public async Task<TsReportOptionsDto> OptionsAsync(TsViewer viewer, CancellationToken cancellationToken = default)
    {
        var entries = await FilteredAsync(viewer, new TsReportFilter(null, null, [], [], [], [], [], false), cancellationToken);
        var clients = await entries.Select(e => new { e.Job.ClientId, e.Job.Client.Name, e.Job.Client.Archived }).Distinct()
            .OrderBy(x => x.Name).Select(x => new TsReportOptionDto(x.ClientId, x.Name, x.Archived)).ToListAsync(cancellationToken);
        var jobs = (await entries.Select(e => new { e.JobId, e.Job.Code, e.Job.Title, e.Job.Archived }).Distinct().ToListAsync(cancellationToken))
            .OrderBy(x => x.Code == "").ThenBy(x => x.Code).ThenBy(x => x.Title)
            .Select(x => new TsReportOptionDto(x.JobId, x.Code == "" ? x.Title : $"[{x.Code}] {x.Title}", x.Archived)).ToList();
        var tasks = await entries.Select(e => new { e.TaskId, e.Task.Name, e.Task.Archived }).Distinct()
            .OrderBy(x => x.Name).Select(x => new TsReportOptionDto(x.TaskId, x.Name, x.Archived)).ToListAsync(cancellationToken);
        var people = await entries.Select(e => new { e.PersonId, e.Person.FirstName, e.Person.LastName, e.Person.Archived, e.Person.Title }).Distinct()
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ToListAsync(cancellationToken);
        var roles = people.SelectMany(p => RolesOf(p.Title)).Distinct().Order().ToList();
        return new TsReportOptionsDto(clients, jobs, tasks,
            people.Select(x => new TsReportOptionDto(x.PersonId, $"{x.FirstName} {x.LastName}".Trim(), x.Archived)).ToList(), roles);
    }

    private static IEnumerable<string> RolesOf(string? title)
        => (title ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<IQueryable<TsEntry>> FilteredAsync(TsViewer viewer, TsReportFilter filter, CancellationToken cancellationToken)
    {
        var entries = _access.Visible(viewer, _context.TsEntries.AsNoTracking());
        if (filter.Roles.Count > 0)
        {
            var titled = await _context.TsPeople.AsNoTracking().Where(p => p.Title != null).Select(p => new { p.Id, p.Title }).ToListAsync(cancellationToken);
            var withRole = titled.Where(p => RolesOf(p.Title).Any(filter.Roles.Contains)).Select(p => p.Id).ToList();
            entries = entries.Where(e => withRole.Contains(e.PersonId));
        }

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
