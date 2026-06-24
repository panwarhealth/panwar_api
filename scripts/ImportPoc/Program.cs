using Panwar.Api.Services.Import;

// Validation harness: parse one workbook (or every .xlsx/.xls in a folder) and
// print a reconciliation summary. Usage: dotnet run -- "<path>" [year]
var path = args.Length > 0 ? args[0] : ".";
var year = args.Length > 1 && int.TryParse(args[1], out var y) ? y : 2026;

var files = Directory.Exists(path)
    ? Directory.GetFiles(path, "*.xls*", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
    : new[] { path };

foreach (var file in files)
{
    if (Path.GetFileName(file).StartsWith("~$")) continue;
    Console.WriteLine($"\n================ {Path.GetFileName(file)} ================");
    var parser = ImportParser.Default();
    var doc = new ImportDocument { ClientSlug = "test", Year = year };
    try
    {
        using var wb = WorkbookLoader.Load(File.ReadAllBytes(file));
        parser.ParseInto(wb, new ParseContext { ClientSlug = "test", Year = year, FileName = Path.GetFileName(file) }, doc);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ERROR opening/parsing: {ex.Message}");
        continue;
    }

    foreach (var s in doc.Sources)
        Console.WriteLine($"  format={s.FormatId} match={s.Match}");

    Console.WriteLine($"  placements={doc.Placements.Count}  educationAssets={doc.Education.Count}  warnings={doc.Warnings.Count}");

    foreach (var p in doc.Placements)
    {
        var byMetric = p.Actuals.GroupBy(a => a.Metric)
            .Select(g => $"{g.Key}={g.Sum(a => a.Value):0.##}");
        Console.WriteLine($"    P: [{p.Publisher}/{p.Audience}/{p.Template}] {p.Name}  -> {string.Join(", ", byMetric)}");
        foreach (var mn in p.MonthNotes.OrderBy(x => x.Key))
            Console.WriteLine($"        month {mn.Key}: {mn.Value}");
    }
    foreach (var e in doc.Education)
    {
        var byStatus = e.Values.GroupBy(v => v.Status)
            .Select(g => $"{g.Key}={g.Sum(v => v.Value):0.##} ({g.Count()}mo)");
        Console.WriteLine($"    E: [{e.Brand}] {e.Title}  -> {string.Join(", ", byStatus)}");
    }
    foreach (var w in doc.Warnings)
        Console.WriteLine($"    ! {w.Message}");
}
