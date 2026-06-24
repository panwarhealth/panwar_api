using System.Text.RegularExpressions;

namespace Panwar.Api.Services.Import;

internal static class Catalog
{
    private static readonly Regex MetricLabelClean = new("[^a-z0-9]+", RegexOptions.Compiled);

    public static readonly Dictionary<string, string[]> TemplateKeys = new()
    {
        ["DigitalDisplay"] = new[] { "impressions", "clicks", "media_cost", "unique_impressions", "unique_clicks" },
        ["Edm"] = new[] { "sends", "opens", "clicks", "media_cost", "unique_opens", "unique_clicks", "downloads", "unique_downloads" },
        ["Print"] = new[] { "circulation", "placements_count", "media_cost" },
        ["SponsoredContent"] = new[] { "views", "organic_views", "downloads", "media_cost" },
        ["Education"] = new[] { "completions", "pending", "page_views", "media_cost", "unique_page_views", "downloads" },
    };

    public static readonly Dictionary<string, string> PublisherAudience = new()
    {
        ["ajp"] = "pharmacists",
        ["ap"] = "pharmacists",
        ["pharmacy-club"] = "pharmacists",
        ["princeton"] = "pharmacists",
        ["ajgp"] = "gps",
        ["newsgp"] = "gps",
        ["healthed"] = "gps",
        ["arterial"] = "gps",
        ["adg"] = "gps",
        ["medicine-today"] = "gps",
    };

    public static string? AudienceFor(string publisherSlug) => PublisherAudience.GetValueOrDefault(publisherSlug);

    public static bool IsValidMetric(string template, string key)
        => TemplateKeys.TryGetValue(template, out var keys) && keys.Contains(key);

    public static string NormalizeMetric(string label)
    {
        var s = label.Trim().ToLowerInvariant();
        s = s.Replace("total ", "");
        s = MetricLabelClean.Replace(s, "_").Trim('_');
        return s;
    }

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
