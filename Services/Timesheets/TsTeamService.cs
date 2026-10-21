using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Timesheets;

public interface ITsTeamService
{
    Task<IReadOnlyList<TsTeamRowDto>> WeekAsync(TsViewer viewer, DateOnly weekStart, CancellationToken cancellationToken = default);
}

public class TsTeamService : ITsTeamService
{
    private readonly AppDbContext _context;
    private readonly IGraphService _graph;
    private readonly ITsAccessService _access;

    public TsTeamService(AppDbContext context, IGraphService graph, ITsAccessService access)
    {
        _context = context;
        _graph = graph;
        _access = access;
    }

    public async Task<IReadOnlyList<TsTeamRowDto>> WeekAsync(TsViewer viewer, DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        var weekEnd = weekStart.AddDays(6);
        var people = await _access.VisiblePeople(viewer, _context.TsPeople.AsNoTracking())
            .Include(p => p.CapacityChanges.Where(c => c.Date >= weekStart && c.Date <= weekEnd))
            .Where(p => !p.Archived && !p.IsPlaceholder)
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);

        var logged = await _context.TsEntries.AsNoTracking()
            .Where(e => e.Date >= weekStart && e.Date <= weekEnd)
            .GroupBy(e => new { e.PersonId, e.Job.IsLeave })
            .Select(g => new { g.Key.PersonId, g.Key.IsLeave, Hours = g.Sum(e => e.Hours) })
            .ToListAsync(cancellationToken);

        var roles = (await _graph.GetUsersWithRolesAsync(cancellationToken))
            .DistinctBy(u => u.Email.ToLowerInvariant())
            .ToDictionary(u => u.Email.ToLowerInvariant(), u => RoleName(u.Roles.Select(r => r.RoleValue).ToList()));

        var today = TsCalendar.Today;
        var dueBy = weekEnd < today ? weekEnd : today.AddDays(-1);

        return people.Select(p =>
        {
            var work = logged.FirstOrDefault(l => l.PersonId == p.Id && !l.IsLeave)?.Hours ?? 0;
            var leave = logged.FirstOrDefault(l => l.PersonId == p.Id && l.IsLeave)?.Hours ?? 0;
            var due = dueBy < weekStart ? 0 : TsCalendar.Capacity(p, weekStart, dueBy);
            return new TsTeamRowDto(
                p.Id, TsNames.Full(p), p.Title, p.Email is null ? null : roles.GetValueOrDefault(p.Email), p.IsContractor,
                work, TsCalendar.Capacity(p, weekStart, weekEnd), leave, work + leave < due);
        }).ToList();
    }

    private static string RoleName(IReadOnlyCollection<string> roles) => TsRoles.LevelFor(roles) switch
    {
        TsLevel.Admin => "Administrator",
        TsLevel.Manager => "Manager",
        _ => "Member"
    };
}
