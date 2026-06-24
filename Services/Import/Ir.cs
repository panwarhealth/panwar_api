namespace Panwar.Api.Services.Import;

public sealed class ImportDocument
{
    public string ClientSlug { get; set; } = "";
    public int Year { get; set; }
    public List<SourceInfo> Sources { get; set; } = new();
    public List<ParsedPlacement> Placements { get; set; } = new();
    public List<ParsedEducationAsset> Education { get; set; } = new();
    public List<Warning> Warnings { get; set; } = new();
    // Bounded text snapshot of every sheet, so the AI can read a tab a note
    // points to ("refer to the X tab") without re-opening the file.
    public List<RawTab> RawTabs { get; set; } = new();
}

public sealed class RawTab
{
    public string File { get; set; } = "";
    public string Sheet { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class SourceInfo
{
    public string File { get; set; } = "";
    public string FormatId { get; set; } = "";
    public string Match { get; set; } = "";
}

public sealed class ParsedPlacement
{
    public string Source { get; set; } = "";
    public string Brand { get; set; } = "";
    public string? Audience { get; set; }
    public string Publisher { get; set; } = "";
    public string Template { get; set; } = "";
    public string Name { get; set; } = "";
    public string Objective { get; set; } = "";
    public List<ParsedActual> Actuals { get; set; } = new();
    // Human-written notes found anywhere in this placement's block. These guide
    // the reader to the truth and take priority over the raw cells.
    public List<string> Notes { get; set; } = new();
    // Notes keyed by month (1-12) - each month's note names that send's topic /
    // real date (e.g. 3 -> "MSK Pain - w/c 16th March"), how Gabe splits one
    // block into per-send placements. Captured even for months with no value.
    public Dictionary<int, string> MonthNotes { get; set; } = new();
}

public sealed class ParsedActual
{
    public string Metric { get; set; } = "";
    public int Month { get; set; }
    public decimal Value { get; set; }
    public string? Note { get; set; }
}

public sealed class ParsedEducationAsset
{
    public string Source { get; set; } = "";
    public string Brand { get; set; } = "";
    public string? Type { get; set; }
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Expiry { get; set; }
    public List<ParsedEducationValue> Values { get; set; } = new();
}

public sealed class ParsedEducationValue
{
    public string Status { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Value { get; set; }
}

public sealed class Warning
{
    public string Level { get; set; } = "warn";
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
}
