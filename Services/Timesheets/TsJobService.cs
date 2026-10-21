using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public enum TsJobStatus
{
    Open,
    Archived,
    All
}

public interface ITsJobService
{
    Task<IReadOnlyList<TsJobRowDto>> ListAsync(TsJobStatus status, CancellationToken cancellationToken = default);
    Task<TsJobDetailDto?> GetAsync(Guid jobId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
    Task<TsNextCodeDto> NextCodeAsync(Guid clientId, CancellationToken cancellationToken = default);
    Task<TsJobDetailDto> CreateAsync(TsJobWriteRequest data, CancellationToken cancellationToken = default);
    Task<TsJobDetailDto?> UpdateAsync(Guid jobId, TsJobWriteRequest data, CancellationToken cancellationToken = default);
    Task<bool> SetArchivedAsync(Guid jobId, bool archived, CancellationToken cancellationToken = default);
}

public class TsJobService : ITsJobService
{
    private const int MaxTitle = 300;
    private const int MaxCode = 40;

    private readonly AppDbContext _context;
    private readonly ITsBudgetAlertService _alerts;
    private readonly ITrelloService _trello;

    public TsJobService(AppDbContext context, ITsBudgetAlertService alerts, ITrelloService trello)
    {
        _context = context;
        _alerts = alerts;
        _trello = trello;
    }

    public async Task<IReadOnlyList<TsJobRowDto>> ListAsync(TsJobStatus status, CancellationToken cancellationToken = default)
    {
        var jobs = _context.TsJobs.AsNoTracking();
        if (status != TsJobStatus.All) jobs = jobs.Where(j => j.Archived == (status == TsJobStatus.Archived));

        return await jobs
            .OrderBy(j => j.Client.Name).ThenBy(j => j.Code)
            .Select(j => new TsJobRowDto(
                j.Id, j.Code, j.Title, j.ClientId, j.Client.Name,
                _context.TsEntries.Where(e => e.JobId == j.Id).Sum(e => (decimal?)e.Hours) ?? 0,
                j.Archived))
            .ToListAsync(cancellationToken);
    }

    public async Task<TsJobDetailDto?> GetAsync(Guid jobId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var job = await _context.TsJobs.AsNoTracking()
            .Include(j => j.Client)
            .Include(j => j.Tasks).ThenInclude(t => t.Task)
            .Include(j => j.People).ThenInclude(p => p.Person)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null) return null;

        var allEntries = _context.TsEntries.AsNoTracking().Where(e => e.JobId == jobId);
        var byDate = await allEntries
            .GroupBy(e => e.Date)
            .Select(g => new { Date = g.Key, Hours = g.Sum(e => e.Hours) })
            .ToListAsync(cancellationToken);

        var ranged = allEntries;
        if (from is { } f) ranged = ranged.Where(e => e.Date >= f);
        if (to is { } t) ranged = ranged.Where(e => e.Date <= t);

        var byTask = await ranged.GroupBy(e => new { e.TaskId, e.Task.Name })
            .Select(g => new { g.Key.TaskId, g.Key.Name, Hours = g.Sum(e => e.Hours) })
            .ToListAsync(cancellationToken);
        var byPerson = await ranged.GroupBy(e => new { e.PersonId, e.Person.FirstName, e.Person.LastName })
            .Select(g => new { g.Key.PersonId, Name = (g.Key.FirstName + " " + g.Key.LastName).Trim(), Hours = g.Sum(e => e.Hours) })
            .ToListAsync(cancellationToken);

        var tasks = job.Tasks
            .Select(t => new TsJobTaskDto(t.TaskId, t.Task.Name, t.BudgetHours, byTask.FirstOrDefault(x => x.TaskId == t.TaskId)?.Hours ?? 0))
            .Concat(byTask.Where(x => job.Tasks.All(t => t.TaskId != x.TaskId))
                .Select(x => new TsJobTaskDto(x.TaskId, x.Name, null, x.Hours)))
            .OrderBy(t => t.Name)
            .ToList();

        var people = job.People
            .Select(p => new TsJobPersonDto(p.PersonId, TsNames.Full(p.Person), p.IsManager, true,
                byPerson.FirstOrDefault(x => x.PersonId == p.PersonId)?.Hours ?? 0))
            .Concat(byPerson.Where(x => job.People.All(p => p.PersonId != x.PersonId))
                .Select(x => new TsJobPersonDto(x.PersonId, x.Name, false, job.Everyone, x.Hours)))
            .OrderByDescending(p => p.Spent).ThenBy(p => p.Name)
            .ToList();

        var weeks = byDate
            .GroupBy(d => TsCalendar.WeekStart(d.Date))
            .OrderBy(g => g.Key)
            .Select(g => new TsWeekHoursDto(g.Key, g.Sum(d => d.Hours)))
            .ToList();

        var budgets = job.Tasks.Where(t => t.BudgetHours is not null).Select(t => t.BudgetHours!.Value).ToList();
        return new TsJobDetailDto(
            job.Id, job.Code, job.Title, job.ClientId, job.Client.Name, job.Archived, job.IsLeave, job.Everyone,
            job.AlertPercent, job.TrelloCardUrl, job.OpenedOn, byDate.Sum(d => d.Hours),
            budgets.Count == 0 ? null : budgets.Sum(), tasks, people, weeks);
    }

    public async Task<TsNextCodeDto> NextCodeAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var client = await _context.TsClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken)
            ?? throw new TsValidationException("Pick a client");
        var last = await _context.TsJobs.AsNoTracking()
            .Where(j => j.ClientId == clientId)
            .OrderByDescending(j => j.OpenedOn).ThenByDescending(j => j.CreatedAt)
            .Select(j => j.Code + " " + j.Title)
            .FirstOrDefaultAsync(cancellationToken);
        if (client.SuppliesOwnCodes || string.IsNullOrEmpty(client.CodePrefix)) return new TsNextCodeDto(null, last);

        var codes = await _context.TsJobs.AsNoTracking().Where(j => j.ClientId == clientId).Select(j => j.Code).ToListAsync(cancellationToken);
        return new TsNextCodeDto(TsJobCodes.Next(client.CodePrefix, codes), last);
    }

    public async Task<TsJobDetailDto> CreateAsync(TsJobWriteRequest data, CancellationToken cancellationToken = default)
    {
        var job = new TsJob
        {
            Id = Guid.NewGuid(),
            Code = "",
            Title = "",
            OpenedOn = TsCalendar.Today,
            CreatedAt = DateTime.UtcNow
        };
        await ApplyAsync(job, data, cancellationToken);
        _context.TsJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);

        if (data.CreateTrelloCard)
        {
            var budget = data.Tasks.Sum(t => t.BudgetHours ?? 0);
            job.TrelloCardUrl = await _trello.CreateJobCardAsync(job.Code, job.Title, budget, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        return (await GetAsync(job.Id, null, null, cancellationToken))!;
    }

    public async Task<TsJobDetailDto?> UpdateAsync(Guid jobId, TsJobWriteRequest data, CancellationToken cancellationToken = default)
    {
        var job = await _context.TsJobs
            .Include(j => j.Tasks)
            .Include(j => j.People)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null) return null;

        await ApplyAsync(job, data, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await _alerts.CheckAsync([job.Id], cancellationToken);
        return await GetAsync(job.Id, null, null, cancellationToken);
    }

    public async Task<bool> SetArchivedAsync(Guid jobId, bool archived, CancellationToken cancellationToken = default)
    {
        var job = await _context.TsJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null) return false;
        job.Archived = archived;
        job.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ApplyAsync(TsJob job, TsJobWriteRequest data, CancellationToken cancellationToken)
    {
        var title = data.Title.Trim();
        var code = data.Code.Trim().ToUpperInvariant();
        var codeChanged = code != job.Code;
        if (title.Length == 0 || title.Length > MaxTitle) throw new TsValidationException("Job name is required");
        if (codeChanged && (code.Length == 0 || code.Length > MaxCode)) throw new TsValidationException("Job code is required");
        if (data.AlertPercent is < 1 or > 100) throw new TsValidationException("Alert % must be between 1 and 100");
        if (data.Tasks.Count == 0) throw new TsValidationException("Pick at least one task");
        if (data.Tasks.Any(t => t.BudgetHours < 0)) throw new TsValidationException("Budget hours can't be negative");

        if (!await _context.TsClients.AnyAsync(c => c.Id == data.ClientId, cancellationToken))
            throw new TsValidationException("Pick a client");
        if (codeChanged && await _context.TsJobs.AnyAsync(j => j.Id != job.Id && j.Code.ToUpper() == code, cancellationToken))
            throw new TsValidationException($"{code} is already used by another job");

        var taskIds = data.Tasks.Select(t => t.TaskId).Distinct().ToList();
        if (await _context.TsTasks.CountAsync(t => taskIds.Contains(t.Id), cancellationToken) != taskIds.Count)
            throw new TsValidationException("One of those tasks no longer exists");

        var activePeople = await _context.TsPeople
            .Where(p => !p.Archived && !p.IsPlaceholder)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        var people = data.People.DistinctBy(p => p.PersonId).ToList();
        if (people.Any(p => !activePeople.Contains(p.PersonId))) throw new TsValidationException("One of those people is no longer active");

        job.ClientId = data.ClientId;
        job.Code = code;
        job.Title = title;
        job.IsLeave = data.IsLeave;
        job.AlertPercent = data.AlertPercent;
        job.Everyone = activePeople.All(id => people.Any(p => p.PersonId == id));
        job.UpdatedAt = DateTime.UtcNow;

        var tasks = data.Tasks.DistinctBy(t => t.TaskId).ToList();
        foreach (var gone in job.Tasks.Where(t => tasks.All(x => x.TaskId != t.TaskId)).ToList()) job.Tasks.Remove(gone);
        foreach (var t in tasks)
        {
            var existing = job.Tasks.FirstOrDefault(x => x.TaskId == t.TaskId);
            if (existing is null) job.Tasks.Add(new TsJobTask { JobId = job.Id, TaskId = t.TaskId, BudgetHours = t.BudgetHours });
            else existing.BudgetHours = t.BudgetHours;
        }

        foreach (var gone in job.People.Where(p => people.All(x => x.PersonId != p.PersonId)).ToList()) job.People.Remove(gone);
        foreach (var p in people)
        {
            var existing = job.People.FirstOrDefault(x => x.PersonId == p.PersonId);
            if (existing is null) job.People.Add(new TsJobPerson { JobId = job.Id, PersonId = p.PersonId, IsManager = p.IsManager });
            else existing.IsManager = p.IsManager;
        }
    }
}
