using Panwar.Api.Models;

namespace Panwar.Api.Services;

// Month ordinal = year*12 + (month-1). Placement date shape determines presence/target logic:
//   StartDate + EndDate → education range; StartDate only → eDM send; neither → LiveMonths/Year.
internal static class PeriodWindow
{
    public static int Ord(int year, int month) => year * 12 + (month - 1);

    public static int Ord(DateOnly d) => Ord(d.Year, d.Month);

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

    public static bool AppearsInWindow(Placement p, int fromOrd, int toOrd)
    {
        if (p.StartDate is { } start)
        {
            if (p.EndDate is { } end)
            {
                return Ord(start) <= toOrd && Ord(end) >= fromOrd;
            }
            var o = Ord(start);
            return o >= fromOrd && o <= toOrd;
        }
        return p.Year >= fromOrd / 12 && p.Year <= toOrd / 12;
    }

    // Cost belongs to the booking year; a multi-year education buy contributes spend only in that year.
    public static bool CostsCountInWindow(Placement p, int fromOrd, int toOrd)
        => p.Year >= fromOrd / 12 && p.Year <= toOrd / 12;

    public static (int fromOrd, int toOrd) LiveSpan(Placement p)
    {
        if (p.StartDate is { } start)
        {
            return p.EndDate is { } end ? (Ord(start), Ord(end)) : (Ord(start), Ord(start));
        }
        if (p.LiveMonths.Length > 0)
        {
            return (Ord(p.Year, p.LiveMonths.Min()), Ord(p.Year, p.LiveMonths.Max()));
        }
        return (Ord(p.Year, 1), Ord(p.Year, 12));
    }

    public static decimal TargetFraction(Placement p, int fromOrd, int toOrd)
    {
        if (p.StartDate is { } start)
        {
            if (p.EndDate is { } end)
            {
                var startOrd = Ord(start);
                var endOrd = Ord(end);
                var total = endOrd - startOrd + 1;
                if (total <= 0) return 0m;
                var overlap = Math.Min(endOrd, toOrd) - Math.Max(startOrd, fromOrd) + 1;
                return overlap <= 0 ? 0m : (decimal)overlap / total;
            }
            var o = Ord(start);
            return o >= fromOrd && o <= toOrd ? 1m : 0m;
        }

        var months = p.LiveMonths.Length > 0 ? p.LiveMonths : Enumerable.Range(1, 12).ToArray();
        var inWindow = months.Count(m =>
        {
            var o = Ord(p.Year, m);
            return o >= fromOrd && o <= toOrd;
        });
        return (decimal)inWindow / months.Length;
    }
}
