using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;

namespace Panwar.Api.Services.Timesheets;

public enum TsScheduleExportKind
{
    Projects,
    Team
}

public enum TsScheduleExportPeriod
{
    Week,
    Month
}

public sealed record TsScheduleExportRequest(
    TsScheduleExportKind Kind,
    bool Tasks,
    TsScheduleExportPeriod Period,
    bool Rows,
    DateOnly From,
    DateOnly To);

public interface ITsScheduleExportService
{
    Task<byte[]> CsvAsync(TsScheduleExportRequest request, CancellationToken cancellationToken = default);
}

public class TsScheduleExportService : ITsScheduleExportService
{
    private const int MaxDays = 731;
    private const string TimeOff = "Time Off";
    private const string TimeOffLabel = "black";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly AppDbContext _context;

    public TsScheduleExportService(AppDbContext context)
    {
        _context = context;
    }

    private sealed record Bucket(string Label, DateOnly From, DateOnly To);

    private sealed record Booking(Guid PersonId, bool IsLeave, string Client, string Title, string Code, string Task,
        DateOnly StartDate, DateOnly EndDate, decimal HoursPerDay);

    public async Task<byte[]> CsvAsync(TsScheduleExportRequest request, CancellationToken cancellationToken = default)
    {
        if (request.To < request.From) throw new TsValidationException("The end date is before the start date");
        if (request.To.DayNumber - request.From.DayNumber > MaxDays) throw new TsValidationException("Exports can cover up to two years");

        var people = await _context.TsPeople.AsNoTracking()
            .Include(p => p.CapacityChanges.Where(c => c.Date >= request.From && c.Date <= request.To))
            .Where(p => !p.Archived)
            .ToListAsync(cancellationToken);
        var byPerson = people.ToDictionary(p => p.Id);

        var bookings = (await _context.TsBookings.AsNoTracking()
            .Where(b => b.StartDate <= request.To && b.EndDate >= request.From)
            .Select(b => new Booking(b.PersonId, b.Job.IsLeave, b.Job.Client.Name, b.Job.Title, b.Job.Code,
                b.Task == null ? "" : b.Task.Name, b.StartDate, b.EndDate, b.HoursPerDay))
            .ToListAsync(cancellationToken))
            .Where(b => byPerson.ContainsKey(b.PersonId))
            .ToList();

        var buckets = Buckets(request);
        decimal Hours(Booking b, Bucket bucket)
        {
            var from = b.StartDate > bucket.From ? b.StartDate : bucket.From;
            var to = b.EndDate < bucket.To ? b.EndDate : bucket.To;
            return TsCalendar.Days(from, to).Count(d => TsCalendar.Capacity(byPerson[b.PersonId], d) > 0) * b.HoursPerDay;
        }

        var lines = request.Kind == TsScheduleExportKind.Team
            ? Team(request, people, bookings, buckets, Hours)
            : Projects(request, byPerson, bookings, buckets, Hours);

        var csv = new StringBuilder();
        foreach (var line in lines) csv.Append(string.Join(',', line)).Append('\n');
        return new UTF8Encoding(false).GetBytes(csv.ToString());
    }

    private static IEnumerable<IEnumerable<string>> Projects(TsScheduleExportRequest request, Dictionary<Guid, TsPerson> byPerson,
        List<Booking> bookings, List<Bucket> buckets, Func<Booking, Bucket, decimal> hours)
    {
        var groups = bookings
            .GroupBy(b => (
                b.IsLeave,
                Client: b.IsLeave ? "" : b.Client,
                Title: b.IsLeave ? TimeOff : b.Title,
                Code: b.IsLeave ? "" : b.Code,
                Task: request.Tasks && !b.IsLeave ? b.Task : "",
                b.PersonId))
            .Select(g => (g.Key, Person: byPerson[g.Key.PersonId], Hours: buckets.Select(bucket => g.Sum(b => hours(b, bucket))).ToList()))
            .Where(g => g.Hours.Any(h => h != 0))
            .OrderByDescending(g => g.Key.IsLeave)
            .ThenBy(g => g.Key.Client, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Title, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Code, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Task, StringComparer.Ordinal)
            .ThenBy(g => TsNames.Full(g.Person), StringComparer.Ordinal)
            .ToList();

        var headers = new List<string> { "Client", "Project", "Project Code", "Project Label", "Project Tags" };
        if (request.Tasks) headers.Add("Task");
        headers.AddRange(["Person", "Roles"]);

        IEnumerable<string> Describe((bool IsLeave, string Client, string Title, string Code, string Task, Guid PersonId) key, TsPerson person)
        {
            var fields = new List<string> { Quote(key.Client), Quote(key.Title), Quote(key.Code), key.IsLeave ? TimeOffLabel : "", "\"\"" };
            if (request.Tasks) fields.Add(Quote(key.Task));
            fields.AddRange([Quote(TsNames.Full(person)), QuoteList(person.Title)]);
            return fields;
        }

        if (request.Rows)
        {
            yield return ["Date", .. headers, "Hours"];
            foreach (var g in groups)
                for (var i = 0; i < buckets.Count; i++)
                    yield return [buckets[i].Label, .. Describe(g.Key, g.Person), Number(g.Hours[i])];
        }
        else
        {
            yield return [.. headers, .. buckets.Select(b => b.Label)];
            foreach (var g in groups)
                yield return [.. Describe(g.Key, g.Person), .. g.Hours.Select(Number)];
        }
    }

    private static IEnumerable<IEnumerable<string>> Team(TsScheduleExportRequest request, List<TsPerson> people,
        List<Booking> bookings, List<Bucket> buckets, Func<Booking, Bucket, decimal> hours)
    {
        var rows = people
            .OrderBy(TsNames.Full, StringComparer.Ordinal)
            .Select(p =>
            {
                var mine = bookings.Where(b => b.PersonId == p.Id).ToList();
                return (Person: p, Buckets: buckets.Select(bucket => (
                    bucket.Label,
                    Capacity: TsCalendar.Capacity(p, bucket.From, bucket.To),
                    Scheduled: mine.Sum(b => hours(b, bucket)))).ToList());
            })
            .ToList();

        if (request.Rows)
        {
            yield return ["Date", "Person", "Roles", "Capacity", "Schedule"];
            foreach (var row in rows)
                foreach (var b in row.Buckets)
                    yield return [b.Label, Quote(TsNames.Full(row.Person)), QuoteList(row.Person.Title), Number(b.Capacity), Number(b.Scheduled)];
        }
        else
        {
            yield return ["Person", "Roles", "Capacity", .. buckets.Select(b => b.Label)];
            foreach (var row in rows)
                yield return [Quote(TsNames.Full(row.Person)), QuoteList(row.Person.Title), Number(row.Person.DayHours.Sum()), .. row.Buckets.Select(b => Number(b.Scheduled))];
        }
    }

    private static List<Bucket> Buckets(TsScheduleExportRequest request)
    {
        var buckets = new List<Bucket>();
        for (var start = request.From; start <= request.To;)
        {
            var week = request.Period == TsScheduleExportPeriod.Week;
            var end = week
                ? TsCalendar.WeekStart(start).AddDays(6)
                : new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
            if (end > request.To) end = request.To;
            var label = week
                ? TsCalendar.WeekStart(start).ToString("yyyy-MM-dd", Inv)
                : start.ToString("MMM yyyy", Inv);
            buckets.Add(new Bucket(label, start, end));
            start = end.AddDays(1);
        }
        return buckets;
    }

    private static string Number(decimal value) => Math.Round(value, 2).ToString("0.0#", Inv);

    private static string QuoteList(string? value) => string.IsNullOrEmpty(value) ? "\"\"" : Quote(value);

    private static string Quote(string value)
        => value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
