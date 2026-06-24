using ClosedXML.Excel;

namespace Panwar.Api.Services.Import;

public enum AdapterMatch { None, Weak, Strong }

public sealed class ParseContext
{
    public string ClientSlug { get; init; } = "";
    public int Year { get; init; }
    public string FileName { get; init; } = "";
}

public interface IWorkbookAdapter
{
    string FormatId { get; }
    AdapterMatch Detect(IXLWorkbook wb);
    void Parse(IXLWorkbook wb, ParseContext ctx, ImportDocument doc);
}

public sealed class ImportParser
{
    private readonly IReadOnlyList<IWorkbookAdapter> _adapters;

    public ImportParser(IReadOnlyList<IWorkbookAdapter> adapters) => _adapters = adapters;

    public static ImportParser Default() => new(new IWorkbookAdapter[]
    {
        new ResultsTemplateAdapter(),
        new PrincetonAdapter(),
        new PharmacyClubAdapter(),
        new ResearchReviewAdapter(),
        new SolusEdmAdapter(),
        new ReckittReportAdapter(),
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
        SnapshotTabs(wb, ctx, doc);
    }

    // Bounded text snapshot of each sheet so the AI can read a tab a note refers
    // to. Capped to keep the prompt small; the Lookup helper sheet is skipped.
    private static void SnapshotTabs(IXLWorkbook wb, ParseContext ctx, ImportDocument doc)
    {
        const int maxRows = 60, maxCols = 16;
        foreach (var ws in wb.Worksheets)
        {
            if (ws.Name.Trim().Equals("Lookup", StringComparison.OrdinalIgnoreCase)) continue;
            int lastRow = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 0, maxRows);
            int lastCol = Math.Min(ws.LastColumnUsed()?.ColumnNumber() ?? 0, maxCols);
            if (lastRow == 0 || lastCol == 0) continue;

            var sb = new System.Text.StringBuilder();
            for (int r = 1; r <= lastRow; r++)
            {
                var cells = new List<string>();
                for (int c = 1; c <= lastCol; c++)
                {
                    var s = Spreadsheet.ReadString(ws.Cell(r, c));
                    if (s is not null) cells.Add($"{(char)('A' + c - 1)}={s}");
                }
                if (cells.Count > 0) sb.Append('r').Append(r).Append(": ").AppendLine(string.Join(" | ", cells));
            }
            doc.RawTabs.Add(new RawTab { File = ctx.FileName, Sheet = ws.Name.Trim(), Text = sb.ToString() });
        }
    }
}
