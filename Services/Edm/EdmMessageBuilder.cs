using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Panwar.Api.Models;

namespace Panwar.Api.Services.Edm;

/// <summary>
/// Builds the per-recipient email from a campaign's stored HTML: merge tags, the hidden preheader,
/// the unsubscribe footer (unless the build placed its own link), and the open pixel.
/// Merge tags: {{first_name}}, {{last_name}}, {{email}}, {{unsubscribe_url}}, with an optional
/// fallback ({{first_name|there}}). Mailchimp's *|FNAME|*, *|LNAME|*, *|EMAIL|*, *|UNSUB|* also work
/// because older builds use them.
/// </summary>
public static class EdmMessageBuilder
{
    private static readonly Regex Curly = new(
        "\\{\\{\\s*(?<tag>first_name|last_name|email|unsubscribe_url)\\s*(?:\\|(?<fallback>[^}]*))?\\}\\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Mailchimp = new("\\*\\|(?<tag>FNAME|LNAME|EMAIL|UNSUB)\\|\\*", RegexOptions.Compiled);
    private static readonly Regex BodyOpen = new("<body[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BodyClose = new("</body>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public sealed record Personalisation(string Email, string? FirstName, string? LastName, string UnsubscribeUrl, string? PixelUrl);

    public sealed record Built(string Subject, string Html, string? Text);

    public static bool ContainsMergeTag(string s) => Curly.IsMatch(s) || Mailchimp.IsMatch(s);

    public static bool HasUnsubscribeTag(string html) =>
        Curly.Matches(html).Any(m => m.Groups["tag"].Value.Equals("unsubscribe_url", StringComparison.OrdinalIgnoreCase))
        || html.Contains("*|UNSUB|*", StringComparison.Ordinal);

    public static Built Build(EdmCampaign campaign, EdmSender sender, Personalisation p)
    {
        var html = campaign.HtmlBody ?? "";
        var ownUnsubscribe = HasUnsubscribeTag(html);

        html = ReplaceTags(html, p, htmlEncode: true);
        html = InsertAfterBodyOpen(html, PreheaderHtml(campaign.PreviewText));

        var tail = new StringBuilder();
        if (!ownUnsubscribe) tail.Append(FooterHtml(sender, p.UnsubscribeUrl));
        if (p.PixelUrl is not null)
            tail.Append($"<img src=\"{Attr(p.PixelUrl)}\" width=\"1\" height=\"1\" alt=\"\" style=\"display:block;width:1px;height:1px;border:0;\" />");
        html = InsertBeforeBodyClose(html, tail.ToString());

        string? text = null;
        if (!string.IsNullOrWhiteSpace(campaign.TextBody))
        {
            text = ReplaceTags(campaign.TextBody, p, htmlEncode: false);
            if (!HasUnsubscribeTag(campaign.TextBody))
                text = $"{text.TrimEnd()}\n\n---\n{sender.FooterText}\nUnsubscribe: {p.UnsubscribeUrl}\n";
        }

        var subject = ReplaceTags(campaign.Subject ?? "", p, htmlEncode: false);
        return new Built(subject, html, text);
    }

    private static string ReplaceTags(string s, Personalisation p, bool htmlEncode)
    {
        string Enc(string v) => htmlEncode ? WebUtility.HtmlEncode(v) : v;

        s = Curly.Replace(s, m =>
        {
            var fallback = m.Groups["fallback"].Success ? m.Groups["fallback"].Value.Trim() : "";
            return m.Groups["tag"].Value.ToLowerInvariant() switch
            {
                "first_name" => Enc(Blank(p.FirstName) ?? fallback),
                "last_name" => Enc(Blank(p.LastName) ?? fallback),
                "email" => Enc(p.Email),
                _ => htmlEncode ? Attr(p.UnsubscribeUrl) : p.UnsubscribeUrl,
            };
        });

        return Mailchimp.Replace(s, m => m.Groups["tag"].Value switch
        {
            "FNAME" => Enc(p.FirstName ?? ""),
            "LNAME" => Enc(p.LastName ?? ""),
            "EMAIL" => Enc(p.Email),
            _ => htmlEncode ? Attr(p.UnsubscribeUrl) : p.UnsubscribeUrl,
        });
    }

    private static string PreheaderHtml(string? preview)
    {
        if (string.IsNullOrWhiteSpace(preview)) return "";
        // The zero-width filler stops inboxes pulling body copy into the preview after our text.
        var filler = string.Concat(Enumerable.Repeat("&#847;&zwnj;&nbsp;", 90));
        return "<div style=\"display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all;\">"
            + WebUtility.HtmlEncode(preview.Trim()) + filler + "</div>";
    }

    private static string FooterHtml(EdmSender sender, string unsubscribeUrl) =>
        "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>"
        + "<td align=\"center\" style=\"padding:24px 16px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#6b6b6b;\">"
        + WebUtility.HtmlEncode(sender.FooterText)
        + $" <a href=\"{Attr(unsubscribeUrl)}\" style=\"color:{Attr(sender.BrandColour)};text-decoration:underline;\">Unsubscribe</a>"
        + "</td></tr></table>";

    private static string InsertAfterBodyOpen(string html, string fragment)
    {
        if (fragment.Length == 0) return html;
        var m = BodyOpen.Match(html);
        return m.Success ? html.Insert(m.Index + m.Length, fragment) : fragment + html;
    }

    private static string InsertBeforeBodyClose(string html, string fragment)
    {
        if (fragment.Length == 0) return html;
        var matches = BodyClose.Matches(html);
        return matches.Count > 0 ? html.Insert(matches[^1].Index, fragment) : html + fragment;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Attr(string s) => WebUtility.HtmlEncode(s);
}
