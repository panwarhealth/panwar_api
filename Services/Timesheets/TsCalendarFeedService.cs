using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;

namespace Panwar.Api.Services.Timesheets;

public interface ITsCalendarFeedService
{
    Task<string?> TokenAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<string?> IcsAsync(string token, CancellationToken cancellationToken = default);
}

public class TsCalendarFeedService : ITsCalendarFeedService
{
    private const int DaysBack = 60;
    private const int DaysAhead = 730;
    private const int MaxLine = 74;

    private readonly AppDbContext _context;

    public TsCalendarFeedService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string?> TokenAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var person = await _context.TsPeople.FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null) return null;
        if (person.CalendarToken is null)
        {
            person.CalendarToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            await _context.SaveChangesAsync(cancellationToken);
        }
        return person.CalendarToken;
    }

    public async Task<string?> IcsAsync(string token, CancellationToken cancellationToken = default)
    {
        var person = await _context.TsPeople.AsNoTracking()
            .Where(p => p.CalendarToken == token)
            .Select(p => new { p.Id, p.FirstName, p.LastName })
            .FirstOrDefaultAsync(cancellationToken);
        if (person is null) return null;

        var today = TsCalendar.Today;
        var from = today.AddDays(-DaysBack);
        var to = today.AddDays(DaysAhead);
        var bookings = await _context.TsBookings.AsNoTracking()
            .Where(b => b.PersonId == person.Id && b.EndDate >= from && b.StartDate <= to)
            .OrderBy(b => b.StartDate)
            .Select(b => new
            {
                b.Id, b.StartDate, b.EndDate, b.HoursPerDay, b.Note, b.UpdatedAt,
                b.Job.Code, b.Job.Title, b.Job.IsLeave, Client = b.Job.Client.Name
            })
            .ToListAsync(cancellationToken);

        var ics = new StringBuilder();
        void Line(string text)
        {
            var rest = text;
            ics.Append(rest[..Math.Min(MaxLine, rest.Length)]).Append("\r\n");
            for (rest = rest[Math.Min(MaxLine, rest.Length)..]; rest.Length > 0; rest = rest[Math.Min(MaxLine - 1, rest.Length)..])
                ics.Append(' ').Append(rest[..Math.Min(MaxLine - 1, rest.Length)]).Append("\r\n");
        }

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Panwar Health//Timesheets//EN");
        Line("CALSCALE:GREGORIAN");
        Line($"X-WR-CALNAME:{Escape($"Schedule - {person.FirstName} {person.LastName}".Trim())}");
        foreach (var b in bookings)
        {
            var hours = Math.Round(b.HoursPerDay, 2).ToString("0.##", CultureInfo.InvariantCulture);
            var summary = b.IsLeave ? b.Title : $"[{b.Code}] {b.Title} ({hours} h/d)";
            var description = string.IsNullOrEmpty(b.Note) ? b.Client : $"{b.Client}\n{b.Note}";
            Line("BEGIN:VEVENT");
            Line($"UID:{b.Id}@panwarhealth.com.au");
            Line($"DTSTAMP:{b.UpdatedAt.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");
            Line($"DTSTART;VALUE=DATE:{b.StartDate:yyyyMMdd}");
            Line($"DTEND;VALUE=DATE:{b.EndDate.AddDays(1):yyyyMMdd}");
            Line($"SUMMARY:{Escape(summary)}");
            Line($"DESCRIPTION:{Escape(description)}");
            Line("TRANSP:TRANSPARENT");
            Line("END:VEVENT");
        }
        Line("END:VCALENDAR");
        return ics.ToString();
    }

    private static string Escape(string text)
        => text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
}
