using System.Text;
using ClosedXML.Excel;
using ExcelDataReader;

namespace Panwar.Api.Services.Import;

internal static class WorkbookLoader
{
    private static bool _codePagesRegistered;

    public static XLWorkbook Load(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0x50 && bytes[1] == 0x4B)
        {
            var ms = new MemoryStream(bytes);
            try { return new XLWorkbook(ms); }
            catch { ms.Dispose(); throw; }
        }

        if (!_codePagesRegistered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _codePagesRegistered = true;
        }

        using var stream = new MemoryStream(bytes);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var wb = new XLWorkbook();
        do
        {
            var name = string.IsNullOrWhiteSpace(reader.Name) ? $"Sheet{wb.Worksheets.Count + 1}" : reader.Name;
            var ws = wb.AddWorksheet(name.Length > 31 ? name[..31] : name);
            int r = 0;
            while (reader.Read())
            {
                r++;
                for (int c = 0; c < reader.FieldCount; c++)
                {
                    var v = reader.GetValue(c);
                    if (v is null) continue;
                    var cell = ws.Cell(r, c + 1);
                    switch (v)
                    {
                        case double d: cell.Value = d; break;
                        case DateTime dt: cell.Value = dt; break;
                        case bool b: cell.Value = b; break;
                        default: cell.Value = v.ToString() ?? ""; break;
                    }
                }
            }
        } while (reader.NextResult());
        return wb;
    }
}
