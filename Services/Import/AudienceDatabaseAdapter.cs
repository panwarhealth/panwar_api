using ClosedXML.Excel;

namespace Panwar.Api.Services.Import;

// Per-audience "DATABASE" sheets ("GP DATABASE 2026") in the placement-block layout.
public sealed class AudienceDatabaseAdapter : IWorkbookAdapter
{
    public string FormatId => "audience-database";

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
            PlacementBlocks.Parse(ws, ctx, doc, audienceHint: ws.Name.Trim());
        }
    }
}
