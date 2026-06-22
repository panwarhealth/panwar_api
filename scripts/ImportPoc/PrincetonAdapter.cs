using ClosedXML.Excel;
using static Panwar.Tools.ImportPoc.Spreadsheet;

namespace Panwar.Tools.ImportPoc;

// Princeton "Deskset" engagement: one sheet per month (JAN-DEC), daily rows,
// columns = content pieces (Portal / Pain Leaflet / ...). Rolls daily -> monthly,
// validates against the sheet's own "Total for <month>" row. A very different
// shape to the block-structured templates - proves the multi-model design.
public sealed class PrincetonAdapter : IWorkbookAdapter
{
    public string FormatId => "princeton";

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4, ["MAY"] = 5, ["JUN"] = 6,
        ["JUL"] = 7, ["AUG"] = 8, ["SEP"] = 9, ["OCT"] = 10, ["NOV"] = 11, ["DEC"] = 12,
    };

    public AdapterMatch Detect(IXLWorkbook wb)
    {
        var names = wb.Worksheets.Select(w => w.Name.Trim().ToUpperInvariant()).ToList();
        if (names.Any(n => n.Contains("PLACEMENTS"))) return AdapterMatch.None;
        var monthSheets = names.Count(n => Months.ContainsKey(n));
        if (monthSheets < 6) return AdapterMatch.None;
        // A Princeton month sheet carries the "Engagement Results by Day" marker.
        var marker = wb.Worksheets.Any(w => Months.ContainsKey(w.Name.Trim())
            && (ReadString(w.Cell(3, 3))?.Contains("Engagement", StringComparison.OrdinalIgnoreCase) ?? false));
        return marker ? AdapterMatch.Strong : AdapterMatch.Weak;
    }

    public void Parse(IXLWorkbook wb, ParseContext ctx, ImportDocument doc)
    {
        foreach (var ws in wb.Worksheets)
        {
            if (!Months.TryGetValue(ws.Name.Trim(), out var month)) continue;
            ParseMonth(ws, month, ctx, doc);
        }
    }

    private static void ParseMonth(IXLWorksheet ws, int month, ParseContext ctx, ImportDocument doc)
    {
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0) return;

        // Asset name: the cell beside the "Asset" label in col B.
        string? asset = null;
        int headerRow = 0;
        for (int r = 1; r <= Math.Min(lastRow, 8); r++)
        {
            var b = ReadString(ws.Cell(r, 2));
            if (string.Equals(b, "Asset", StringComparison.OrdinalIgnoreCase)) asset = ReadString(ws.Cell(r, 3));
            if (string.Equals(b, "Day", StringComparison.OrdinalIgnoreCase)
                && string.Equals(ReadString(ws.Cell(r, 3)), "Date", StringComparison.OrdinalIgnoreCase))
            {
                headerRow = r;
                break;
            }
        }
        if (headerRow == 0 || asset is null) return;

        // Content-piece columns: D.. in the header row, until blank.
        var pieces = new List<(int Col, string Name)>();
        for (int c = 4; c <= lastCol; c++)
        {
            var label = ReadString(ws.Cell(headerRow, c));
            if (label is null) break;
            pieces.Add((c, label));
        }
        if (pieces.Count == 0) return;

        var brand = asset.Split(' ', 2)[0];   // "NFC Princeton Deskset" -> "NFC"

        // Sum the daily rows per piece; stop at the "Total"/"YTD" footer or a non-date.
        var sums = pieces.ToDictionary(p => p.Col, _ => 0m);
        var declared = new Dictionary<int, decimal?>();
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var cLabel = ReadString(ws.Cell(r, 3));
            if (cLabel is not null && cLabel.Contains("Total", StringComparison.OrdinalIgnoreCase))
            {
                if (cLabel.StartsWith("Total for", StringComparison.OrdinalIgnoreCase))
                    foreach (var (col, _) in pieces) declared[col] = ReadDecimal(ws.Cell(r, col));
                continue;
            }
            if (ReadDate(ws.Cell(r, 3)) is null) continue; // only true daily rows
            foreach (var (col, _) in pieces)
            {
                var v = ReadDecimal(ws.Cell(r, col));
                if (v is not null) sums[col] += v.Value;
            }
        }

        foreach (var (col, pieceName) in pieces)
        {
            var total = sums[col];
            if (total == 0m) continue;
            var name = $"{asset} - {pieceName}";
            var publisher = "princeton";
            var template = Catalog.TemplateFromName(name, new[] { "sessions" });
            doc.Placements.Add(new ParsedPlacement
            {
                Source = ctx.FileName,
                Brand = brand,
                Audience = Catalog.AudienceFor(publisher),
                Publisher = publisher,
                Template = template,
                Name = name,
                Objective = "Awareness",
                Actuals = { new ParsedActual { Metric = "sessions", Month = month, Value = total } },
            });

            if (declared.TryGetValue(col, out var d) && d is not null && Math.Abs(d.Value - total) > 0.5m)
                doc.Warnings.Add(new Warning { Source = ctx.FileName, Message = $"'{name}' {MonthName(month)}: days sum to {total} but sheet total is {d.Value}" });
            if (!Catalog.IsValidMetric(template, "sessions"))
                doc.Warnings.Add(new Warning { Source = ctx.FileName, Message = $"'{name}': metric 'sessions' has no home in the {template} template - needs a metric mapping decision" });
        }
    }

    private static string MonthName(int m) => new[] { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" }[m];
}
