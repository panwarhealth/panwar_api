using System.Text.RegularExpressions;

namespace Panwar.Api.Services.Import;

internal static class MetricNaming
{
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]+", RegexOptions.Compiled);

    // "Total Sends " -> "sends"
    public static string Normalize(string label)
    {
        var s = label.Trim().ToLowerInvariant().Replace("total ", "");
        return NonAlphanumeric.Replace(s, "_").Trim('_');
    }
}
