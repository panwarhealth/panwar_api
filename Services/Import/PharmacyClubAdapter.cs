using ClosedXML.Excel;
using static Panwar.Api.Services.Import.Spreadsheet;

namespace Panwar.Api.Services.Import;

// Pharmacy Club "EDUCATION RESULTS": an education-only workbook. One sheet, a
// header row (Brand | <title col> | Expiry | Status/Month | month-date columns…),
// then one row per module carrying monthly completions. No placements.
public sealed class PharmacyClubAdapter : IWorkbookAdapter
{
    public string FormatId => "pharmacy-club";

    public AdapterMatch Detect(IXLWorkbook wb)
    {
        var hasSheet = wb.Worksheets.Any(w => w.Name.Trim().Equals("EDUCATION RESULTS", StringComparison.OrdinalIgnoreCase));
        return hasSheet ? AdapterMatch.Strong : AdapterMatch.None;
    }

    public void Parse(IXLWorkbook wb, ParseContext ctx, ImportDocument doc)
    {
        var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("EDUCATION RESULTS", StringComparison.OrdinalIgnoreCase));
        if (ws is null) return;

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        int headerRow = 0;
        for (int r = 1; r <= Math.Min(lastRow, 6); r++)
        {
            if (string.Equals(ReadString(ws.Cell(r, 2)), "Brand", StringComparison.OrdinalIgnoreCase)
                && (ReadString(ws.Cell(r, 5))?.Contains("Status", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                headerRow = r;
                break;
            }
        }
        if (headerRow == 0) return;

        // Month columns: from col F(6) onward, header holds a date → (year, month).
        var monthCols = new List<(int Col, int Year, int Month)>();
        for (int c = 6; c <= lastCol; c++)
        {
            var d = ReadDate(ws.Cell(headerRow, c));
            if (d is null) continue;
            monthCols.Add((c, d.Value.Year, d.Value.Month));
        }

        bool sawExtras = false;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var brand = ReadString(ws.Cell(r, 2));
            var title = ReadString(ws.Cell(r, 3));
            var status = ReadString(ws.Cell(r, 5));

            // Footnote / "Additional Completions" prose below the grid - not a module row.
            if (brand is not null && title is null)
            {
                if (brand.Contains("Additional Completions", StringComparison.OrdinalIgnoreCase)) sawExtras = true;
                continue;
            }
            if (brand is null || title is null) continue;

            var asset = new ParsedEducationAsset
            {
                Source = ctx.FileName,
                Brand = brand,
                Type = null,
                Title = title,
                Author = null,
                Expiry = ReadString(ws.Cell(r, 4)),
            };

            var st = status ?? "Completions";
            foreach (var (col, year, month) in monthCols)
            {
                var v = ReadDecimal(ws.Cell(r, col));
                if (v is null || v == 0m) continue;
                asset.Values.Add(new ParsedEducationValue { Status = st, Year = year, Month = month, Value = v.Value });
            }

            if (asset.Values.Count > 0) doc.Education.Add(asset);
        }

        if (sawExtras)
            doc.Warnings.Add(new Warning
            {
                Source = ctx.FileName,
                Message = "An 'Additional Completions' note was found below the grid - those one-off figures are not imported automatically, enter them manually if needed",
            });
    }
}
