using System.Globalization;

namespace Panwar.Api.Services.Edm;

/// <summary>Staff think in Sydney time; the database stores UTC.</summary>
public static class EdmTime
{
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    /// <summary>"2026-09-18T09:00" in Sydney (AEST or AEDT, whichever applies that day) to UTC.</summary>
    public static DateTime? ParseSydney(string local)
    {
        if (!DateTime.TryParseExact(local, ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
            return null;
        // The skipped hour when clocks go forward has no UTC equivalent; nudge it an hour on.
        if (Sydney.IsInvalidTime(wall)) wall = wall.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(wall, Sydney);
    }

    public static string? FormatSydney(DateTime? utc) =>
        utc is { } u
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u, DateTimeKind.Utc), Sydney).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : null;
}
