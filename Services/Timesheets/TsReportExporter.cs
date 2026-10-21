using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Panwar.Api.Services.Timesheets;

public interface ITsReportExporter
{
    byte[] ToCsv(IReadOnlyList<TsDetailedRow> rows);
    byte[] ToXlsx(IReadOnlyList<TsDetailedRow> rows);
}

public class TsReportExporter : ITsReportExporter
{
    private const string SheetName = "Harvest";
    private const int ShortDateFormatId = 14;

    public byte[] ToCsv(IReadOnlyList<TsDetailedRow> rows)
    {
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', TsDetailedReport.Columns.Select(c => Quote(c.Header))));
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', TsDetailedReport.Columns.Select(c => Quote(CsvValue(c.Value(row))))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    public byte[] ToXlsx(IReadOnlyList<TsDetailedRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);
        for (var c = 0; c < TsDetailedReport.Columns.Count; c++)
            sheet.Cell(1, c + 1).Value = TsDetailedReport.Columns[c].Header;

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < TsDetailedReport.Columns.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                switch (TsDetailedReport.Columns[c].Value(rows[r]))
                {
                    case DateOnly d:
                        cell.Value = d.ToDateTime(TimeOnly.MinValue);
                        cell.Style.NumberFormat.NumberFormatId = ShortDateFormatId;
                        break;
                    case decimal n:
                        cell.Value = n;
                        break;
                    case string s:
                        cell.Value = s;
                        break;
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string CsvValue(object? value) => value switch
    {
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal n => n.ToString("0.##", CultureInfo.InvariantCulture),
        _ => TsDetailedReport.Display(value)
    };

    private static string Quote(string value)
        => value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
