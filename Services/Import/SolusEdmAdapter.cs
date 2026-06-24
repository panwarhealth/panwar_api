using System.Text.RegularExpressions;
using ClosedXML.Excel;
using static Panwar.Api.Services.Import.Spreadsheet;

namespace Panwar.Api.Services.Import;

// Standalone Solus eDM campaign export (e.g. MedToday "Email Campaign Report",
// a legacy .xls). One send = one eDM placement's actuals for the delivery month:
// a key/value block (Total Recipients, Successful Deliveries, Total Opens,
// Recipients Who Opened, Total Clicks, Recipients Who Clicked) plus a Clicks-by-URL
// table. Publisher is left unresolved for the admin to map in the preview.
public sealed class SolusEdmAdapter : IWorkbookAdapter
{
    public string FormatId => "solus-edm";

    private static readonly string[] MonthNames =
        { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };

    public AdapterMatch Detect(IXLWorkbook wb)
    {
        foreach (var ws in wb.Worksheets)
        {
            var a1 = ReadString(ws.Cell(1, 1));
            if (a1 is not null && a1.Contains("Email Campaign Report", StringComparison.OrdinalIgnoreCase))
                return AdapterMatch.Strong;
        }
        return AdapterMatch.None;
    }

    public void Parse(IXLWorkbook wb, ParseContext ctx, ImportDocument doc)
    {
        var ws = wb.Worksheets.FirstOrDefault(w =>
            ReadString(w.Cell(1, 1))?.Contains("Email Campaign Report", StringComparison.OrdinalIgnoreCase) ?? false);
        if (ws is null) return;

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        string? title = null;
        int? month = null;
        var vals = new Dictionary<string, decimal>();

        for (int r = 1; r <= lastRow; r++)
        {
            var label = ReadString(ws.Cell(r, 1));
            if (label is null) continue;
            var l = label.Trim();

            if (l.Equals("Title", StringComparison.OrdinalIgnoreCase))
                title = ReadString(ws.Cell(r, 2));
            else if (l.StartsWith("Delivery Date", StringComparison.OrdinalIgnoreCase))
                month = MonthFromText(ReadString(ws.Cell(r, 2)));
            else if (l.Equals("Successful Deliveries", StringComparison.OrdinalIgnoreCase))
                Set(vals, "sends", ws.Cell(r, 2));
            else if (l.Equals("Total Opens", StringComparison.OrdinalIgnoreCase))
                Set(vals, "opens", ws.Cell(r, 2));
            else if (l.Equals("Recipients Who Opened", StringComparison.OrdinalIgnoreCase))
                Set(vals, "unique_opens", ws.Cell(r, 2));
            else if (l.Equals("Total Clicks", StringComparison.OrdinalIgnoreCase))
                Set(vals, "clicks", ws.Cell(r, 2));
            else if (l.Equals("Recipients Who Clicked", StringComparison.OrdinalIgnoreCase))
                Set(vals, "unique_clicks", ws.Cell(r, 2));
        }

        if (title is null || month is null || vals.Count == 0)
        {
            doc.Warnings.Add(new Warning { Source = ctx.FileName, Message = "Solus eDM report recognised but key fields (title/date/metrics) could not be read" });
            return;
        }

        var publisher = ResolvePublisher(title) ?? "";
        var placement = new ParsedPlacement
        {
            Source = ctx.FileName,
            Brand = "",
            Audience = Catalog.AudienceFor(publisher),
            Publisher = publisher,
            Template = "Edm",
            Name = title,
            Objective = "Awareness",
        };
        foreach (var (key, value) in vals)
            placement.Actuals.Add(new ParsedActual { Metric = key, Month = month.Value, Value = value });

        doc.Placements.Add(placement);
        doc.Warnings.Add(new Warning
        {
            Source = ctx.FileName,
            Message = $"Solus eDM '{title}' read for {MonthNames[month.Value - 1]} - map it to the matching eDM placement in the preview",
        });
    }

    private static void Set(Dictionary<string, decimal> vals, string key, IXLCell cell)
    {
        var n = ParseLeadingNumber(ReadString(cell)) ?? ReadDecimal(cell);
        if (n is not null) vals[key] = n.Value;
    }

    // "7,690 (48.8%)" -> 7690
    private static decimal? ParseLeadingNumber(string? s)
    {
        if (s is null) return null;
        var m = Regex.Match(s.Trim(), @"^[\d,]+");
        if (!m.Success) return null;
        return decimal.TryParse(m.Value.Replace(",", ""), out var d) ? d : null;
    }

    private static int? MonthFromText(string? s)
    {
        if (s is null) return null;
        var l = s.ToLowerInvariant();
        for (int i = 0; i < 12; i++)
            if (l.Contains(MonthNames[i])) return i + 1;
        return null;
    }
}
