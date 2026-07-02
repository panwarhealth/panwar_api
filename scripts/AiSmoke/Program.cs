using System.Diagnostics;
using System.Text;
using System.Text.Json;

// Step-0 smoke test: does the live Anthropic integration work end to end, and does
// Sonnet 5 map the real "AP Solus eDMs" block to the right per-send placements using
// the per-month notes? Uses the REAL parsed notes + REAL candidate names (from the DB).

string key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? ReadKeyFromLocalSettings();
string model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-5";
if (string.IsNullOrWhiteSpace(key)) { Console.Error.WriteLine("No ANTHROPIC_API_KEY"); return; }

const string system =
    "You reconcile rows from a media-results spreadsheet against a client's existing placements.\n" +
    "RULES:\n" +
    "- Human notes and comments OVERRIDE the raw cell positions. If a note gives a different date/month or topic than where a number sits, trust the note.\n" +
    "- A single block can represent MULTIPLE sends across the year; each month's note names that send's topic and its real send date.\n" +
    "- For each flagged block, return one send entry per month that has a note or a value, using the note to set the topic and the correct month.\n" +
    "- Map each send to the single best matching existing placement by topic via target_ref, or 0 if none clearly fits.\n" +
    "- Be conservative: only give high confidence (>0.8) when a note or referenced tab makes the mapping clear.\n" +
    "Return JSON only, matching the provided schema.";

// Real data captured from the parser this session + candidates from the DB.
string user = """
FLAGGED BLOCKS (resolve each into per-send mappings):
[A] "AP Solus eDMs" - brand "AP", publisher ap, template Edm
    months with a value: 2 (sends=20685, unique_opens=10786, unique_clicks=261)
    month 2 note: Please refer to AP Solus eDM data tab for detials
    month 3 note: MSK Pain - w/c 16th March / RB Nurofen Adult 17 March 2026
    month 6 note: Headache - w/c 15th June
    month 8 note: Arthritis - w/c 17th August
    month 9 note: Period - w/c 14th September
    month 10 note: Dental - w/c 19th October

EXISTING PLACEMENTS you may map to (use the number as target_ref, or 0 for none):
[1] AP Solus eDM - CTA to Immunisation CPD Article + Portal (Edm)
[2] AP Solus eDM - CTA to Pain CPDs + Portal (Edm)
[3] AP Solus eDM - Gavi Gum/Senokot DA + CTA to Portal (Edm)
[4] AP Solus eDM - Immunisation (Edm)
[5] AP Solus eDM - Mini Caps w/ CTA to Portal (Edm)
[6] AP Solus eDM - MSK Pain (Edm)
[7] AP Solus eDM - Resource Summary (Edm)
""";

var schema = JsonSerializer.Deserialize<JsonElement>("""
{
  "type": "object", "additionalProperties": false, "required": ["blocks"],
  "properties": { "blocks": { "type": "array", "items": {
    "type": "object", "additionalProperties": false, "required": ["ref", "sends"],
    "properties": {
      "ref": { "type": "string" },
      "sends": { "type": "array", "items": {
        "type": "object", "additionalProperties": false,
        "required": ["month", "topic", "target_ref", "reason", "confidence"],
        "properties": {
          "month": { "type": "integer" }, "topic": { "type": "string" },
          "target_ref": { "type": "integer" }, "reason": { "type": "string" },
          "confidence": { "type": "number" } } } } } } } }
}
""");

var body = new Dictionary<string, object?>
{
    ["model"] = model,
    ["max_tokens"] = 4096,
    ["system"] = system,
    ["messages"] = new[] { new { role = "user", content = user } },
    ["output_config"] = new { format = new { type = "json_schema", schema } },
};

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
{
    Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
};
req.Headers.Add("x-api-key", key);
req.Headers.Add("anthropic-version", "2023-06-01");

Console.WriteLine($"Model: {model}\nCalling Anthropic...\n");
var sw = Stopwatch.StartNew();
using var resp = await http.SendAsync(req);
sw.Stop();
var respBody = await resp.Content.ReadAsStringAsync();
Console.WriteLine($"HTTP {(int)resp.StatusCode} in {sw.ElapsedMilliseconds} ms\n");
if (!resp.IsSuccessStatusCode) { Console.WriteLine(respBody); return; }

using var doc = JsonDocument.Parse(respBody);
var root = doc.RootElement;
var text = "";
foreach (var block in root.GetProperty("content").EnumerateArray())
    if (block.GetProperty("type").GetString() == "text") text = block.GetProperty("text").GetString() ?? "";

Console.WriteLine("=== MODEL OUTPUT (JSON) ===");
try { Console.WriteLine(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(text), new JsonSerializerOptions { WriteIndented = true })); }
catch { Console.WriteLine(text); }

if (root.TryGetProperty("usage", out var u))
{
    int inTok = u.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
    int outTok = u.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
    Console.WriteLine($"\n=== USAGE ===\ninput_tokens={inTok}  output_tokens={outTok}");
    Console.WriteLine($"(cost depends on Sonnet 5 rates - confirm via Models API; this is one flagged block)");
}

static string ReadKeyFromLocalSettings()
{
    var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "local.settings.json");
    if (!File.Exists(path)) return "";
    using var d = JsonDocument.Parse(File.ReadAllText(path));
    return d.RootElement.GetProperty("Values").TryGetProperty("ANTHROPIC_API_KEY", out var k) ? k.GetString() ?? "" : "";
}
