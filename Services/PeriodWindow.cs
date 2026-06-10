using Panwar.Api.Models;

namespace Panwar.Api.Services;

/// <summary>
/// Month-granularity period helpers shared by the dashboard + summary services.
/// A month is encoded as an ordinal (year*12 + month-1) so windows compare with
/// simple integer arithmetic. Strings are "YYYY-MM".
///
/// Placement presence/targets branch on date shape (no template lookup needed):
///   StartDate + EndDate  → education range (month-span overlap)
///   StartDate only       → eDM send (single send month)
///   neither              → LiveMonths placements + legacy eDMs (by reporting Year)
/// </summary>
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

    /// <summary>
    /// Whether a placement's metrics/presence fall inside the window. Education
    /// ranges overlap; eDM sends land on a single month; everything else is
    /// scoped to its reporting year.
    /// </summary>
    public static bool AppearsInWindow(Placement p, int fromOrd, int toOrd)
    {
        if (p.StartDate is { } start)
        {
            if (p.EndDate is { } end)
            {
                // Education range: any month overlap.
                return Ord(start) <= toOrd && Ord(end) >= fromOrd;
            }
            // eDM send: the send month is in the window.
            var o = Ord(start);
            return o >= fromOrd && o <= toOrd;
        }
        // LiveMonths placements + legacy eDMs: belong to a reporting year.
        return p.Year >= fromOrd / 12 && p.Year <= toOrd / 12;
    }

    /// <summary>
    /// Whether a placement's cost counts toward the window. Cost always belongs
    /// to the booking year, so a multi-year education buy contributes its spend
    /// only in the year it was booked even though its metrics show across years.
    /// </summary>
    public static bool CostsCountInWindow(Placement p, int fromOrd, int toOrd)
        => p.Year >= fromOrd / 12 && p.Year <= toOrd / 12;

    /// <summary>
    /// The month-ordinal span a placement is live over: its date range, its send
    /// month, or its live months within its reporting year (whole year when no
    /// live months are recorded). Used to extend the selectable period span to
    /// planned placements that have no actuals yet.
    /// </summary>
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

    /// <summary>
    /// Fraction of a placement's annual KPI target attributable to the window.
    /// Education ranges pro-rate by month overlap; eDM sends are all-or-nothing;
    /// LiveMonths placements pro-rate by live months inside the window (placements
    /// with no live months recorded spread evenly over their reporting year).
    /// </summary>
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
