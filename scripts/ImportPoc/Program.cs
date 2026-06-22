using System.Text.Json;
using ClosedXML.Excel;
using Panwar.Tools.ImportPoc;

// PoC: parse a real publisher file into the canonical IR and print it as JSON.
// Usage: dotnet run -- "<path-to-xlsx>" [clientSlug] [year]

var path = args.Length > 0
    ? args[0]
    : @"C:\Users\User\Downloads\RESULTS\RESULTS\5. MAY\Reckitt AJP Results Template-May 2026.xlsx";
var clientSlug = args.Length > 1 ? args[1] : "reckitt";
var year = args.Length > 2 && int.TryParse(args[2], out var y) ? y : 2026;

if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

var parser = ImportParser.Default();
using var wb = new XLWorkbook(path);
var ctx = new ParseContext { ClientSlug = clientSlug, Year = year, FileName = Path.GetFileName(path) };
var doc = parser.ParseFile(wb, ctx);

var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
});
Console.WriteLine(json);

Console.Error.WriteLine(
    $"\n== summary ==\nadapter: {doc.Sources.FirstOrDefault()?.FormatId} ({doc.Sources.FirstOrDefault()?.Match})" +
    $"\nplacements: {doc.Placements.Count}  (actual rows: {doc.Placements.Sum(p => p.Actuals.Count)})" +
    $"\neducation assets: {doc.Education.Count}  (value rows: {doc.Education.Sum(e => e.Values.Count)})" +
    $"\nwarnings: {doc.Warnings.Count}");
return 0;
