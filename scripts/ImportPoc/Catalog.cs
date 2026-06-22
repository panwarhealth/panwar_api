using System.Text.RegularExpressions;

namespace Panwar.Tools.ImportPoc;

// The canonical vocabulary the parsed data must resolve to, mirrored from the
// live metric_field / template / audience tables. In the real Services/Import
// this is loaded from the DB per client; hardcoded here for the PoC.
internal static class Catalog
{
    // metric_template.Code name -> non-calculated metric_field.Key set (from prod).
    public static readonly Dictionary<string, string[]> TemplateKeys = new()
    {
        ["DigitalDisplay"] = new[] { "impressions", "clicks", "media_cost", "unique_impressions", "unique_clicks" },
        ["Edm"] = new[] { "sends", "opens", "clicks", "media_cost", "unique_opens", "unique_clicks", "downloads", "unique_downloads" },
        ["Print"] = new[] { "circulation", "placements_count", "media_cost" },
        ["SponsoredContent"] = new[] { "views", "organic_views", "downloads", "media_cost" },
        ["Education"] = new[] { "completions", "pending", "page_views", "media_cost", "unique_page_views", "downloads" },
    };

    // publisher slug -> default audience slug, where the publisher is single-audience.
    public static readonly Dictionary<string, string> PublisherAudience = new()
    {
        ["ajp"] = "pharmacists",
        ["ap"] = "pharmacists",
        ["pharmacy-club"] = "pharmacists",
        ["princeton"] = "pharmacists",
        ["ajgp"] = "gps",
        ["newsgp"] = "gps",
        ["healthed"] = "gps",
    };

    public static string? AudienceFor(string publisherSlug) => PublisherAudience.GetValueOrDefault(publisherSlug);

    public static bool IsValidMetric(string template, string key)
        => TemplateKeys.TryGetValue(template, out var keys) && keys.Contains(key);

    // Publisher label -> canonical metric key, e.g. "Total Sends" -> "sends".
    public static string NormalizeMetric(string label)
    {
        var s = label.Trim().ToLowerInvariant();
        s = s.Replace("total ", "");                       // "Total Sends" -> "sends"
        s = Regex.Replace(s, "[^a-z0-9]+", "_").Trim('_');  // "Unique Opens" -> "unique_opens"
        return s;
    }

    // A template IS its metric set, so the parsed metrics are the strongest signal
    // for which template a placement belongs to; the name is only a fallback when
    // the metrics are inconclusive. The preview lets a human override.
    public static string TemplateFromName(string name, IReadOnlyCollection<string> metricKeys)
    {
        if (metricKeys.Any(k => k == "impressions")) return "DigitalDisplay";
        if (metricKeys.Any(k => k is "sends" or "opens")) return "Edm";
        if (metricKeys.Any(k => k is "views" or "organic_views")) return "SponsoredContent";
        if (metricKeys.Any(k => k is "completions" or "page_views" or "pending")) return "Education";
        if (metricKeys.Any(k => k is "circulation" or "placements_count")) return "Print";

        var n = name.ToLowerInvariant();
        if (n.Contains("edm") || n.Contains("solus")) return "Edm";
        if (n.Contains("magazine") || n.Contains("print")) return "Print";
        if (n.Contains("banner") || n.Contains("mrec") || n.Contains("leaderboard") || n.Contains("display")) return "DigitalDisplay";
        if (n.Contains("sponsored") || n.Contains("advertorial") || n.Contains("article")) return "SponsoredContent";
        return "DigitalDisplay";
    }
}
