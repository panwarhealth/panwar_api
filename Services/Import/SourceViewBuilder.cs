using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Import;

// Builds the Excel-style excerpts each preview card shows so the admin can eyeball
// the real cells: every block gets its own patch of the sheet it was parsed from,
// and AI cards additionally get the referenced tab with the cited cells highlighted.
internal static class SourceViewBuilder
{
    // Joins file name + block name into one lookup key; U+0001 can't appear in either.
    private const char KeySep = '\u0001';

    // Index every non-empty cell's value -> where it sits, so we can find where each
    // parsed block came from in the file (first occurrence wins).
    public static Dictionary<string, (SheetSnapshot Sheet, int Row, int Col)> BuildBlockLocator(ImportDocument doc)
    {
        var map = new Dictionary<string, (SheetSnapshot, int, int)>(StringComparer.Ordinal);
        foreach (var sn in doc.Snapshot)
            foreach (var kv in sn.Cells)
            {
                var loc = Spreadsheet.ParseA1(kv.Key);
                if (loc is null) continue;
                var norm = Spreadsheet.NormalizeName(kv.Value);
                if (norm.Length == 0) continue;
                var mapKey = sn.File + KeySep + norm;
                if (!map.ContainsKey(mapKey)) map[mapKey] = (sn, loc.Value.Row, loc.Value.Col);
            }
        return map;
    }

    // The block's own patch of the spreadsheet: its name row plus the rows beneath it,
    // so every card can be cross-checked against the file. No highlight (nothing AI-specific).
    public static IReadOnlyList<SourceViewDto> BuildBlockSourceView(
        IReadOnlyDictionary<string, (SheetSnapshot Sheet, int Row, int Col)> locator, ImportDocument doc, string source, string name)
    {
        if (!locator.TryGetValue(source + KeySep + Spreadsheet.NormalizeName(name), out var loc)) return Array.Empty<SourceViewDto>();
        var sheet = loc.Sheet;
        int startRow = loc.Row, labelCol = loc.Col;

        // Walk down while the label column keeps having values (the block's month rows).
        int end = startRow;
        for (int r = startRow + 1; r <= Math.Min(sheet.Rows, startRow + 13); r++)
        {
            if (sheet.Cells.ContainsKey($"{Spreadsheet.ColLetter(labelCol)}{r}")) end = r;
            else break;
        }

        int fromCol = 1, toCol = Math.Min(sheet.Cols, 16);
        var rows = new List<SourceGridRowDto>();
        for (int r = Math.Max(1, startRow - 1); r <= end; r++)
            rows.Add(BuildGridRow(sheet, r, fromCol, toCol, highlight: null));
        return new[] { new SourceViewDto(sheet.Sheet, TabsFor(doc, source), rows) };
    }

    // When a note names another tab ("refer to the AP Solus eDM data tab"), show that
    // tab's patch on the card too, so the admin can cross-check the reference without
    // opening Excel. Deterministic - works even when the AI is off or cited nothing there.
    public static IReadOnlyList<SourceViewDto> BuildReferencedTabViews(
        ImportDocument doc, string source, IReadOnlyList<string> notes, string? ownSheet)
    {
        if (notes.Count == 0) return Array.Empty<SourceViewDto>();
        var normNotes = notes.Select(Spreadsheet.NormalizeName).ToList();
        var views = new List<SourceViewDto>();
        foreach (var sheet in doc.Snapshot.Where(s => string.Equals(s.File, source, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.Equals(sheet.Sheet, ownSheet, StringComparison.OrdinalIgnoreCase)) continue;
            var name = Spreadsheet.NormalizeName(sheet.Sheet);
            // Short tab names ("data") would match half the notes in the file by accident.
            if (name.Length < 5 || !normNotes.Any(n => n.Contains(name, StringComparison.Ordinal))) continue;

            int toRow = Math.Min(sheet.Rows, 30), toCol = Math.Min(sheet.Cols, 14);
            var rows = new List<SourceGridRowDto>();
            for (int r = 1; r <= toRow; r++) rows.Add(BuildGridRow(sheet, r, 1, toCol, highlight: null));
            views.Add(new SourceViewDto(sheet.Sheet, TabsFor(doc, source), rows));
        }
        return views;
    }

    // Excerpts of the cells the AI cited, straight from the captured workbook snapshot,
    // so the user can eyeball the AI's source next to the card.
    public static IReadOnlyList<SourceViewDto> BuildCitedSourceViews(ImportDocument doc, string source, IReadOnlyList<PlacementSuggestionDto> sends)
    {
        var cited = sends
            .SelectMany(s => s.Values.Select(v => (Sheet: v.SourceSheet ?? "", Cell: (v.SourceCell ?? "").Trim().ToUpperInvariant()))
                .Concat((s.Evidence ?? Array.Empty<SuggestionCellRefDto>()).Select(e => (Sheet: e.Sheet, Cell: e.Cell.Trim().ToUpperInvariant()))))
            .Where(x => x.Sheet.Length > 0 && x.Cell.Length > 0)
            .Distinct()
            .ToList();
        if (cited.Count == 0) return Array.Empty<SourceViewDto>();

        var views = new List<SourceViewDto>();
        foreach (var grp in cited.GroupBy(x => x.Sheet, StringComparer.OrdinalIgnoreCase))
        {
            var sheet = doc.Snapshot.FirstOrDefault(sn =>
                string.Equals(sn.File, source, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(sn.Sheet, grp.Key, StringComparison.OrdinalIgnoreCase));
            if (sheet is null) continue;

            var highlight = new HashSet<string>(grp.Select(x => x.Cell), StringComparer.OrdinalIgnoreCase);
            var coords = grp.Select(x => Spreadsheet.ParseA1(x.Cell)).Where(c => c is not null).Select(c => c!.Value).ToList();
            if (coords.Count == 0) continue;

            int minR = coords.Min(c => c.Row), maxR = coords.Max(c => c.Row);
            int minC = coords.Min(c => c.Col), maxC = coords.Max(c => c.Col);

            // Column window: from A (row labels live on the left) out to just past the
            // cited columns, capped so it stays readable.
            int fromCol = 1;
            int toCol = Math.Min(sheet.Cols, Math.Max(maxC + 1, minC + 1));
            if (toCol - fromCol > 13) fromCol = Math.Max(1, minC - 1);

            // Rows: the header rows (which carry the column labels) plus a window around
            // the cited rows so the numbers have context.
            var rowSet = new SortedSet<int>();
            foreach (var r in new[] { 1, 2 }) if (r <= sheet.Rows) rowSet.Add(r);
            for (int r = Math.Max(1, minR - 1); r <= Math.Min(sheet.Rows, maxR + 1); r++) rowSet.Add(r);

            var rows = rowSet.Select(r => BuildGridRow(sheet, r, fromCol, toCol, highlight)).ToList();
            views.Add(new SourceViewDto(sheet.Sheet, TabsFor(doc, source), rows));
        }
        return views;
    }

    private static SourceGridRowDto BuildGridRow(SheetSnapshot sheet, int row, int fromCol, int toCol, HashSet<string>? highlight)
    {
        var cells = new List<SourceGridCellDto>();
        for (int c = fromCol; c <= toCol; c++)
        {
            var col = Spreadsheet.ColLetter(c);
            sheet.Cells.TryGetValue($"{col}{row}", out var val);
            cells.Add(new SourceGridCellDto(col, val ?? "", highlight?.Contains($"{col}{row}") == true));
        }
        return new SourceGridRowDto(row, cells);
    }

    private static IReadOnlyList<string> TabsFor(ImportDocument doc, string source)
        => doc.Snapshot.Where(s => string.Equals(s.File, source, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Sheet).ToList();
}
