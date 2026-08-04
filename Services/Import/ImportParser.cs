using ClosedXML.Excel;

namespace Panwar.Api.Services.Import;

public enum AdapterMatch { None, Weak, Strong }

public sealed class ParseContext
{
    public string ClientSlug { get; init; } = "";
    public int Year { get; init; }
    public string FileName { get; init; } = "";
    public MetricVocabulary Vocabulary { get; init; } = MetricVocabulary.Empty;
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
        new MonthTabsAdapter(),
        new EducationResultsAdapter(),
        new AudienceDatabaseAdapter(),
        new EmailCampaignReportAdapter(),
        new PortalReportAdapter(),
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
            int before = doc.Placements.Count + doc.Education.Count;
            doc.Sources.Add(new SourceInfo { File = ctx.FileName, FormatId = "generic", Match = "Fallback" });
            foreach (var ws in wb.Worksheets)
            {
                if (ws.Visibility != XLWorksheetVisibility.Visible) continue;
                PlacementBlocks.Parse(ws, ctx, doc);
            }
            if (doc.Placements.Count + doc.Education.Count == before)
                doc.Warnings.Add(new Warning
                {
                    Source = ctx.FileName,
                    Message = "No placement blocks were found in this file - it needs a header row of metric names next to a brand cell, with month rows underneath",
                });
        }
        else
        {
            doc.Sources.Add(new SourceInfo { File = ctx.FileName, FormatId = best.FormatId, Match = bestMatch.ToString() });
            best.Parse(wb, ctx, doc);
        }
        SnapshotWorkbook(wb, ctx, doc);
    }

    // Sparse cell map + comments per visible sheet; what the AI tools read.
    private static void SnapshotWorkbook(IXLWorkbook wb, ParseContext ctx, ImportDocument doc)
    {
        const int maxRows = 600, maxCols = 40;
        foreach (var ws in wb.Worksheets)
        {
            if (ws.Visibility != XLWorksheetVisibility.Visible) continue;
            int lastRow = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 0, maxRows);
            int lastCol = Math.Min(ws.LastColumnUsed()?.ColumnNumber() ?? 0, maxCols);
            if (lastRow == 0 || lastCol == 0) continue;

            var sheet = new SheetSnapshot { File = ctx.FileName, Sheet = ws.Name.Trim(), Rows = lastRow, Cols = lastCol };
            for (int r = 1; r <= lastRow; r++)
                for (int c = 1; c <= lastCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    var s = Spreadsheet.ReadDisplayString(cell);
                    if (s is not null) sheet.Cells[$"{Spreadsheet.ColLetter(c)}{r}"] = s;
                    if (cell.HasComment)
                    {
                        var text = cell.GetComment().Text?.Trim();
                        if (!string.IsNullOrEmpty(text))
                            sheet.Comments.Add(new CellComment { Cell = $"{Spreadsheet.ColLetter(c)}{r}", Text = text });
                    }
                }
            doc.Snapshot.Add(sheet);
        }
    }
}
