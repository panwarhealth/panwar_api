using ClosedXML.Excel;
using static Panwar.Api.Services.Import.Spreadsheet;

namespace Panwar.Api.Services.Import;

// Shared parser for the "Results Template" block layout used by AJP, AP, Arterial
// and Research Review. The sheet is a column of placements: a brand/metric HEADER
// row sets the active metric columns (and is reused by several placements until
// the next header), each placement is a SUMMARY row (name + year totals) followed
// by its month rows (a month number or month name in the brand column). The brand
// column is auto-detected per sheet, so any column offset works.
internal static class PlacementBlocks
{
    private static readonly string[] MonthNames =
        { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };

    public static void Parse(
        IXLWorksheet ws,
        ParseContext ctx,
        ImportDocument doc,
        string? publisherOverride = null,
        string? audienceOverride = null)
    {
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        int brandCol = FindBrandCol(ws, lastRow, lastCol);
        if (brandCol == 0) return;

        string? currentBrand = null;
        var metrics = new List<(int Col, string Key, string Label)>();
        int row = 1;

        while (row <= lastRow)
        {
            var cell = ReadString(ws.Cell(row, brandCol));
            if (cell is null) { row++; continue; }

            // HEADER: metric labels sit immediately right of the brand (within a
            // small window, so far-right Note prose like "confirm send dates"
            // can't masquerade as a metric). A header carries >= 2 such labels.
            int metricStart = FirstMetricCol(ws, row, brandCol + 1, Math.Min(lastCol, brandCol + 3));
            if (metricStart != 0)
            {
                currentBrand = cell;
                metrics = ReadMetrics(ws, row, metricStart, lastCol);
                row++;
                continue;
            }

            // MONTH row with no active placement, or a row we can't use yet.
            if (MonthOf(ws.Cell(row, brandCol)) is not null || metrics.Count == 0)
            {
                row++;
                continue;
            }

            // SUMMARY row: parse this placement and its month rows.
            row = ParsePlacement(ws, ctx, doc, row, brandCol, currentBrand ?? "", metrics, lastRow, lastCol, publisherOverride, audienceOverride);
        }
    }

    private static int ParsePlacement(
        IXLWorksheet ws, ParseContext ctx, ImportDocument doc, int summaryRow, int brandCol,
        string brand, List<(int Col, string Key, string Label)> metrics, int lastRow, int lastCol,
        string? publisherOverride, string? audienceOverride)
    {
        var name = ReadString(ws.Cell(summaryRow, brandCol))!;
        var keys = metrics.Select(m => m.Key).ToList();
        var publisher = publisherOverride ?? ResolvePublisher(name) ?? "";
        var audience = audienceOverride ?? Catalog.AudienceFor(publisher);
        var template = Catalog.TemplateFromName(name, keys);

        var placement = new ParsedPlacement
        {
            Source = ctx.FileName,
            Brand = brand,
            Audience = audience,
            Publisher = publisher,
            Template = template,
            Name = name,
            Objective = InferObjective(name),
        };
        if (publisher == "")
            doc.Warnings.Add(new Warning { Source = ctx.FileName, Message = $"Could not resolve publisher for placement '{name}'" });
        else if (placement.Audience is null)
            doc.Warnings.Add(new Warning { Source = ctx.FileName, Message = $"Could not derive audience for publisher '{publisher}' ('{name}') - pick in preview" });
        foreach (var key in keys.Where(k => !Catalog.IsValidMetric(template, k)).Distinct())
            doc.Warnings.Add(new Warning { Source = ctx.FileName, Message = $"'{name}': metric '{key}' is not in the {template} template - confirm template/metric mapping" });

        var notes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in RowNotes(ws, summaryRow, brandCol, lastCol)) AddNote(notes, n);

        var monthlySum = new Dictionary<string, decimal>();
        int r = summaryRow + 1;
        while (r <= lastRow)
        {
            var month = MonthOf(ws.Cell(r, brandCol));
            if (month is null) break;
            var rowNotes = RowNotes(ws, r, brandCol, lastCol);
            foreach (var n in rowNotes) AddNote(notes, n);
            // Per-month note names that send's topic / real date - the key the AI
            // uses to split this block into per-send placements.
            if (rowNotes.Count > 0) placement.MonthNotes[month.Value] = string.Join(" | ", rowNotes);
            var note = rowNotes.Count > 0 ? rowNotes[0] : null;
            foreach (var (col, key, _) in metrics)
            {
                var v = ReadDecimal(ws.Cell(r, col));
                if (v is null || v == 0m) continue;
                placement.Actuals.Add(new ParsedActual { Metric = key, Month = month.Value, Value = v.Value, Note = note });
                monthlySum[key] = monthlySum.GetValueOrDefault(key) + v.Value;
            }
            r++;
        }
        placement.Notes.AddRange(notes);

        foreach (var (col, key, label) in metrics)
        {
            var declared = ReadDecimal(ws.Cell(summaryRow, col));
            if (declared is null) continue;
            var got = monthlySum.GetValueOrDefault(key);
            if (Math.Abs(declared.Value - got) > 0.5m)
                doc.Warnings.Add(new Warning
                {
                    Source = ctx.FileName,
                    Message = $"'{name}' {label}: months sum to {got} but file total is {declared.Value}",
                });
        }

        if (placement.Actuals.Count > 0) doc.Placements.Add(placement);
        return Math.Max(r, summaryRow + 1);
    }

    // The brand column = the column immediately left of the metric labels on the
    // first real header row (a row carrying >= 2 metric-like labels, so a stray
    // note word doesn't pick the wrong column).
    private static int FindBrandCol(IXLWorksheet ws, int lastRow, int lastCol)
    {
        for (int r = 1; r <= Math.Min(lastRow, 60); r++)
        {
            int mc = FirstMetricCol(ws, r, 2, lastCol);
            if (mc == 0) continue;
            int labels = 0;
            for (int c = mc; c <= lastCol; c++)
                if (LooksLikeMetricLabel(ReadString(ws.Cell(r, c)))) labels++;
            if (labels < 2) continue;
            for (int c = mc - 1; c >= 1; c--)
                if (ReadString(ws.Cell(r, c)) is not null) return c;
        }
        return 0;
    }

    private static int FirstMetricCol(IXLWorksheet ws, int row, int startCol, int lastCol)
    {
        for (int c = startCol; c <= lastCol; c++)
            if (LooksLikeMetricLabel(ReadString(ws.Cell(row, c)))) return c;
        return 0;
    }

    private static List<(int Col, string Key, string Label)> ReadMetrics(IXLWorksheet ws, int row, int metricStart, int lastCol)
    {
        var metrics = new List<(int, string, string)>();
        for (int c = metricStart; c <= lastCol; c++)
        {
            var label = ReadString(ws.Cell(row, c));
            if (label is null) break;
            if (label.Equals("Note", StringComparison.OrdinalIgnoreCase)) break;
            var key = Catalog.NormalizeMetric(label);
            if (key.Length == 0) continue; // placeholder header like "-" - not a metric
            metrics.Add((c, key, label));
        }
        return metrics;
    }

    private static int? MonthOf(IXLCell cell)
    {
        var d = ReadDecimal(cell);
        if (d is not null)
        {
            var n = (int)d.Value;
            if (n is >= 1 and <= 12 && n == d.Value) return n;
            return null;
        }
        var s = ReadString(cell);
        if (s is null) return null;
        s = s.Trim().ToLowerInvariant();
        for (int i = 0; i < 12; i++)
            if (s == MonthNames[i] || s == MonthNames[i].Substring(0, 3)) return i + 1;
        return null;
    }

    private static void AddNote(HashSet<string> notes, string? text)
    {
        if (text is null) return;
        var t = text.Trim();
        if (t.Length > 3 && t.Any(char.IsLetter)) notes.Add(t);
    }

    // All human guidance on a row: text typed into a cell that isn't a number,
    // plus any Excel cell-comment. These carry the send's topic/date.
    private static List<string> RowNotes(IXLWorksheet ws, int row, int brandCol, int lastCol)
    {
        var result = new List<string>();
        for (int c = brandCol + 1; c <= lastCol; c++)
        {
            var cell = ws.Cell(row, c);
            var s = ReadDisplayString(cell);
            if (s is not null && ReadDecimal(cell) is null && s.Trim().Length > 3 && s.Any(char.IsLetter))
                result.Add(s.Trim());
            if (cell.HasComment)
            {
                var ct = cell.GetComment().Text?.Trim();
                if (!string.IsNullOrEmpty(ct)) result.Add(ct);
            }
        }
        return result;
    }

    private static string? NoteForRow(IXLWorksheet ws, int row, List<(int Col, string Key, string Label)> metrics, int lastCol)
    {
        int after = (metrics.Count > 0 ? metrics[^1].Col : 4) + 1;
        for (int c = after; c <= lastCol; c++)
        {
            var s = ReadString(ws.Cell(row, c));
            if (s is not null && s != "0") return s;
        }
        return null;
    }
}
