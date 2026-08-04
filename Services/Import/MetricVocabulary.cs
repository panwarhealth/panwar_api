namespace Panwar.Api.Services.Import;

public sealed class MetricVocabulary
{
    public static readonly MetricVocabulary Empty = new(new Dictionary<string, IReadOnlySet<string>>());

    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _byTemplate;

    public MetricVocabulary(IReadOnlyDictionary<string, IReadOnlySet<string>> byTemplate)
        => _byTemplate = byTemplate;

    public IReadOnlyCollection<string> AllKeys
        => _byTemplate.Values.SelectMany(v => v).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();

    public string InferTemplate(string name, IReadOnlyCollection<string> metricKeys)
    {
        var best = _byTemplate
            .Select(t => (Template: t.Key, Score: metricKeys.Count(k => t.Value.Contains(k))))
            .Where(t => t.Score > 0)
            .OrderByDescending(t => t.Score)
            .ToList();
        if (best.Count > 0 && (best.Count == 1 || best[0].Score > best[1].Score)) return best[0].Template;
        return TemplateFromName(name);
    }

    private static string TemplateFromName(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("edm") || n.Contains("solus")) return "Edm";
        if (n.Contains("magazine") || n.Contains("print")) return "Print";
        if (n.Contains("banner") || n.Contains("mrec") || n.Contains("leaderboard") || n.Contains("display")) return "DigitalDisplay";
        if (n.Contains("sponsored") || n.Contains("advertorial") || n.Contains("article")) return "SponsoredContent";
        return "DigitalDisplay";
    }
}
