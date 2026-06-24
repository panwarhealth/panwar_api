using ClosedXML.Excel;

namespace Panwar.Api.Services.Import;

// Research Review: the same block layout as the Results Template, but split into
// per-audience sheets ("2026 - GP DATABASE", "2026 - PHARMACIST DATABASE"). The
// audience comes from the sheet name; publisher is always research-review.
public sealed class ResearchReviewAdapter : IWorkbookAdapter
{
    public string FormatId => "research-review";

    public AdapterMatch Detect(IXLWorkbook wb)
    {
        var has = wb.Worksheets.Any(w =>
        {
            var u = w.Name.ToUpperInvariant();
            return u.Contains("DATABASE") && (u.Contains("GP") || u.Contains("PHARMACIST"));
        });
        return has ? AdapterMatch.Strong : AdapterMatch.None;
    }

    public void Parse(IXLWorkbook wb, ParseContext ctx, ImportDocument doc)
    {
        var sheets = wb.Worksheets.Where(w => w.Name.ToUpperInvariant().Contains("DATABASE")).ToList();
        bool anyYear = sheets.Any(w => w.Name.Contains(ctx.Year.ToString()));

        foreach (var ws in sheets)
        {
            if (anyYear && !ws.Name.Contains(ctx.Year.ToString())) continue;
            var u = ws.Name.ToUpperInvariant();
            string? audience = u.Contains("PHARMACIST") ? "pharmacists" : u.Contains("GP") ? "gps" : null;
            PlacementBlocks.Parse(ws, ctx, doc, publisherOverride: "research-review", audienceOverride: audience);
        }
    }
}
