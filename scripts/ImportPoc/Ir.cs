namespace Panwar.Tools.ImportPoc;

// Canonical Intermediate Representation - the single shape every adapter emits.
// Pure data, no DB/EF dependency, so it lifts straight into Services/Import later.

public sealed class ImportDocument
{
    public string ClientSlug { get; set; } = "";
    public int Year { get; set; }
    public List<SourceInfo> Sources { get; set; } = new();
    public List<ParsedPlacement> Placements { get; set; } = new();
    public List<ParsedEducationAsset> Education { get; set; } = new();
    public List<Warning> Warnings { get; set; } = new();
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
