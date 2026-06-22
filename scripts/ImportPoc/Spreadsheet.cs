using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Panwar.Tools.ImportPoc;

// Format-agnostic spreadsheet primitives, lifted/generalised from the recovered
// Reckitt seeder. Reused by every adapter.
internal static class Spreadsheet
{
    public static string? ReadString(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var value = cell.Value;
        if (value.IsBlank) return null;
        var text = value.ToString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
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

    private static readonly string[] MetricMarkers =
        { "send", "open", "click", "view", "impression", "session", "reach", "completion", "enrol" };

    // A placement block header has a brand in col C and a metric label in col D
    // (numeric summary/month rows never do).
    public static bool LooksLikeMetricLabel(string? s)
        => s is not null && MetricMarkers.Any(m => s.Contains(m, StringComparison.OrdinalIgnoreCase));

    // Map a placement name to the canonical publisher slug (ported from the seeder).
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
        if (u.StartsWith("NEWSGP")) return "newsgp";
        if (u.StartsWith("MT ") || u.StartsWith("MEDICAL TODAY")) return "medical-today";
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
