using ClosedXML.Excel;
using static Panwar.Tools.ImportPoc.Spreadsheet;

namespace Panwar.Tools.ImportPoc;

// Panwar's own "Results Template" (AJP, AP): block-structured PLACEMENTS + an
// EDUCATION grid + daily working sheets. The flagship adapter.
public sealed class ResultsTemplateAdapter : IWorkbookAdapter
{
    public string FormatId => "results-template";

    public AdapterMatch Detect(IXLWorkbook wb)
    {
        var names = wb.Worksheets.Select(w => w.Name.Trim().ToUpperInvariant()).ToList();
        var hasPlacements = names.Any(n => n.Contains("PLACEMENTS"));
        var hasMarker = names.Any(n => n is "ASSET TYPE" or "EDUCATION");
        if (hasPlacements && hasMarker) return AdapterMatch.Strong;
        if (hasPlacements) return AdapterMatch.Weak;
        return AdapterMatch.None;
    }

    public void Parse(IXLWorkbook wb, ParseContext ctx, ImportDocument doc)
    {
        var placementsSheet = wb.Worksheets.FirstOrDefault(w => w.Name.ToUpperInvariant().Contains("PLACEMENTS"));
        if (placementsSheet is not null) ParsePlacements(placementsSheet, ctx, doc);

        var educationSheet = wb.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("EDUCATION", StringComparison.OrdinalIgnoreCase));
        if (educationSheet is not null) ParseEducation(educationSheet, ctx, doc);
    }

    // ── PLACEMENTS: per-asset blocks (header → summary → 12 month rows) ───────
    private static void ParsePlacements(IXLWorksheet ws, ParseContext ctx, ImportDocument doc)
    {
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        int row = 1;

        while (row <= lastRow)
        {
            var brand = ReadString(ws.Cell(row, 3));        // col C
            var firstMetric = ReadString(ws.Cell(row, 4));  // col D
            if (brand is null || !LooksLikeMetricLabel(firstMetric))
            {
                row++;
                continue;
            }

            // Metric columns: D.. until the "Note" label (or a blank).
            var metrics = new List<(int Col, string Key, string Label)>();
            for (int c = 4; c <= lastCol; c++)
            {
                var label = ReadString(ws.Cell(row, c));
                if (label is null) break;
                if (label.Equals("Note", StringComparison.OrdinalIgnoreCase)) break;
                metrics.Add((c, Catalog.NormalizeMetric(label), label));
            }

            int summaryRow = row + 1;
            var name = ReadString(ws.Cell(summaryRow, 3));
            if (name is null) { row++; continue; }

            var keys = metrics.Select(m => m.Key).ToList();
            var publisher = ResolvePublisher(name) ?? "";
            var template = Catalog.TemplateFromName(name, keys);
            var placement = new ParsedPlacement
            {
                Source = ctx.FileName,
                Brand = brand,
                Audience = Catalog.AudienceFor(publisher),
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

            // 12 month rows: month m at summaryRow + m (C col carries the month number).
            var monthlySum = new Dictionary<string, decimal>();
            for (int m = 1; m <= 12; m++)
            {
                int mrow = summaryRow + m;
                if (mrow > lastRow) break;
                var note = NoteForRow(ws, mrow, metrics, lastCol);
                foreach (var (col, key, _) in metrics)
                {
                    var v = ReadDecimal(ws.Cell(mrow, col));
                    if (v is null || v == 0m) continue;
                    placement.Actuals.Add(new ParsedActual { Metric = key, Month = m, Value = v.Value, Note = note });
                    monthlySum[key] = monthlySum.GetValueOrDefault(key) + v.Value;
                }
            }

            // Trust signal: parsed monthly sum should equal the summary (year-total) row.
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
            row = summaryRow + 13; // header(1) + summary(1) + 12 months → next block
        }
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

    // ── EDUCATION: brand/type/title/KOL/expiry + Completed & Enrolled month pairs ─
    private static void ParseEducation(IXLWorksheet ws, ParseContext ctx, ImportDocument doc)
    {
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        int headerRow = 0;
        for (int r = 1; r <= Math.Min(lastRow, 6); r++)
        {
            if (string.Equals(ReadString(ws.Cell(r, 2)), "Brand", StringComparison.OrdinalIgnoreCase)
                && (ReadString(ws.Cell(r, 7))?.Contains("Status", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                headerRow = r;
                break;
            }
        }
        if (headerRow == 0) return;

        // Month columns: from col H(8) onward, header cell holds a date → (year, month).
        var monthCols = new List<(int Col, int Year, int Month)>();
        for (int c = 8; c <= lastCol; c++)
        {
            var d = ReadDate(ws.Cell(headerRow, c));
            if (d is null) continue;
            monthCols.Add((c, d.Value.Year, d.Value.Month));
        }

        ParsedEducationAsset? current = null;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var status = ReadString(ws.Cell(r, 7)); // col G
            var brand = ReadString(ws.Cell(r, 2));
            var title = ReadString(ws.Cell(r, 4));

            if (brand is not null && title is not null)
            {
                current = new ParsedEducationAsset
                {
                    Source = ctx.FileName,
                    Brand = brand,
                    Type = ReadString(ws.Cell(r, 3)),
                    Title = title,
                    Author = ReadString(ws.Cell(r, 5)),
                    Expiry = ReadString(ws.Cell(r, 6)),
                };
                doc.Education.Add(current);
            }

            if (current is null || status is null) continue;
            foreach (var (col, year, month) in monthCols)
            {
                var v = ReadDecimal(ws.Cell(r, col));
                if (v is null || v == 0m) continue;
                current.Values.Add(new ParsedEducationValue { Status = status, Year = year, Month = month, Value = v.Value });
            }
        }
    }
}
