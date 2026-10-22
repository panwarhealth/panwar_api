using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public sealed record TsActivityFilter(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyCollection<Guid> ClientIds,
    IReadOnlyCollection<Guid> JobIds,
    IReadOnlyCollection<Guid> TaskIds,
    IReadOnlyCollection<Guid> PersonIds,
    IReadOnlyCollection<Guid> PerformedByIds,
    IReadOnlyCollection<TsActivityKind> Kinds);

public interface ITsActivityService
{
    Task<TsActivityListDto> ListAsync(TsViewer viewer, TsActivityFilter filter, CancellationToken cancellationToken = default);
}

public class TsActivityService : ITsActivityService
{
    private const int MaxRows = 1000;
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    private readonly AppDbContext _context;
    private readonly ITsAccessService _access;

    public TsActivityService(AppDbContext context, ITsAccessService access)
    {
        _context = context;
        _access = access;
    }

    public static TsActivity For(TsActivityKind kind, TsEntry entry, Guid performedById, decimal? hoursBefore = null, Guid? fromJobId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            At = DateTime.UtcNow,
            Kind = kind,
            EntryId = entry.Id,
            PersonId = entry.PersonId,
            PerformedById = performedById,
            Date = entry.Date,
            JobId = entry.JobId,
            FromJobId = fromJobId,
            TaskId = entry.TaskId,
            HoursBefore = hoursBefore,
            Hours = entry.Hours
        };

    public async Task<TsActivityListDto> ListAsync(TsViewer viewer, TsActivityFilter filter, CancellationToken cancellationToken = default)
    {
        var activity = _context.TsActivities.AsNoTracking();
        if (!viewer.IsAdmin)
        {
            var visible = _access.VisiblePeople(viewer, _context.TsPeople).Select(p => p.Id);
            activity = activity.Where(a => visible.Contains(a.PersonId) || a.PerformedById == viewer.PersonId);
        }
        if (filter.From is { } from)
        {
            var since = Utc(from);
            activity = activity.Where(a => a.At >= since);
        }
        if (filter.To is { } to)
        {
            var before = Utc(to.AddDays(1));
            activity = activity.Where(a => a.At < before);
        }
        if (filter.ClientIds.Count > 0) activity = activity.Where(a => filter.ClientIds.Contains(a.Job.ClientId));
        if (filter.JobIds.Count > 0) activity = activity.Where(a => filter.JobIds.Contains(a.JobId));
        if (filter.TaskIds.Count > 0) activity = activity.Where(a => filter.TaskIds.Contains(a.TaskId));
        if (filter.PersonIds.Count > 0) activity = activity.Where(a => filter.PersonIds.Contains(a.PersonId));
        if (filter.PerformedByIds.Count > 0) activity = activity.Where(a => filter.PerformedByIds.Contains(a.PerformedById));
        if (filter.Kinds.Count > 0) activity = activity.Where(a => filter.Kinds.Contains(a.Kind));

        var count = await activity.CountAsync(cancellationToken);
        var rows = await activity
            .OrderByDescending(a => a.At)
            .Take(MaxRows)
            .Select(a => new TsActivityDto(
                a.At, a.Kind.ToString(), a.Date, a.Job.Client.Name, a.Job.Code, a.Job.Title,
                a.FromJob == null ? null : a.FromJob.Code == "" ? a.FromJob.Title : "[" + a.FromJob.Code + "] " + a.FromJob.Title,
                a.Task.Name, a.HoursBefore, a.Hours,
                (a.Person.FirstName + " " + a.Person.LastName).Trim(),
                (a.PerformedBy.FirstName + " " + a.PerformedBy.LastName).Trim()))
            .ToListAsync(cancellationToken);
        return new TsActivityListDto(count, rows);
    }

    private static DateTime Utc(DateOnly date)
        => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), Sydney);
}
