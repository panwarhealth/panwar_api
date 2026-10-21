using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public interface ITsScheduleService
{
    Task<TsScheduleDto> GetAsync(DateOnly weekStart, int weeks, CancellationToken cancellationToken = default);
    Task<TsBookingDto> CreateAsync(TsBookingWriteRequest data, CancellationToken cancellationToken = default);
    Task<TsBookingDto?> UpdateAsync(Guid bookingId, TsBookingWriteRequest data, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid bookingId, CancellationToken cancellationToken = default);
}

public class TsScheduleService : ITsScheduleService
{
    private const decimal MaxHoursPerDay = 24;
    private const int MaxDays = 731;
    private const int MaxNote = 500;

    private static readonly Expression<Func<TsBooking, TsBookingDto>> ToDto = b => new TsBookingDto(
        b.Id, b.PersonId, b.JobId, b.Job.Code, b.Job.Title, b.Job.Client.Name, b.Job.IsLeave,
        b.TaskId, b.StartDate, b.EndDate, b.HoursPerDay, b.Note);

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
                var covering = capacity > 0 ? mine.Where(b => b.StartDate <= d && b.EndDate >= d).ToList() : [];
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
            .GroupBy(b => new { b.JobId, b.JobCode, b.JobTitle, b.ClientName, b.IsLeave })
            .Select(g => new TsScheduleJobDto(g.Key.JobId, g.Key.JobCode, g.Key.JobTitle, g.Key.ClientName, g.Key.IsLeave,
                days.Chunk(7).Select(week => g.Sum(b => week.Count(d =>
                    b.StartDate <= d && b.EndDate >= d && TsCalendar.Capacity(byPerson[b.PersonId], d) > 0) * b.HoursPerDay)).ToList()))
            .OrderBy(j => j.ClientName).ThenBy(j => j.JobCode)
            .ToList();

        return new TsScheduleDto(rows, jobs, bookings);
    }

    public async Task<TsBookingDto> CreateAsync(TsBookingWriteRequest data, CancellationToken cancellationToken = default)
    {
        var booking = new TsBooking { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        await ApplyAsync(booking, data, cancellationToken);
        _context.TsBookings.Add(booking);
        await _context.SaveChangesAsync(cancellationToken);
        return await _context.TsBookings.AsNoTracking().Where(b => b.Id == booking.Id).Select(ToDto).FirstAsync(cancellationToken);
    }

    public async Task<TsBookingDto?> UpdateAsync(Guid bookingId, TsBookingWriteRequest data, CancellationToken cancellationToken = default)
    {
        var booking = await _context.TsBookings.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null) return null;
        await ApplyAsync(booking, data, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return await _context.TsBookings.AsNoTracking().Where(b => b.Id == booking.Id).Select(ToDto).FirstAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid bookingId, CancellationToken cancellationToken = default)
        => await _context.TsBookings.Where(b => b.Id == bookingId).ExecuteDeleteAsync(cancellationToken) > 0;

    private async Task ApplyAsync(TsBooking booking, TsBookingWriteRequest data, CancellationToken cancellationToken)
    {
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
        booking.UpdatedAt = DateTime.UtcNow;
    }
}
