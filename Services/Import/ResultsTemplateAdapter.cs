using ClosedXML.Excel;
using static Panwar.Api.Services.Import.Spreadsheet;

namespace Panwar.Api.Services.Import;

// Panwar's own "Results Template" (AJP, AP, Arterial): block-structured PLACEMENTS
// + an EDUCATION grid. The flagship adapter.
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
        // Some workbooks (Arterial) carry multiple year sheets ("2025 PLACEMENTS",
        // "2026 PLACEMENTS"); parse the one matching the target year.
        var placementsSheets = wb.Worksheets
            .Where(w => w.Name.ToUpperInvariant().Contains("PLACEMENTS"))
            .ToList();
        var placementsSheet = placementsSheets.FirstOrDefault(w => w.Name.Contains(ctx.Year.ToString()))
            ?? placementsSheets.FirstOrDefault();
        if (placementsSheet is not null) PlacementBlocks.Parse(placementsSheet, ctx, doc);

        var educationSheet = wb.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("EDUCATION", StringComparison.OrdinalIgnoreCase));
        if (educationSheet is not null) ParseEducation(educationSheet, ctx, doc);
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
            var status = ReadString(ws.Cell(r, 7));
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
