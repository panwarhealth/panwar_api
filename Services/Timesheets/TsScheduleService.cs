using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public interface ITsScheduleService
{
    Task<TsScheduleDto> GetAsync(DateOnly weekStart, int weeks, CancellationToken cancellationToken = default);
    Task<TsBookingDto> CreateAsync(TsBookingWriteRequest data, Guid byPersonId, CancellationToken cancellationToken = default);
    Task<TsBookingDto?> UpdateAsync(Guid bookingId, TsBookingWriteRequest data, Guid byPersonId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid bookingId, bool following, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TsJobHoursDto>> FutureHoursAsync(CancellationToken cancellationToken = default);
    Task<int> ShiftJobAsync(Guid jobId, int days, Guid byPersonId, CancellationToken cancellationToken = default);
    Task<TsBookingDto?> MoveAsync(Guid bookingId, DateOnly startDate, DateOnly endDate, Guid byPersonId, CancellationToken cancellationToken = default);
}

public class TsScheduleService : ITsScheduleService
{
    private const decimal MaxHoursPerDay = 24;
    private const int MaxDays = 731;
    private const int MaxNote = 500;
    private const int MaxRepeatWeeks = 104;

    private Expression<Func<TsBooking, TsBookingDto>> ToDto => b => new TsBookingDto(
        b.Id, b.PersonId, b.JobId, b.Job.Code, b.Job.Title, b.Job.Client.Name, b.Job.IsLeave,
        b.TaskId, b.StartDate, b.EndDate, b.HoursPerDay, b.OnDaysOff, b.Note,
        b.RepeatSetId == null ? 1 : _context.TsBookings.Count(x => x.RepeatSetId == b.RepeatSetId && x.StartDate >= b.StartDate),
        b.UpdatedAt,
        b.UpdatedBy == null ? null : (b.UpdatedBy.FirstName + " " + b.UpdatedBy.LastName).Trim(),
        b.Job.Color);

    private readonly AppDbContext _context;

    public TsScheduleService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TsScheduleDto> GetAsync(DateOnly weekStart, int weeks, CancellationToken cancellationToken = default)
    {
        var start = TsCalendar.WeekStart(weekStart);
        var end = start.AddDays(weeks * 7 - 1);

        var people = await _context.TsPeople.AsNoTracking()
            .Include(p => p.CapacityChanges.Where(c => c.Date >= start && c.Date <= end))
            .Where(p => !p.Archived)
            .OrderBy(p => p.IsPlaceholder).ThenBy(p => p.FirstName).ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);

        var bookings = await _context.TsBookings.AsNoTracking()
            .Where(b => b.StartDate <= end && b.EndDate >= start)
            .OrderBy(b => b.StartDate).ThenBy(b => b.Job.Code)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        var days = TsCalendar.Days(start, end).ToList();
        var byPerson = people.ToDictionary(p => p.Id);
        var rows = people.Select(p =>
        {
            var mine = bookings.Where(b => b.PersonId == p.Id).ToList();
            var dayRows = days.Select(d =>
            {
                var capacity = TsCalendar.Capacity(p, d);
                var covering = mine.Where(b => b.StartDate <= d && b.EndDate >= d && (capacity > 0 || b.OnDaysOff)).ToList();
                return new TsScheduleDayDto(d, capacity, covering.Where(b => !b.IsLeave).Sum(b => b.HoursPerDay), covering.Where(b => b.IsLeave).Sum(b => b.HoursPerDay));
            }).ToList();
            var weekRows = dayRows.Chunk(7).Select(week =>
            {
                var capacity = week.Sum(d => d.Capacity);
                var booked = week.Sum(d => d.Booked);
                var off = week.Sum(d => d.Off);
                var available = capacity - off;
                int? percent = available > 0 ? (int)Math.Round(booked / available * 100) : null;
                return new TsScheduleWeekDto(week[0].Date, capacity, booked, off, percent);
            }).ToList();
            return new TsSchedulePersonDto(p.Id, TsNames.Full(p), p.Title, p.IsPlaceholder, weekRows, dayRows);
        }).ToList();

        var jobs = bookings
            .Where(b => byPerson.ContainsKey(b.PersonId))
            .GroupBy(b => new { b.JobId, b.JobCode, b.JobTitle, b.ClientName, b.IsLeave, b.JobColor })
            .Select(g => new TsScheduleJobDto(g.Key.JobId, g.Key.JobCode, g.Key.JobTitle, g.Key.ClientName, g.Key.IsLeave, g.Key.JobColor,
                days.Chunk(7).Select(week => g.Sum(b => week.Count(d =>
                    b.StartDate <= d && b.EndDate >= d && (b.OnDaysOff || TsCalendar.Capacity(byPerson[b.PersonId], d) > 0)) * b.HoursPerDay)).ToList()))
            .OrderBy(j => j.ClientName).ThenBy(j => j.JobCode)
            .ToList();

        return new TsScheduleDto(rows, jobs, bookings);
    }

    public async Task<TsBookingDto> CreateAsync(TsBookingWriteRequest data, Guid byPersonId, CancellationToken cancellationToken = default)
    {
        var booking = new TsBooking { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        await ApplyAsync(booking, data, byPersonId, cancellationToken);
        _context.TsBookings.Add(booking);
        AddRepeats(booking, data.RepeatWeeks);
        await _context.SaveChangesAsync(cancellationToken);
        return await _context.TsBookings.AsNoTracking().Where(b => b.Id == booking.Id).Select(ToDto).FirstAsync(cancellationToken);
    }

    public async Task<TsBookingDto?> UpdateAsync(Guid bookingId, TsBookingWriteRequest data, Guid byPersonId, CancellationToken cancellationToken = default)
    {
        var booking = await _context.TsBookings.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null) return null;
        var (setId, start) = (booking.RepeatSetId, booking.StartDate);
        await ApplyAsync(booking, data, byPersonId, cancellationToken);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (setId is not null)
            await _context.TsBookings
                .Where(b => b.RepeatSetId == setId && b.StartDate > start && b.Id != bookingId)
                .ExecuteDeleteAsync(cancellationToken);
        AddRepeats(booking, data.RepeatWeeks);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await _context.TsBookings.AsNoTracking().Where(b => b.Id == booking.Id).Select(ToDto).FirstAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid bookingId, bool following, CancellationToken cancellationToken = default)
    {
        var booking = await _context.TsBookings.AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new { b.RepeatSetId, b.StartDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (booking is null) return false;

        var doomed = following && booking.RepeatSetId is { } setId
            ? _context.TsBookings.Where(b => b.RepeatSetId == setId && b.StartDate >= booking.StartDate)
            : _context.TsBookings.Where(b => b.Id == bookingId);
        await doomed.ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<TsJobHoursDto>> FutureHoursAsync(CancellationToken cancellationToken = default)
    {
        var today = TsCalendar.Today;
        var bookings = await _context.TsBookings.AsNoTracking()
            .Where(b => b.EndDate >= today && !b.Person.Archived)
            .Select(b => new { b.JobId, b.PersonId, b.StartDate, b.EndDate, b.HoursPerDay, b.OnDaysOff })
            .ToListAsync(cancellationToken);
        var personIds = bookings.Select(b => b.PersonId).Distinct().ToList();
        var people = await _context.TsPeople.AsNoTracking()
            .Include(p => p.CapacityChanges.Where(c => c.Date >= today))
            .Where(p => personIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return bookings
            .GroupBy(b => b.JobId)
            .Select(g => new TsJobHoursDto(g.Key, g.Sum(b =>
                TsCalendar.Days(b.StartDate > today ? b.StartDate : today, b.EndDate)
                    .Count(d => b.OnDaysOff || TsCalendar.Capacity(people[b.PersonId], d) > 0) * b.HoursPerDay),
                g.Select(b => b.PersonId).Distinct().ToList()))
            .ToList();
    }

    public async Task<int> ShiftJobAsync(Guid jobId, int days, Guid byPersonId, CancellationToken cancellationToken = default)
    {
        if (days == 0 || Math.Abs(days) > MaxDays) throw new TsValidationException("Shift by 1 to 731 days");
        var today = TsCalendar.Today;
        var bookings = await _context.TsBookings.Where(b => b.JobId == jobId && b.EndDate >= today).ToListAsync(cancellationToken);
        foreach (var booking in bookings)
        {
            booking.StartDate = booking.StartDate.AddDays(days);
            booking.EndDate = booking.EndDate.AddDays(days);
            booking.UpdatedById = byPersonId;
            booking.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync(cancellationToken);
        return bookings.Count;
    }

    public async Task<TsBookingDto?> MoveAsync(Guid bookingId, DateOnly startDate, DateOnly endDate, Guid byPersonId, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate) throw new TsValidationException("The end date is before the start date");
        if (endDate.DayNumber - startDate.DayNumber > MaxDays) throw new TsValidationException("Bookings can be up to two years long");
        var booking = await _context.TsBookings.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null) return null;

        booking.StartDate = startDate;
        booking.EndDate = endDate;
        booking.UpdatedById = byPersonId;
        booking.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return await _context.TsBookings.AsNoTracking().Where(b => b.Id == booking.Id).Select(ToDto).FirstAsync(cancellationToken);
    }

    private void AddRepeats(TsBooking booking, int weeks)
    {
        if (weeks <= 1)
        {
            booking.RepeatSetId = null;
            return;
        }
        booking.RepeatSetId ??= Guid.NewGuid();
        for (var week = 1; week < weeks; week++)
            _context.TsBookings.Add(new TsBooking
            {
                Id = Guid.NewGuid(),
                PersonId = booking.PersonId,
                JobId = booking.JobId,
                TaskId = booking.TaskId,
                StartDate = booking.StartDate.AddDays(7 * week),
                EndDate = booking.EndDate.AddDays(7 * week),
                HoursPerDay = booking.HoursPerDay,
                OnDaysOff = booking.OnDaysOff,
                Note = booking.Note,
                RepeatSetId = booking.RepeatSetId,
                UpdatedById = booking.UpdatedById,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = booking.UpdatedAt
            });
    }

    private async Task ApplyAsync(TsBooking booking, TsBookingWriteRequest data, Guid byPersonId, CancellationToken cancellationToken)
    {
        if (data.RepeatWeeks < 1 || data.RepeatWeeks > MaxRepeatWeeks) throw new TsValidationException("Repeat weekly for 1 to 104 weeks");
        if (data.RepeatWeeks > 1 && data.EndDate.DayNumber - data.StartDate.DayNumber >= 7)
            throw new TsValidationException("Only bookings of a week or less can repeat weekly");
        if (data.EndDate < data.StartDate) throw new TsValidationException("The end date is before the start date");
        if (data.EndDate.DayNumber - data.StartDate.DayNumber > MaxDays) throw new TsValidationException("Bookings can be up to two years long");
        if (data.HoursPerDay <= 0 || data.HoursPerDay > MaxHoursPerDay) throw new TsValidationException("Hours a day must be more than 0 and no more than 24");
        var note = string.IsNullOrWhiteSpace(data.Note) ? null : data.Note.Trim();
        if (note?.Length > MaxNote) throw new TsValidationException("That note is too long");

        if (!await _context.TsPeople.AnyAsync(p => p.Id == data.PersonId && !p.Archived, cancellationToken))
            throw new TsValidationException("Pick someone to schedule");
        if (!await _context.TsJobs.AnyAsync(j => j.Id == data.JobId && !j.Archived, cancellationToken))
            throw new TsValidationException("Pick an open job");
        if (data.TaskId is { } taskId && !await _context.TsJobTasks.AnyAsync(t => t.JobId == data.JobId && t.TaskId == taskId, cancellationToken))
            throw new TsValidationException("That task isn't on this job");

        booking.PersonId = data.PersonId;
        booking.JobId = data.JobId;
        booking.TaskId = data.TaskId;
        booking.StartDate = data.StartDate;
        booking.EndDate = data.EndDate;
        booking.HoursPerDay = data.HoursPerDay;
        booking.Note = note;
        booking.OnDaysOff = data.OnDaysOff;
        booking.UpdatedById = byPersonId;
        booking.UpdatedAt = DateTime.UtcNow;
    }
}
