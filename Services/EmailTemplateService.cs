using System.Collections.Concurrent;
using System.Net;

namespace Panwar.Api.Services;

public class EmailTemplateService : IEmailTemplateService
{
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public string Render(
        string templateFile,
        IReadOnlyDictionary<string, string> tokens,
        IReadOnlyDictionary<string, string>? rawTokens = null)
    {
        var html = _cache.GetOrAdd(templateFile, Load);
        foreach (var (key, value) in tokens)
        {
            html = html.Replace("{{" + key + "}}", WebUtility.HtmlEncode(value));
        }
        if (rawTokens is not null)
        {
            foreach (var (key, value) in rawTokens)
            {
                html = html.Replace("{{" + key + "}}", value);
            }
        }
        return html;
    }

    private static string Load(string templateFile)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "EmailTemplates", templateFile);
        return File.ReadAllText(path);
    }
}
