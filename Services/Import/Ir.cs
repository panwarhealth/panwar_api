namespace Panwar.Api.Services.Import;

public sealed class ImportDocument
{
    public string ClientSlug { get; set; } = "";
    public int Year { get; set; }
    public List<SourceInfo> Sources { get; set; } = new();
    public List<ParsedPlacement> Placements { get; set; } = new();
    public List<ParsedEducationAsset> Education { get; set; } = new();
    public List<Warning> Warnings { get; set; } = new();
    // Whole-workbook snapshot (every sheet: a bounded sparse cell map + all Excel
    // cell-comments with coordinates). This is what the agentic AI tools read from
    // (list_tabs/read_tab/read_cells/read_comments) and the ground truth the
    // grounding pass re-reads to verify every value the AI cites.
    public List<SheetSnapshot> Snapshot { get; set; } = new();
}

public sealed class SheetSnapshot
{
    public string File { get; set; } = "";
    public string Sheet { get; set; } = "";
    public int Rows { get; set; }
    public int Cols { get; set; }
    // A1 cell reference (e.g. "D7") -> trimmed text value. Sparse: empty cells omitted.
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
    public string? Group { get; set; }    // publisher block heading ("AP", "Pharmacy Club") - the page table's GroupLabel
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
