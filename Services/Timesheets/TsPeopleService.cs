using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public interface ITsPeopleService
{
    Task<IReadOnlyList<TsPersonDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<TsPersonDto> CreateAsync(TsPersonWriteRequest data, CancellationToken cancellationToken = default);
    Task<TsPersonDto?> UpdateAsync(Guid personId, TsPersonWriteRequest data, CancellationToken cancellationToken = default);
    Task<TsPersonDto?> SetCapacityAsync(Guid personId, TsCapacityWriteRequest data, CancellationToken cancellationToken = default);
}

public class TsPeopleService : ITsPeopleService
{
    private const int MaxName = 100;
    private const decimal MaxDayHours = 24;

    private readonly AppDbContext _context;

    public TsPeopleService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TsPersonDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var people = await _context.TsPeople.AsNoTracking()
            .Include(p => p.CapacityChanges).Include(p => p.Manages)
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);
        return people.Select(ToDto).ToList();
    }

    public async Task<TsPersonDto> CreateAsync(TsPersonWriteRequest data, CancellationToken cancellationToken = default)
    {
        var person = new TsPerson { Id = Guid.NewGuid(), FirstName = "", DayHours = TsCalendar.StandardWeek(), CreatedAt = DateTime.UtcNow };
        await ApplyAsync(person, data, cancellationToken);
        _context.TsPeople.Add(person);
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(person);
    }

    public async Task<TsPersonDto?> UpdateAsync(Guid personId, TsPersonWriteRequest data, CancellationToken cancellationToken = default)
    {
        var person = await _context.TsPeople.Include(p => p.CapacityChanges).Include(p => p.Manages).FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null) return null;
        await ApplyAsync(person, data, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(person);
    }

    public async Task<TsPersonDto?> SetCapacityAsync(Guid personId, TsCapacityWriteRequest data, CancellationToken cancellationToken = default)
    {
        var person = await _context.TsPeople.Include(p => p.CapacityChanges).Include(p => p.Manages).FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null) return null;

        if (data.DayHours.Count != 7) throw new TsValidationException("Capacity needs hours for all seven days");
        if (data.DayHours.Concat(data.Changes.Select(c => c.Hours)).Any(h => h < 0 || h > MaxDayHours))
            throw new TsValidationException("Hours in a day must be between 0 and 24");
        var changes = data.Changes.DistinctBy(c => c.Date).ToList();

        person.DayHours = [.. data.DayHours];
        foreach (var gone in person.CapacityChanges.Where(c => changes.All(x => x.Date != c.Date)).ToList())
            person.CapacityChanges.Remove(gone);
        foreach (var c in changes)
        {
            var existing = person.CapacityChanges.FirstOrDefault(x => x.Date == c.Date);
            var note = string.IsNullOrWhiteSpace(c.Note) ? null : c.Note.Trim();
            if (existing is null)
                person.CapacityChanges.Add(new TsCapacityChange { Id = Guid.NewGuid(), PersonId = person.Id, Date = c.Date, Hours = c.Hours, Note = note });
            else
            {
                existing.Hours = c.Hours;
                existing.Note = note;
            }
        }
        person.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(person);
    }

    private async Task ApplyAsync(TsPerson person, TsPersonWriteRequest data, CancellationToken cancellationToken)
    {
        var first = data.FirstName.Trim();
        var last = data.LastName.Trim();
        var email = string.IsNullOrWhiteSpace(data.Email) ? null : data.Email.Trim().ToLowerInvariant();
        if (first.Length == 0 || first.Length > MaxName || last.Length > MaxName) throw new TsValidationException("Name is required");
        if (!data.IsPlaceholder && email is null) throw new TsValidationException("Email is required, it's how they sign in");
        if (email is not null && !email.Contains('@')) throw new TsValidationException("That email doesn't look right");
        if (email is not null && await _context.TsPeople.AnyAsync(p => p.Id != person.Id && p.Email == email, cancellationToken))
            throw new TsValidationException($"{email} is already on the team");

        person.FirstName = first;
        person.LastName = last;
        person.Email = data.IsPlaceholder ? null : email;
        person.Title = string.IsNullOrWhiteSpace(data.Title) ? null : data.Title.Trim();
        person.IsContractor = data.IsContractor;
        person.IsPlaceholder = data.IsPlaceholder;
        person.Archived = data.Archived;
        person.UpdatedAt = DateTime.UtcNow;

        List<Guid> managed = data.IsPlaceholder ? [] : data.ManagedPersonIds.Where(id => id != person.Id).Distinct().ToList();
        if (await _context.TsPeople.CountAsync(p => managed.Contains(p.Id), cancellationToken) != managed.Count)
            throw new TsValidationException("One of the people they manage no longer exists");
        foreach (var gone in person.Manages.Where(m => !managed.Contains(m.PersonId)).ToList()) person.Manages.Remove(gone);
        foreach (var id in managed.Where(id => person.Manages.All(m => m.PersonId != id)))
            person.Manages.Add(new TsPersonManager { ManagerId = person.Id, PersonId = id });
    }

    private static TsPersonDto ToDto(TsPerson p)
        => new(p.Id, p.FirstName, p.LastName, p.Email, p.Title, p.IsContractor, p.IsPlaceholder, p.Archived, p.DayHours,
            p.CapacityChanges.OrderBy(c => c.Date).Select(c => new TsCapacityChangeDto(c.Date, c.Hours, c.Note)).ToList(),
            p.Manages.Select(m => m.PersonId).ToList());
}
