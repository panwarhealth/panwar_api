using Panwar.Api.Models;

namespace Panwar.Api.Services.Timesheets;

public static class TsCalendar
{
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Sydney));

    public static decimal[] StandardWeek() => [7.5m, 7.5m, 7.5m, 7.5m, 7.5m, 0, 0];

    public static int DayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-DayIndex(date));

    public static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1)) yield return d;
    }

    public static decimal Capacity(TsPerson person, DateOnly date)
    {
        var change = person.CapacityChanges.FirstOrDefault(c => c.Date == date);
        if (change is not null) return change.Hours;
        var index = DayIndex(date);
        return index < person.DayHours.Length ? person.DayHours[index] : 0;
    }

    public static decimal Capacity(TsPerson person, DateOnly from, DateOnly to)
        => Days(from, to).Sum(d => Capacity(person, d));
}
