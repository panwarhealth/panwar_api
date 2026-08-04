using Panwar.Api.Models;

namespace Panwar.Api.Services.Import;

internal static class ImportTaxonomyResolver
{
    public static void Resolve(
        ImportDocument doc,
        IReadOnlyList<Publisher> publishers,
        IReadOnlyList<Brand> brands,
        IReadOnlyList<Audience> audiences,
        IReadOnlyList<Placement> existing)
    {
        var publisherKeys = publishers
            .SelectMany(p => new[] { (p.Slug, Key: Norm(p.Name)), (p.Slug, Key: Norm(p.Slug)) })
            .Where(t => t.Key.Length >= 2)
            .Distinct()
            .OrderByDescending(t => t.Key.Length)
            .ToList();

        var audiencesByPublisher = existing
            .GroupBy(p => p.Publisher.Slug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => p.Audience.Slug).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var pp in doc.Placements)
        {
            if (pp.Publisher.Length == 0)
                pp.Publisher = MatchSlug(publisherKeys, pp.Name) ?? MatchSlug(publisherKeys, pp.Source) ?? "";
            if (pp.Brand.Length == 0)
                pp.Brand = MatchBrand(pp.Name, brands) ?? SoleBrand(brands);
            pp.Audience ??= MatchAudience(pp.AudienceHint, audiences)
                ?? SoleAudienceFor(pp.Publisher, audiencesByPublisher)
                ?? (audiences.Count == 1 ? audiences[0].Slug : null);

            if (pp.Publisher.Length == 0)
                doc.Warnings.Add(new Warning { Source = pp.Source, Message = $"Could not work out the publisher for '{pp.Name}' - pick where it goes in the preview" });
            else if (pp.Audience is null)
                doc.Warnings.Add(new Warning { Source = pp.Source, Message = $"Could not work out the audience for '{pp.Name}' - pick it in the preview" });
        }

        foreach (var ea in doc.Education)
            if (ea.Brand.Length == 0)
                ea.Brand = MatchBrand(ea.Title, brands) ?? SoleBrand(brands);
    }

    private static string? MatchSlug(IReadOnlyList<(string Slug, string Key)> keys, string text)
    {
        var norm = Norm(text);
        foreach (var (slug, key) in keys)
            if (ContainsToken(norm, key)) return slug;
        return null;
    }

    private static string? MatchBrand(string name, IReadOnlyList<Brand> brands)
    {
        var text = Norm(name);
        var hits = brands.Where(b => ContainsToken(text, Norm(b.Name))).ToList();
        return hits.Count == 1 ? hits[0].Name : null;
    }

    private static string SoleBrand(IReadOnlyList<Brand> brands)
        => brands.Count == 1 ? brands[0].Name : "";

    // "GP DATABASE 2026" -> the client's matching audience slug
    private static string? MatchAudience(string? hint, IReadOnlyList<Audience> audiences)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;
        var text = Norm(hint);
        var hits = audiences
            .Where(a => ContainsToken(text, Norm(a.Name)) || ContainsToken(text, Norm(a.Slug)))
            .Select(a => a.Slug)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    private static string? SoleAudienceFor(string publisherSlug, Dictionary<string, List<string>> byPublisher)
        => publisherSlug.Length > 0 && byPublisher.TryGetValue(publisherSlug, out var slugs) && slugs.Count == 1
            ? slugs[0] : null;

    // Whole-word containment.
    private static bool ContainsToken(string text, string key)
    {
        if (key.Length == 0) return false;
        int i = 0;
        while ((i = text.IndexOf(key, i, StringComparison.Ordinal)) >= 0)
        {
            bool startOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            bool endOk = i + key.Length == text.Length || !char.IsLetterOrDigit(text[i + key.Length]);
            if (startOk && endOk) return true;
            i++;
        }
        return false;
    }

    private static string Norm(string? s) => Spreadsheet.NormalizeName(s);
}
