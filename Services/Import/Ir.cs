namespace Panwar.Api.Services.Import;

public sealed class ImportDocument
{
    public string ClientSlug { get; set; } = "";
    public int Year { get; set; }
    public List<SourceInfo> Sources { get; set; } = new();
    public List<ParsedPlacement> Placements { get; set; } = new();
    public List<ParsedEducationAsset> Education { get; set; } = new();
    public List<Warning> Warnings { get; set; } = new();
    public List<SheetSnapshot> Snapshot { get; set; } = new();
}

public sealed class SheetSnapshot
{
    public string File { get; set; } = "";
    public string Sheet { get; set; } = "";
    public int Rows { get; set; }
    public int Cols { get; set; }
    // "D7" -> cell text; empty cells omitted
    public Dictionary<string, string> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CellComment> Comments { get; set; } = new();
}

public sealed class CellComment
{
    public string Cell { get; set; } = "";
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
    public string? AudienceHint { get; set; }
    public string Publisher { get; set; } = "";
    public string Template { get; set; } = "";
    public string Name { get; set; } = "";
    public string Objective { get; set; } = "";
    public bool FromAi { get; set; }
    public List<ParsedActual> Actuals { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    // month (1-12) -> that month's note
    public Dictionary<int, string> MonthNotes { get; set; } = new();
}

public sealed class ParsedActual
{
    public string Metric { get; set; } = "";
    public int Month { get; set; }
    public decimal Value { get; set; }
    public string? Note { get; set; }
    public string? SourceSheet { get; set; }
    public string? SourceCell { get; set; }
}

public sealed class ParsedEducationAsset
{
    public string Source { get; set; } = "";
    public string? Group { get; set; }
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
