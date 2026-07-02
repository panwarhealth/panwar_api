using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Panwar.Api.Services.Import;

internal static class Spreadsheet
{
    private static readonly Regex CollapseWhitespace = new(@"\s+", RegexOptions.Compiled);

    // Whitespace-insensitive, case-insensitive form used to match names across the
    // file, the DB and the snapshot.
    public static string NormalizeName(string? s) => CollapseWhitespace.Replace((s ?? "").Trim().ToLowerInvariant(), " ");

    public static string? ReadString(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var value = cell.Value;
        if (value.IsBlank) return null;
        var text = value.ToString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    // Like ReadString, but date cells come out human ("23 Mar 2026") instead of the
    // raw "23/03/2026 12:00:00 AM" - for notes and on-card grids the user reads.
    public static string? ReadDisplayString(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var value = cell.Value;
        if (value.IsDateTime)
        {
            var dt = value.GetDateTime();
            return dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("d MMM yyyy") : dt.ToString("d MMM yyyy h:mm tt");
        }
        return ReadString(cell);
    }

    public static decimal? ReadDecimal(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var value = cell.Value;
        if (value.IsBlank) return null;
        if (value.IsNumber) return (decimal)value.GetNumber();
        if (value.IsText)
        {
            var text = value.GetText().Trim();
            if (string.IsNullOrEmpty(text) || text == "-") return null;
            if (decimal.TryParse(text, out var d)) return d;
        }
        return null;
    }

    public static DateTime? ReadDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var value = cell.Value;
        if (value.IsDateTime) return value.GetDateTime();
        if (value.IsNumber)
        {
            try { return DateTime.FromOADate(value.GetNumber()); } catch { return null; }
        }
        return null;
    }

    // ReadDate as ISO yyyy-MM-dd, with a fallback for dates typed as text. Text is
    // parsed day-first ("23/03/2026", "23 Mar 2026" - these are Aussie workbooks),
    // never with the invariant month-first rule that would read 03/06 as March 6.
    public static string? ReadDateIso(IXLCell cell)
    {
        if (ReadDate(cell) is { } dt) return DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd");
        var text = ReadString(cell);
        if (text is null) return null;
        return DateOnly.TryParse(text, AustralianCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd") : null;
    }

    private static readonly System.Globalization.CultureInfo AustralianCulture = new("en-AU");

    // 1 -> "A", 26 -> "Z", 27 -> "AA". Handles the >26 columns a wide snapshot needs.
    public static string ColLetter(int col)
    {
        var s = "";
        while (col > 0)
        {
            int rem = (col - 1) % 26;
            s = (char)('A' + rem) + s;
            col = (col - 1) / 26;
        }
        return s;
    }

    // Inverse of ColLetter: "D7" -> (7, 4). Null for anything that isn't an A1 ref.
    public static (int Row, int Col)? ParseA1(string a1)
    {
        int i = 0;
        while (i < a1.Length && char.IsLetter(a1[i])) i++;
        if (i == 0 || i >= a1.Length) return null;
        if (!int.TryParse(a1[i..], out var row) || row < 1) return null;
        int col = 0;
        foreach (var ch in a1[..i]) col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return (row, col);
    }

    private static readonly string[] MetricMarkers =
        { "send", "open", "click", "view", "impression", "session", "reach", "completion", "enrol" };

    public static bool LooksLikeMetricLabel(string? s)
        => s is not null && MetricMarkers.Any(m => s.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static string? ResolvePublisher(string name)
    {
        var u = name.ToUpperInvariant();
        if (u.StartsWith("AJP")) return "ajp";
        if (u.StartsWith("AP ") || u.StartsWith("AP-") || u == "AP") return "ap";
        if (u.StartsWith("ARTERIAL")) return "arterial";
        if (u.StartsWith("HEALTHED")) return "healthed";
        if (u.StartsWith("AJGP") || u.StartsWith("RACGP")) return "ajgp";
        if (u.StartsWith("ADG")) return "adg";
        if (u.StartsWith("PRINCETON")) return "princeton";
        if (u.StartsWith("RESEARCH REVIEW")) return "research-review";
        if (u.StartsWith("NEWSGP")) return "newsgp";
        if (u.StartsWith("MT ") || u.StartsWith("MEDICAL TODAY") || u.StartsWith("MEDICINE TODAY")) return "medicine-today";
        if (u.StartsWith("PRAXHUB")) return "praxhub";
        return null;
    }

    public static string InferObjective(string name)
    {
        var l = name.ToLowerInvariant();
        if (l.Contains("cpd") || l.Contains("article") || l.Contains("webinar") || l.Contains("podcast"))
            return "Engagement";
        if (l.Contains("cta to") || l.Contains("cta -")) return "Consideration";
        return "Awareness";
    }
}
