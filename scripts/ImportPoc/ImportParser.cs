using ClosedXML.Excel;

namespace Panwar.Tools.ImportPoc;

public enum AdapterMatch { None, Weak, Strong }

public sealed class ParseContext
{
    public string ClientSlug { get; init; } = "";
    public int Year { get; init; }
    public string FileName { get; init; } = "";
}

// One adapter per format "model". Pure - reads a workbook, emits the canonical IR, never writes a DB.
public interface IWorkbookAdapter
{
    string FormatId { get; }
    AdapterMatch Detect(IXLWorkbook wb);
    void Parse(IXLWorkbook wb, ParseContext ctx, ImportDocument doc);
}

// Dispatcher: detect the strongest-matching adapter for each file, route, merge.
public sealed class ImportParser
{
    private readonly IReadOnlyList<IWorkbookAdapter> _adapters;

    public ImportParser(IReadOnlyList<IWorkbookAdapter> adapters) => _adapters = adapters;

    public static ImportParser Default() => new(new IWorkbookAdapter[]
    {
        new ResultsTemplateAdapter(),
        new PrincetonAdapter(),
    });

    public ImportDocument ParseFile(IXLWorkbook wb, ParseContext ctx)
    {
        var doc = new ImportDocument { ClientSlug = ctx.ClientSlug, Year = ctx.Year };
        ParseInto(wb, ctx, doc);
        return doc;
    }

    public void ParseInto(IXLWorkbook wb, ParseContext ctx, ImportDocument doc)
    {
        IWorkbookAdapter? best = null;
        var bestMatch = AdapterMatch.None;
        foreach (var a in _adapters)
        {
            var m = a.Detect(wb);
            if (m > bestMatch) { bestMatch = m; best = a; }
        }

        if (best is null || bestMatch == AdapterMatch.None)
        {
            doc.Sources.Add(new SourceInfo { File = ctx.FileName, FormatId = "unrecognised", Match = "none" });
            doc.Warnings.Add(new Warning { Source = ctx.FileName, Message = "No adapter recognised this file's format" });
            return;
        }

        doc.Sources.Add(new SourceInfo { File = ctx.FileName, FormatId = best.FormatId, Match = bestMatch.ToString() });
        best.Parse(wb, ctx, doc);
    }
}
