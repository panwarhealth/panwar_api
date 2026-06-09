namespace Panwar.Api.Services;

/// <summary>
/// Month-granularity period helpers shared by the dashboard + summary services.
/// A month is encoded as an ordinal (year*12 + month-1) so windows compare with
/// simple integer arithmetic. Strings are "YYYY-MM".
/// </summary>
internal static class PeriodWindow
{
    public static int Ord(int year, int month) => year * 12 + (month - 1);

    public static bool TryParse(string? ym, out int ord)
    {
        ord = 0;
        if (string.IsNullOrWhiteSpace(ym)) return false;
        var parts = ym.Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var y)
            || !int.TryParse(parts[1], out var m)
            || m < 1 || m > 12) return false;
        ord = Ord(y, m);
        return true;
    }

    public static string ToYm(int ord) => $"{ord / 12:D4}-{ord % 12 + 1:D2}";
}
