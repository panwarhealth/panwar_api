using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Npgsql;

// ── One-off Reckitt artwork backfill ────────────────────────────────────────
// The brand-tab cards hold the creative images but identify placements only by
// OS code (which the seeded DB lacks). However, each card's "Actual" cell is a
// formula into the YTD Data sheet (e.g. ='YTD Data'!D26), and YTD Data col B is
// the placement NAME the DB was seeded from. So: card → formula → YTD Data row →
// name → DB placement. We pick the nearest non-decorative image per card and set
// placement."ArtworkUrl". Best-effort + idempotent. See plan: Phase A.
//
// Usage: dotnet run -- [path-to-xlsx]   (defaults to the repo copy)

var workbookPath = args.Length > 0
    ? args[0]
    : @"F:\Github\panwar_portals\Reckitt 2025 HCP Media Performance Dashboards FINAL.xlsx";
var settingsPath = @"F:\Github\panwar_api\local.settings.json";

Console.WriteLine($"Workbook : {workbookPath}");
if (!File.Exists(workbookPath)) { Console.Error.WriteLine("Workbook not found."); return 1; }
if (!File.Exists(settingsPath)) { Console.Error.WriteLine("local.settings.json not found."); return 1; }

var cfg = JsonDocument.Parse(File.ReadAllText(settingsPath)).RootElement.GetProperty("Values");
string Cfg(string k) => cfg.GetProperty(k).GetString() ?? throw new InvalidOperationException($"{k} missing");
var accountId = Cfg("CLOUDFLARE_R2_ACCOUNT_ID");
var accessKey = Cfg("CLOUDFLARE_R2_ACCESS_KEY");
var secretKey = Cfg("CLOUDFLARE_R2_SECRET_KEY");
var bucket = Cfg("CLOUDFLARE_R2_BUCKET");
var connString = Cfg("DATABASE_CONNECTION_STRING");

// Brand×audience card tabs (digital cards carry the 'YTD Data' formula bridge).
var brandTabs = new[]
{
    "NUROFEN - Pharmacists", "NUROFEN - GPs",
    "NFC - Pharmacists", "NFC - GPs",
    "GAVISCON - Pharmacists", "GAVISCON - GPs",
};

using var fs = File.Open(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

string? Read(string name)
{
    var e = zip.GetEntry(name);
    if (e is null) return null;
    using var r = new StreamReader(e.Open());
    return r.ReadToEnd();
}

var shared = new List<string>();
foreach (Match si in Regex.Matches(Read("xl/sharedStrings.xml") ?? "", "<si>(.*?)</si>", RegexOptions.Singleline))
{
    var text = string.Concat(Regex.Matches(si.Groups[1].Value, "<t[^>]*>(.*?)</t>", RegexOptions.Singleline)
        .Select(m => m.Groups[1].Value));
    shared.Add(WebUtility.HtmlDecode(text));
}

var wb = Read("xl/workbook.xml") ?? "";
var wbRels = Read("xl/_rels/workbook.xml.rels") ?? "";
var ridToTarget = Regex.Matches(wbRels, "Id=\"(rId\\d+)\"[^>]*Target=\"(worksheets/sheet\\d+\\.xml)\"")
    .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
var sheetNameToFile = new Dictionary<string, string>();
foreach (Match m in Regex.Matches(wb, "<sheet[^>]*name=\"([^\"]+)\"[^>]*r:id=\"(rId\\d+)\""))
    if (ridToTarget.TryGetValue(m.Groups[2].Value, out var tgt))
        sheetNameToFile[WebUtility.HtmlDecode(m.Groups[1].Value)] = "xl/" + tgt;

static int ColToIndex(string col) { int i = 0; foreach (var c in col) i = i * 26 + (c - 'A' + 1); return i - 1; }

// Resolve a single cell value (handles shared strings + formula cached <v>).
string CellValue(string rowXml, string cellRef)
{
    var m = Regex.Match(rowXml, "<c r=\"" + cellRef + "\"(?:[^>]*t=\"(\\w+)\")?[^>]*>(?:<f>.*?</f>)?(?:<v>(.*?)</v>)?", RegexOptions.Singleline);
    if (!m.Success) return "";
    if (m.Groups[1].Value == "s" && m.Groups[2].Value != "") return shared[int.Parse(m.Groups[2].Value)];
    return m.Groups[2].Value;
}

// YTD Data: row → placement name (col B).
var ytdSheet = Read(sheetNameToFile["YTD Data"]) ?? "";
var ytdName = new Dictionary<int, string>();
foreach (Match rm in Regex.Matches(ytdSheet, "<row r=\"(\\d+)\"[^>]*>(.*?)</row>", RegexOptions.Singleline))
{
    var rn = int.Parse(rm.Groups[1].Value);
    var name = CellValue(rm.Groups[2].Value, $"B{rn}").Trim();
    if (name.Length > 0) ytdName[rn] = name;
}

// Decorative blocklist: images reused across the brand drawings.
var drawingByTab = new Dictionary<string, (string drawing, string rels)>();
var usage = new Dictionary<string, int>();
foreach (var tab in brandTabs)
{
    if (!sheetNameToFile.TryGetValue(tab, out var sheetFile)) continue;
    var num = Regex.Match(sheetFile, "sheet(\\d+)\\.xml").Groups[1].Value;
    var rels = Read($"xl/worksheets/_rels/sheet{num}.xml.rels");
    var dm = rels is null ? Match.Empty : Regex.Match(rels, "drawings/(drawing\\d+\\.xml)");
    if (!dm.Success) continue;
    var drawing = Read("xl/drawings/" + dm.Groups[1].Value) ?? "";
    var drawingRels = Read($"xl/drawings/_rels/{dm.Groups[1].Value}.rels") ?? "";
    drawingByTab[tab] = (drawing, drawingRels);
    var embedToFile = Regex.Matches(drawingRels, "Id=\"(rId\\d+)\"[^>]*Target=\"\\.\\./media/([^\"]+)\"")
        .ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value);
    foreach (Match em in Regex.Matches(drawing, "r:embed=\"(rId\\d+)\""))
        if (embedToFile.TryGetValue(em.Groups[1].Value, out var f)) usage[f] = usage.GetValueOrDefault(f) + 1;
}
var decorative = usage.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToHashSet();
Console.WriteLine($"Distinct card images: {usage.Count}; decorative (reused, excluded): {decorative.Count}");

List<(int Col, string File)> ImageAnchors(string tab)
{
    var list = new List<(int, string)>();
    if (!drawingByTab.TryGetValue(tab, out var d)) return list;
    var embedToFile = Regex.Matches(d.rels, "Id=\"(rId\\d+)\"[^>]*Target=\"\\.\\./media/([^\"]+)\"")
        .ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value);
    foreach (Match a in Regex.Matches(d.drawing, "<xdr:twoCellAnchor.*?</xdr:twoCellAnchor>", RegexOptions.Singleline))
    {
        var from = Regex.Match(a.Value, "<xdr:from>.*?<xdr:col>(\\d+)</xdr:col>", RegexOptions.Singleline);
        var embed = Regex.Match(a.Value, "r:embed=\"(rId\\d+)\"");
        if (from.Success && embed.Success && embedToFile.TryGetValue(embed.Groups[1].Value, out var f))
            list.Add((int.Parse(from.Groups[1].Value), f));
    }
    return list;
}

byte[]? MediaBytes(string file)
{
    var e = zip.GetEntry("xl/media/" + file);
    if (e is null) return null;
    using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
}
static string ContentType(string f) => Path.GetExtension(f).ToLowerInvariant() switch
{
    ".png" => "image/png", ".jpeg" or ".jpg" => "image/jpeg", ".gif" => "image/gif",
    ".webp" => "image/webp", ".svg" => "image/svg+xml", _ => "application/octet-stream",
};
static string Slug(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

var s3 = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey),
    new AmazonS3Config { ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com", ForcePathStyle = true });
await using var db = new NpgsqlConnection(connString);
await db.OpenAsync();

int matched = 0, noImage = 0, noDbName = 0;
var usedFiles = new HashSet<string>();

foreach (var tab in brandTabs)
{
    if (!sheetNameToFile.TryGetValue(tab, out var sheetFile)) continue;
    var sx = Read(sheetFile) ?? "";

    // Digital cards: cells whose formula references 'YTD Data'!<col><row>. Group by
    // referenced row (= one placement); block column = leftmost referencing cell.
    var rowToCols = new Dictionary<int, List<int>>();
    foreach (Match c in Regex.Matches(sx, "<c r=\"([A-Z]+)\\d+\"[^>]*><f>([^<]*?'YTD Data'![^<]*)</f>"))
    {
        var cellCol = ColToIndex(c.Groups[1].Value);
        var refm = Regex.Match(c.Groups[2].Value, "'YTD Data'!\\$?[A-Z]+\\$?(\\d+)");
        if (!refm.Success) continue;
        var ytdRow = int.Parse(refm.Groups[1].Value);
        if (!ytdName.ContainsKey(ytdRow)) continue;
        (rowToCols.TryGetValue(ytdRow, out var l) ? l : rowToCols[ytdRow] = new List<int>()).Add(cellCol);
    }

    var anchors = ImageAnchors(tab).Where(a => !decorative.Contains(a.File)).ToList();

    foreach (var (ytdRow, cols) in rowToCols)
    {
        var name = ytdName[ytdRow];
        var blockCol = cols.Min();

        var pick = anchors
            .Where(a => !usedFiles.Contains(a.File) && Math.Abs(a.Col - blockCol) <= 9)
            .Select(a => (a, bytes: MediaBytes(a.File)))
            .Where(x => x.bytes is not null)
            .OrderByDescending(x => x.bytes!.Length)
            .Cast<(( int Col, string File) a, byte[]? bytes)?>()
            .FirstOrDefault();

        if (pick is null) { noImage++; Console.WriteLine($"[{tab}] '{name}': no creative image near col {blockCol}"); continue; }

        // does the DB have this placement name?
        long n;
        await using (var cnt = new NpgsqlCommand("SELECT count(*) FROM panwar_portals.placement WHERE \"Name\" = @name", db))
        { cnt.Parameters.AddWithValue("name", name); n = (long)(await cnt.ExecuteScalarAsync())!; }
        if (n == 0) { noDbName++; Console.WriteLine($"[{tab}] '{name}': no DB placement with this name"); continue; }

        var ext = Path.GetExtension(pick.Value.a.File);
        var key = $"artwork/reckitt/{Slug(name)}{ext}";
        usedFiles.Add(pick.Value.a.File);

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket, Key = key,
            InputStream = new MemoryStream(pick.Value.bytes!),
            ContentType = ContentType(pick.Value.a.File),
            DisablePayloadSigning = true,
        });
        await using var upd = new NpgsqlCommand(
            "UPDATE panwar_portals.placement SET \"ArtworkUrl\" = @key, \"UpdatedAt\" = now() WHERE \"Name\" = @name", db);
        upd.Parameters.AddWithValue("key", key);
        upd.Parameters.AddWithValue("name", name);
        var rows = await upd.ExecuteNonQueryAsync();
        matched++;
        Console.WriteLine($"[{tab}] '{name}' → {pick.Value.a.File} ({pick.Value.bytes!.Length / 1024}KB) → {rows} placement(s)");
    }
}

Console.WriteLine();
Console.WriteLine($"Matched + uploaded: {matched} | card without image: {noImage} | card name not in DB: {noDbName}");
await using (var cov = new NpgsqlCommand(
    @"SELECT c.""Slug"", count(*) FILTER (WHERE p.""ArtworkUrl"" IS NOT NULL), count(*)
      FROM panwar_portals.placement p
      JOIN panwar_portals.brand b ON b.""Id"" = p.""BrandId""
      JOIN panwar_portals.client c ON c.""Id"" = b.""ClientId""
      GROUP BY c.""Slug"" ORDER BY c.""Slug""", db))
await using (var rdr = await cov.ExecuteReaderAsync())
    while (await rdr.ReadAsync())
        Console.WriteLine($"  client {rdr.GetString(0)}: {rdr.GetInt64(1)}/{rdr.GetInt64(2)} placements have artwork");

Console.WriteLine("Done.");
return 0;
