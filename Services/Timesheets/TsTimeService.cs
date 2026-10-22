using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public interface ITsTimeService
{
    Task<TsTimeDto> GetAsync(TsViewer viewer, Guid personId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TsPickJobDto>> PickableJobsAsync(TsViewer viewer, Guid personId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TsNamedDto>> TeammatesAsync(TsViewer viewer, CancellationToken cancellationToken = default);
    Task<TsEntryDto> CreateAsync(TsViewer viewer, TsEntryWriteRequest data, CancellationToken cancellationToken = default);
    Task<TsEntryDto?> UpdateAsync(TsViewer viewer, Guid entryId, TsEntryWriteRequest data, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(TsViewer viewer, Guid entryId, CancellationToken cancellationToken = default);
    Task<TsEntryDto?> StartAsync(TsViewer viewer, Guid entryId, CancellationToken cancellationToken = default);
    Task<TsEntryDto?> StopAsync(TsViewer viewer, Guid entryId, string notes, CancellationToken cancellationToken = default);
    Task SetStarAsync(TsViewer viewer, TsStarRequest data, CancellationToken cancellationToken = default);
    Task DeleteRowAsync(TsViewer viewer, DateOnly weekStart, Guid jobId, Guid taskId, CancellationToken cancellationToken = default);
}

public class TsTimeService : ITsTimeService
{
    private const decimal MaxHours = 24;
    private const int MaxNotes = 4000;

    private static readonly Expression<Func<TsEntry, TsEntryDto>> ToDto = e => new TsEntryDto(
        e.Id, e.PersonId, e.Date, e.JobId, e.Job.Code, e.Job.Title, e.Job.Client.Name,
        e.TaskId, e.Task.Name, e.Hours, e.Notes, e.TimerStartedAt);

    private readonly AppDbContext _context;
    private readonly ITsBudgetAlertService _alerts;
    private readonly ITsAccessService _access;

    public TsTimeService(AppDbContext context, ITsBudgetAlertService alerts, ITsAccessService access)
    {
        _context = context;
        _alerts = alerts;
        _access = access;
    }

    public async Task<TsTimeDto> GetAsync(TsViewer viewer, Guid personId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (!await _access.CanSeePersonAsync(viewer, personId, cancellationToken)) throw new TsForbiddenException();

        var entries = await _context.TsEntries.AsNoTracking()
            .Where(e => e.PersonId == personId && e.Date >= from && e.Date <= to)
            .OrderBy(e => e.Date).ThenBy(e => e.CreatedAt)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        var stars = await _context.TsStars.AsNoTracking()
            .Where(s => s.PersonId == personId && !s.Job.Archived)
            .OrderBy(s => s.Job.Client.Name).ThenBy(s => s.Job.Code).ThenBy(s => s.Task.Name)
            .Select(s => new TsStarDto(s.JobId, s.Job.Code, s.Job.Title, s.Job.Client.Name, s.TaskId, s.Task.Name))
            .ToListAsync(cancellationToken);

        return new TsTimeDto(entries, stars);
    }

    public async Task<IReadOnlyList<TsNamedDto>> TeammatesAsync(TsViewer viewer, CancellationToken cancellationToken = default)
        => await _access.VisiblePeople(viewer, _context.TsPeople.AsNoTracking().Where(p => !p.Archived && !p.IsPlaceholder))
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName)
            .Select(p => new TsNamedDto(p.Id, (p.FirstName + " " + p.LastName).Trim()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TsPickJobDto>> PickableJobsAsync(TsViewer viewer, Guid personId, CancellationToken cancellationToken = default)
    {
        if (!await _access.CanSeePersonAsync(viewer, personId, cancellationToken)) throw new TsForbiddenException();

        var jobs = await LoggableJobs(personId)
            .OrderBy(j => j.Client.Name).ThenBy(j => j.Code)
            .Select(j => new
            {
                j.Id, j.Code, j.Title, ClientName = j.Client.Name,
                Tasks = j.Tasks.Where(t => !t.Task.Archived).OrderBy(t => t.Task.Name).Select(t => new TsNamedDto(t.TaskId, t.Task.Name)).ToList()
            })
            .ToListAsync(cancellationToken);

        return jobs.Select(j => new TsPickJobDto(j.Id, j.Code, j.Title, j.ClientName, j.Tasks)).ToList();
    }

    public async Task<TsEntryDto> CreateAsync(TsViewer viewer, TsEntryWriteRequest data, CancellationToken cancellationToken = default)
    {
        var personId = data.PersonId ?? viewer.PersonId;
        await RequireCanWriteAsync(viewer, personId, data.JobId, cancellationToken);
        await RequireLoggableAsync(personId, data.JobId, data.TaskId, cancellationToken);

        var notes = data.Notes.Trim();
        if (data.StartTimer)
        {
            if (data.Hours < 0 || data.Hours > MaxHours) throw new TsValidationException("Hours must be between 0 and 24");
            await StopRunningAsync(personId, cancellationToken);
        }
        else
        {
            ValidateHours(data.Hours);
            ValidateNotes(notes);
        }

        var entry = new TsEntry
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            JobId = data.JobId,
            TaskId = data.TaskId,
            Date = data.StartTimer ? TsCalendar.Today : data.Date,
            Hours = data.Hours,
            Notes = notes,
            TimerStartedAt = data.StartTimer ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.TsEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
        await _alerts.CheckAsync([entry.JobId], cancellationToken);
        return await GetDtoAsync(entry.Id, cancellationToken);
    }

    public async Task<TsEntryDto?> UpdateAsync(TsViewer viewer, Guid entryId, TsEntryWriteRequest data, CancellationToken cancellationToken = default)
    {
        var entry = await _context.TsEntries.FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        if (entry is null) return null;

        await RequireCanWriteAsync(viewer, entry.PersonId, entry.JobId, cancellationToken);
        await RequireCanWriteAsync(viewer, entry.PersonId, data.JobId, cancellationToken);
        await RequireLoggableAsync(entry.PersonId, data.JobId, data.TaskId, cancellationToken);

        var notes = data.Notes.Trim();
        if (entry.TimerStartedAt is null)
        {
            ValidateHours(data.Hours);
            ValidateNotes(notes);
        }

        var previousJob = entry.JobId;
        entry.JobId = data.JobId;
        entry.TaskId = data.TaskId;
        entry.Date = data.Date;
        entry.Hours = data.Hours;
        entry.Notes = notes;
        entry.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await _alerts.CheckAsync([previousJob, entry.JobId], cancellationToken);
        return await GetDtoAsync(entry.Id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(TsViewer viewer, Guid entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _context.TsEntries.FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        if (entry is null) return false;

        await RequireCanWriteAsync(viewer, entry.PersonId, entry.JobId, cancellationToken);
        _context.TsEntries.Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);
        await _alerts.CheckAsync([entry.JobId], cancellationToken);
        return true;
    }

    public async Task<TsEntryDto?> StartAsync(TsViewer viewer, Guid entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _context.TsEntries.FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        if (entry is null) return null;
        if (entry.PersonId != viewer.PersonId) throw new TsForbiddenException();
        await RequireLoggableAsync(entry.PersonId, entry.JobId, entry.TaskId, cancellationToken);

        await StopRunningAsync(entry.PersonId, cancellationToken);
        var today = TsCalendar.Today;
        if (entry.Date != today)
        {
            entry = new TsEntry
            {
                Id = Guid.NewGuid(),
                PersonId = entry.PersonId,
                JobId = entry.JobId,
                TaskId = entry.TaskId,
                Date = today,
                Notes = entry.Notes,
                CreatedAt = DateTime.UtcNow
            };
            _context.TsEntries.Add(entry);
        }
        entry.TimerStartedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(entry.Id, cancellationToken);
    }

    public async Task<TsEntryDto?> StopAsync(TsViewer viewer, Guid entryId, string notes, CancellationToken cancellationToken = default)
    {
        var entry = await _context.TsEntries.FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        if (entry is null) return null;
        if (entry.PersonId != viewer.PersonId) throw new TsForbiddenException();
        if (entry.TimerStartedAt is null) throw new TsValidationException("That timer isn't running");

        var trimmed = notes.Trim();
        ValidateNotes(trimmed);
        entry.Notes = trimmed;
        Stop(entry);
        await _context.SaveChangesAsync(cancellationToken);
        await _alerts.CheckAsync([entry.JobId], cancellationToken);
        return await GetDtoAsync(entry.Id, cancellationToken);
    }

    public async Task SetStarAsync(TsViewer viewer, TsStarRequest data, CancellationToken cancellationToken = default)
    {
        var existing = await _context.TsStars.FirstOrDefaultAsync(
            s => s.PersonId == viewer.PersonId && s.JobId == data.JobId && s.TaskId == data.TaskId, cancellationToken);

        if (data.Starred && existing is null)
        {
            await RequireLoggableAsync(viewer.PersonId, data.JobId, data.TaskId, cancellationToken);
            _context.TsStars.Add(new TsStar { PersonId = viewer.PersonId, JobId = data.JobId, TaskId = data.TaskId });
        }
        else if (!data.Starred && existing is not null)
        {
            _context.TsStars.Remove(existing);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRowAsync(TsViewer viewer, DateOnly weekStart, Guid jobId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var weekEnd = weekStart.AddDays(6);
        var entries = await _context.TsEntries
            .Where(e => e.PersonId == viewer.PersonId && e.JobId == jobId && e.TaskId == taskId && e.Date >= weekStart && e.Date <= weekEnd)
            .ToListAsync(cancellationToken);
        if (entries.Count > 0) await RequireCanWriteAsync(viewer, viewer.PersonId, jobId, cancellationToken);

        _context.TsEntries.RemoveRange(entries);
        _context.TsStars.RemoveRange(_context.TsStars.Where(s => s.PersonId == viewer.PersonId && s.JobId == jobId && s.TaskId == taskId));
        await _context.SaveChangesAsync(cancellationToken);
        await _alerts.CheckAsync([jobId], cancellationToken);
    }

    private IQueryable<TsJob> LoggableJobs(Guid personId)
        => _context.TsJobs.AsNoTracking()
            .Where(j => !j.Archived && (j.Everyone || j.People.Any(p => p.PersonId == personId)));

    private async Task RequireLoggableAsync(Guid personId, Guid jobId, Guid taskId, CancellationToken cancellationToken)
    {
        var ok = await LoggableJobs(personId)
            .AnyAsync(j => j.Id == jobId && j.Tasks.Any(t => t.TaskId == taskId), cancellationToken);
        if (!ok) throw new TsValidationException("That job is closed or not open to this person");
    }

    private async Task RequireCanWriteAsync(TsViewer viewer, Guid personId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await _access.CanWriteAsync(viewer, personId, jobId, cancellationToken)) throw new TsForbiddenException();
    }

    private async Task StopRunningAsync(Guid personId, CancellationToken cancellationToken)
    {
        var running = await _context.TsEntries
            .Where(e => e.PersonId == personId && e.TimerStartedAt != null)
            .ToListAsync(cancellationToken);
        if (running.Any(e => e.Notes.Length == 0))
            throw new TsValidationException("Add a note to your running timer and stop it first");
        foreach (var entry in running) Stop(entry);
    }

    private static void Stop(TsEntry entry)
    {
        var elapsed = (decimal)(DateTime.UtcNow - entry.TimerStartedAt!.Value).TotalHours;
        entry.Hours = Math.Min(MaxHours, Math.Round(entry.Hours + elapsed, 2));
        entry.TimerStartedAt = null;
        entry.UpdatedAt = DateTime.UtcNow;
    }

    private static void ValidateHours(decimal hours)
    {
        if (hours <= 0 || hours > MaxHours) throw new TsValidationException("Hours must be more than 0 and no more than 24");
    }

    private static void ValidateNotes(string notes)
    {
        if (notes.Length == 0) throw new TsValidationException("A note is needed to save time");
        if (notes.Length > MaxNotes) throw new TsValidationException("That note is too long");
    }

    private Task<TsEntryDto> GetDtoAsync(Guid entryId, CancellationToken cancellationToken)
        => _context.TsEntries.AsNoTracking().Where(e => e.Id == entryId).Select(ToDto).FirstAsync(cancellationToken);
}
