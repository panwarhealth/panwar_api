using System.Text.RegularExpressions;

namespace Panwar.Api.Services.Timesheets;

public static class TsJobCodes
{
    private const int MinDigits = 4;

    public static string Next(string prefix, IEnumerable<string> existingCodes)
    {
        var pattern = new Regex($"^{Regex.Escape(prefix)}([0-9]+)$", RegexOptions.IgnoreCase);
        var numbers = existingCodes.Select(c => pattern.Match(c)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        var max = numbers.Count == 0 ? 0 : numbers.Max(long.Parse);
        var width = Math.Max(MinDigits, numbers.Count == 0 ? 0 : numbers.Max(n => n.Length));
        return prefix.ToUpperInvariant() + (max + 1).ToString().PadLeft(width, '0');
    }
}
