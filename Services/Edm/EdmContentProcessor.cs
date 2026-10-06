using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Edm;

/// <summary>
/// Turns an eDM build (a zip of index.html + images/, or a bare .html) into the HTML we store.
/// Pure — no IO beyond the bytes passed in — so it's easy to reason about and test. Image upload
/// happens in the caller, which hands back the public URL for each relative path via
/// <see cref="RewriteImages"/>.
/// </summary>
public static class EdmContentProcessor
{
    // Gmail clips messages over ~102KB, hiding the footer (and our unsubscribe link) behind "View entire message".
    public const int GmailClipBytes = 102 * 1024;

    private static readonly Regex ImageRef = new(
        "(?<pre>\\b(?:src|background)\\s*=\\s*[\"'])(?<url>[^\"']+)(?<post>[\"'])|(?<pre>url\\(\\s*['\"]?)(?<url>[^'\")]+)(?<post>['\"]?\\s*\\))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Href = new("\\bhref\\s*=\\s*[\"'](?<url>[^\"']+)[\"']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Title = new("<title[^>]*>(?<t>.*?)</title>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // MJML's <mj-preview> renders as the first hidden div after <body>; some builds add another
    // hidden text-only div further down. A text-only hidden div in an email is always a preheader, so
    // we lift the first one's text into the campaign's PreviewText and strip them all: the send-time
    // preheader is then the only one, and editing it can't leave a stale one behind.
    private static readonly Regex Preheader = new(
        "<body[^>]*>\\s*<div[^>]*style=\"display:\\s*none;[^\"]*\"[^>]*>(?<text>.*?)</div>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex HiddenDiv = new(
        "<div[^>]*style=\"display:\\s*none;[^\"]*\"[^>]*>(?<text>[^<]*)</div>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UtmCampaign = new("[?&]utm_campaign=(?<c>[^&#]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex JobCode = new("^(?<c>[A-Za-z]{2,5}[0-9]{4,5})", RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new("\\s+", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
    };

    public sealed record ImageFile(string Path, byte[] Bytes, string ContentType);

    public sealed record Unpacked(string Html, string? Text, IReadOnlyDictionary<string, ImageFile> Images);

    public sealed record Prepared(string Html, string? Title, string? PreviewText, string? CampaignCode);

    /// <summary>Reads the upload. Throws <see cref="EdmValidationException"/> for anything unusable.</summary>
    public static Unpacked Unpack(byte[] bytes, string fileName)
    {
        if (fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            return new Unpacked(Decode(bytes), null, new Dictionary<string, ImageFile>());

        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new EdmValidationException("Upload a .zip from the eDM build, or a single .html file");

        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read); }
        catch (InvalidDataException) { throw new EdmValidationException("That zip file couldn't be opened"); }

        using (zip)
        {
            var entries = zip.Entries
                .Where(e => e.Length > 0 && !e.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal))
                .ToList();

            // Prefer index.html, then the shallowest .html — builds sometimes zip the export folder itself.
            var htmlEntry = entries
                .Where(e => e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(e => e.FullName.Count(c => c == '/'))
                .FirstOrDefault()
                ?? throw new EdmValidationException("No .html file found in the zip");

            var root = htmlEntry.FullName.Contains('/') ? htmlEntry.FullName[..(htmlEntry.FullName.LastIndexOf('/') + 1)] : "";
            var html = Decode(ReadAll(htmlEntry));

            var textEntry = entries.FirstOrDefault(e =>
                e.FullName.StartsWith(root, StringComparison.Ordinal)
                && e.Name.Equals("text-version.txt", StringComparison.OrdinalIgnoreCase));
            var text = textEntry is null ? null : Decode(ReadAll(textEntry));

            var images = new Dictionary<string, ImageFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!entry.FullName.StartsWith(root, StringComparison.Ordinal)) continue;
                if (!ImageTypes.TryGetValue(Path.GetExtension(entry.Name), out var type)) continue;
                var relative = entry.FullName[root.Length..];
                images[relative] = new ImageFile(relative, ReadAll(entry), type);
            }

            return new Unpacked(html, text, images);
        }
    }

    /// <summary>Distinct relative image paths the HTML points at, normalised for lookup in the zip.</summary>
    public static IReadOnlyCollection<string> ReferencedImages(string html) =>
        ImageRef.Matches(html)
            .Select(m => m.Groups["url"].Value.Trim())
            .Where(IsRelative)
            .Select(NormalisePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces relative image references with the URL the resolver returns. References the
    /// resolver can't place (null) are left as-is so <see cref="Analyse"/> reports them missing.
    /// </summary>
    public static string RewriteImages(string html, Func<string, string?> resolve)
    {
        return ImageRef.Replace(html, m =>
        {
            var url = m.Groups["url"].Value.Trim();
            if (!IsRelative(url)) return m.Value;
            var replacement = resolve(NormalisePath(url));
            return replacement is null ? m.Value : m.Groups["pre"].Value + replacement + m.Groups["post"].Value;
        });
    }

    /// <summary>Lifts title, preheader and campaign code out of the HTML and strips the preheader.</summary>
    public static Prepared Prepare(string html, string fileName)
    {
        var title = Title.Match(html) is { Success: true } t ? CleanText(t.Groups["t"].Value) : null;

        var preview = Preheader.Match(html) is { Success: true } pre ? CleanText(pre.Groups["text"].Value) : null;
        if (!string.IsNullOrEmpty(preview)) html = HiddenDiv.Replace(html, "");

        var code = Href.Matches(html)
            .Select(m => UtmCampaign.Match(WebUtility.HtmlDecode(m.Groups["url"].Value)))
            .Where(m => m.Success)
            .Select(m => Uri.UnescapeDataString(m.Groups["c"].Value))
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        code ??= JobCode.Match(Path.GetFileNameWithoutExtension(fileName)) is { Success: true } j
            ? j.Groups["c"].Value.ToLowerInvariant()
            : null;

        return new Prepared(html, string.IsNullOrEmpty(title) ? null : title, string.IsNullOrEmpty(preview) ? null : preview, code);
    }

    public static EdmContentAnalysis Analyse(string html, bool hasText)
    {
        var links = Href.Matches(html)
            .Select(m => WebUtility.HtmlDecode(m.Groups["url"].Value.Trim()))
            .Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Where(u => !EdmMessageBuilder.ContainsMergeTag(u))
            .ToList();

        var missingUtm = links
            .Where(u => !UtmCampaign.IsMatch(u))
            .Distinct()
            .ToList();

        var imageUrls = ImageRef.Matches(html).Select(m => m.Groups["url"].Value.Trim()).ToList();
        var missing = ReferencedImages(html).ToList();
        var insecure = imageUrls.Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();

        var bytes = Encoding.UTF8.GetByteCount(html);
        var hasUnsub = EdmMessageBuilder.HasUnsubscribeTag(html);

        var warnings = new List<string>();
        if (missing.Count > 0)
            warnings.Add($"{missing.Count} image{(missing.Count == 1 ? " isn't" : "s aren't")} in the upload and will show as broken");
        if (insecure.Count > 0)
            warnings.Add($"{insecure.Count} image{(insecure.Count == 1 ? " loads" : "s load")} over http:// and may be blocked");
        if (missingUtm.Count > 0)
            warnings.Add($"{missingUtm.Count} link{(missingUtm.Count == 1 ? " has" : "s have")} no utm_campaign, so clicks won't show in Google Analytics");
        // Leave headroom for the preheader, footer and pixel added at send time.
        if (bytes + 3_000 > GmailClipBytes)
            warnings.Add($"The HTML is {bytes / 1024} KB. Gmail clips emails over 102 KB, hiding the footer and unsubscribe link");

        return new EdmContentAnalysis(bytes, links.Count, imageUrls.Count, hasText, hasUnsub, missingUtm, missing, warnings);
    }

    public static bool IsRelative(string url) =>
        !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
          || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
          || url.StartsWith("//", StringComparison.Ordinal)
          || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
          || url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)
          || url.StartsWith("#", StringComparison.Ordinal)
          || EdmMessageBuilder.ContainsMergeTag(url));

    public static string NormalisePath(string url)
    {
        var path = WebUtility.HtmlDecode(url).Split('?', '#')[0].Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        return Uri.UnescapeDataString(path.TrimStart('/'));
    }

    private static string CleanText(string html) =>
        Whitespace.Replace(WebUtility.HtmlDecode(Tags.Replace(html, " ")).Replace('\u034F', ' ').Replace('\u200C', ' ').Replace('\u00A0', ' '), " ").Trim();

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Decode(byte[] bytes) =>
        new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');
}

public class EdmValidationException(string message) : Exception(message);
