using ClosedXML.Excel;
using static Panwar.Api.Services.Import.Spreadsheet;

namespace Panwar.Api.Services.Import;

// Reckitt-Report: the GP Therapy-Update / Arterial Education portal report. One
// bespoke sheet per item: TU articles (page views), an education course
// (enrolments/completions), an Advertising sheet (impressions/clicks per brand)
// and an eDM sheet (per-send sends/opens/clicks). Figures are cumulative period
// totals (e.g. "January 1st - May 15th"), so non-eDM items are assigned to the
// period-end month and flagged for review; eDM sends carry their own date.
public sealed class ReckittReportAdapter : IWorkbookAdapter
{
    public string FormatId => "reckitt-report";

    private const string Publisher = "arterial";
    private const string Audience = "gps";

    private static readonly string[] MonthNames =
        { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };

    public AdapterMatch Detect(IXLWorkbook wb)
    {
        var names = wb.Worksheets.Select(w => w.Name.Trim().ToUpperInvariant()).ToHashSet();
        bool tu = wb.Worksheets.Any(w => HasArticleMarker(w));
        if (tu || (names.Contains("ADVERTISING") && names.Contains("EDM"))) return AdapterMatch.Strong;
        return AdapterMatch.None;
    }

    public void Parse(IXLWorkbook wb, ParseContext ctx, ImportDocument doc)
    {
        foreach (var ws in wb.Worksheets)
        {
            var name = ws.Name.Trim();
            if (HasArticleMarker(ws)) ParseArticle(ws, ctx, doc);
            else if (FindCellContaining(ws, "Education course", 4) is not null) ParseCourse(ws, ctx, doc);
            else if (name.Equals("Advertising", StringComparison.OrdinalIgnoreCase)) ParseAdvertising(ws, ctx, doc);
            else if (name.Equals("eDM", StringComparison.OrdinalIgnoreCase)) ParseEdm(ws, ctx, doc);
        }

        doc.Warnings.Add(new Warning
        {
            Source = ctx.FileName,
            Message = "Reckitt-Report figures are cumulative period totals from the GP Therapy Update portal - non-eDM items are placed on the period-end month and may overlap the Arterial file; review before committing",
        });
    }

    // ── TU article: page views + unique page views ───────────────────────────
    private static void ParseArticle(IXLWorksheet ws, ParseContext ctx, ImportDocument doc)
    {
        var title = StripPrefix(FindCellContaining(ws, "TU article", 4), "TU article");
        if (title is null) return;
        int month = MaxMonth(ws) ?? 12;

        var placement = new ParsedPlacement
        {
            Source = ctx.FileName,
            Brand = InferBrand(title),
            Audience = Audience,
            Publisher = Publisher,
            Template = "Education",
            Name = title,
            Objective = "Engagement",
        };
        AddIf(placement, "page_views", month, ValueBelow(ws, "Page Views"));
        AddIf(placement, "unique_page_views", month, ValueBelow(ws, "Unique Page Views"));
        if (placement.Actuals.Count > 0) doc.Placements.Add(placement);
    }

    // ── Education course: 2026 enrolments + completions ──────────────────────
    private static void ParseCourse(IXLWorksheet ws, ParseContext ctx, ImportDocument doc)
    {
        var title = StripPrefix(FindCellContaining(ws, "Education course", 4), "Education course");
        if (title is null) return;
        int month = MaxMonth(ws) ?? 12;

        int yearRow = FindLabelRow(ws, ctx.Year.ToString(), 30);
        if (yearRow == 0) return;
        decimal? enrolled = null, completed = null;
        for (int r = yearRow + 1; r <= yearRow + 6; r++)
        {
            var a = ReadString(ws.Cell(r, 1));
            if (a is null) continue;
            if (a.StartsWith("Total", StringComparison.OrdinalIgnoreCase)) break;
            if (a.StartsWith("Enrol", StringComparison.OrdinalIgnoreCase)) enrolled = ReadDecimal(ws.Cell(r, 2));
            else if (a.StartsWith("Complet", StringComparison.OrdinalIgnoreCase)) completed = ReadDecimal(ws.Cell(r, 2));
        }

        var asset = new ParsedEducationAsset { Source = ctx.FileName, Brand = InferBrand(title), Title = title };
        if (completed is not null) asset.Values.Add(new ParsedEducationValue { Status = "Completed", Year = ctx.Year, Month = month, Value = completed.Value });
        if (enrolled is not null) asset.Values.Add(new ParsedEducationValue { Status = "Enrolled", Year = ctx.Year, Month = month, Value = enrolled.Value });
        if (asset.Values.Count > 0) doc.Education.Add(asset);
    }

    // ── Advertising: impressions + clicks per brand block ────────────────────
    private static void ParseAdvertising(IXLWorksheet ws, ParseContext ctx, ImportDocument doc)
    {
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        int month = MaxMonth(ws) ?? 12;
        ParsedPlacement? current = null;

        for (int r = 1; r <= lastRow; r++)
        {
            var a = ReadString(ws.Cell(r, 1));
            if (a is null) continue;
            if (a.Contains(" - ") && MaxMonthInText(a) is not null) continue; // the date-range header

            if (a.StartsWith("Impression", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) AddIf(current, "impressions", month, ReadDecimal(ws.Cell(r, 2)));
            }
            else if (a.Equals("CLICKS", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Click", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) AddIf(current, "clicks", month, ReadDecimal(ws.Cell(r, 2)));
            }
            else
            {
                // A brand/section header starts a new digital-display placement.
                if (current is not null && current.Actuals.Count > 0) doc.Placements.Add(current);
                current = new ParsedPlacement
                {
                    Source = ctx.FileName,
                    Brand = InferBrand(a),
                    Audience = Audience,
                    Publisher = Publisher,
                    Template = "DigitalDisplay",
                    Name = $"GP Portal Advertising - {a}",
                    Objective = "Awareness",
                };
            }
        }
        if (current is not null && current.Actuals.Count > 0) doc.Placements.Add(current);
    }

    // ── eDM: per-send sends/opens + summed ad clicks ─────────────────────────
    private static void ParseEdm(IXLWorksheet ws, ParseContext ctx, ImportDocument doc)
    {
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = 1; r <= lastRow; r++)
        {
            var a = ReadString(ws.Cell(r, 1));
            if (a is null || !a.Contains("EDM Report", StringComparison.OrdinalIgnoreCase)) continue;

            // Next row: "<date> | Sends | Opens"; the row after holds the values.
            int headerRow = r + 1;
            var dateCell = ReadString(ws.Cell(headerRow, 1));
            int month = MonthFromText(dateCell) ?? MaxMonth(ws) ?? 12;
            var sends = ReadDecimal(ws.Cell(headerRow + 1, 2));
            var opens = ReadDecimal(ws.Cell(headerRow + 1, 3));

            // Ad rows after the "ADS" header: sum the click column.
            decimal clicks = 0; bool anyClicks = false;
            int adsRow = headerRow + 2;
            if (string.Equals(ReadString(ws.Cell(adsRow, 1)), "ADS", StringComparison.OrdinalIgnoreCase))
            {
                for (int ar = adsRow + 1; ar <= lastRow; ar++)
                {
                    if (ReadString(ws.Cell(ar, 1)) is null) break;
                    var c = ReadDecimal(ws.Cell(ar, 2));
                    if (c is not null) { clicks += c.Value; anyClicks = true; }
                }
            }

            var name = $"{a} - {(dateCell ?? MonthNames[month - 1])}";
            var placement = new ParsedPlacement
            {
                Source = ctx.FileName,
                Brand = "Nurofen",
                Audience = Audience,
                Publisher = Publisher,
                Template = "Edm",
                Name = name,
                Objective = "Awareness",
            };
            AddIf(placement, "sends", month, sends);
            AddIf(placement, "opens", month, opens);
            if (anyClicks) AddIf(placement, "clicks", month, clicks);
            if (placement.Actuals.Count > 0) doc.Placements.Add(placement);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static bool HasArticleMarker(IXLWorksheet ws) => FindCellContaining(ws, "TU article", 4) is not null;

    private static void AddIf(ParsedPlacement p, string metric, int month, decimal? value)
    {
        if (value is null || value == 0m) return;
        p.Actuals.Add(new ParsedActual { Metric = metric, Month = month, Value = value.Value });
    }

    private static decimal? ValueBelow(IXLWorksheet ws, string label)
    {
        int row = FindLabelRow(ws, label, 30);
        return row == 0 ? null : ReadDecimal(ws.Cell(row + 1, 1));
    }

    private static int FindLabelRow(IXLWorksheet ws, string label, int maxRow)
    {
        for (int r = 1; r <= maxRow; r++)
        {
            var a = ReadString(ws.Cell(r, 1));
            if (a is not null && a.Trim().Equals(label, StringComparison.OrdinalIgnoreCase)) return r;
        }
        return 0;
    }

    private static string? FindCellContaining(IXLWorksheet ws, string needle, int maxRow)
    {
        for (int r = 1; r <= maxRow; r++)
        {
            var a = ReadString(ws.Cell(r, 1));
            if (a is not null && a.Contains(needle, StringComparison.OrdinalIgnoreCase)) return a;
        }
        return null;
    }

    private static string? StripPrefix(string? s, string prefix)
    {
        if (s is null) return null;
        var i = s.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return s.Trim();
        var rest = s[(i + prefix.Length)..].TrimStart(' ', '-', ':').Trim();
        return rest.Length == 0 ? null : rest;
    }

    private static int? MaxMonth(IXLWorksheet ws)
    {
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        int? max = null;
        for (int r = 1; r <= Math.Min(lastRow, 6); r++)
        {
            var m = MaxMonthInText(ReadString(ws.Cell(r, 1)));
            if (m is not null && (max is null || m > max)) max = m;
        }
        return max;
    }

    private static int? MaxMonthInText(string? s)
    {
        if (s is null) return null;
        var l = s.ToLowerInvariant();
        int? max = null;
        for (int i = 0; i < 12; i++)
            if (l.Contains(MonthNames[i]) || l.Contains(MonthNames[i].Substring(0, 3)))
                if (max is null || i + 1 > max) max = i + 1;
        return max;
    }

    private static int? MonthFromText(string? s)
    {
        if (s is null) return null;
        var l = s.ToLowerInvariant();
        for (int i = 0; i < 12; i++)
            if (l.Contains(MonthNames[i]) || l.Contains(MonthNames[i].Substring(0, 3))) return i + 1;
        return null;
    }

    private static string InferBrand(string s)
    {
        var l = s.ToLowerInvariant();
        if (l.Contains("ppi") || l.Contains("reflux") || l.Contains("gord") || l.Contains("gut") || l.Contains("gastro") || l.Contains("laxative") || l.Contains("gaviscon"))
            return "Gaviscon";
        if (l.Contains("child") || l.Contains("paediatric") || l.Contains("pediatric") || l.Contains("infant") || l.Contains("vaccinat") || l.Contains("fever") || l.Contains("nfc"))
            return "NFC";
        if (l.Contains("cold") || l.Contains("flu")) return "C&F";
        return "Nurofen";
    }
}
