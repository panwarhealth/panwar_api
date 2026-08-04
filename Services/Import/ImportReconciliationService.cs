using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services.Ai;

namespace Panwar.Api.Services.Import;

// Agentic loop over the workbook snapshot. Every cited value is re-checked against
// the snapshot before it is surfaced; the AI never writes. Runs logged to import_ai_log.
public class ImportReconciliationService : IImportReconciliationService
{
    private const double ConfidenceFloor = 0.7;   // below this we surface the topic but don't pre-fill a target
    private const decimal GroundingTolerance = 0.5m;

    private const string SystemPrompt =
        "You reconcile flagged rows from a media-results spreadsheet against a client's existing placements.\n" +
        "- Human notes and comments OVERRIDE raw cell positions. When a note or the referenced tab shows a send really happened in a different month than the row the numbers were typed in, put that send in the REAL month with the numbers attached to it there - never leave the values in the typed month with a vague topic.\n" +
        "- Name each send's topic from the note that describes it; only fall back to the block name when no note names a topic.\n" +
        "- A single block can represent MULTIPLE sends across the year; each month's note names that send's topic and its real send date. Return one send per month that has a note or a value.\n" +
        "- Map each send to the single best-matching existing placement by topic via target_ref. If no existing placement CLEARLY matches the same topic, use target_ref 0 - never map loosely to a placement about a different topic.\n" +
        "- When a note points to another tab (e.g. \"refer to the X tab\"), use read_tab / read_cells / read_comments to inspect it before deciding.\n" +
        "- If you assert a numeric value taken from a cell (e.g. a value read from a referenced tab), you MUST include it in that send's values with the exact source_sheet and source_cell you read it from. Only cite cells you actually read via a tool.\n" +
        "- When the same numbers appear in more than one place, cite them from the tab where the send's REAL row lives (e.g. the referenced data tab's March row), not from the mistyped row on the summary tab.\n" +
        "- In each send's evidence, cite the exact cells that told you WHEN the send happened or WHAT it was - the note cell, the date or month-label cell on the referenced tab. Only cells you actually read via a tool. These are shown to the human as proof.\n" +
        "- If a note lists the actual send dates for an email/eDM send (e.g. \"2 Mar, 11 Mar, 25 Mar\" or \"17 March 2026\"), return each one in send_dates as an ISO date (YYYY-MM-DD) using the reporting year. Leave send_dates empty when the note gives no specific dates.\n" +
        "- Be conservative: give confidence above 0.8 only when a note or referenced tab makes the mapping clear; use lower confidence when unsure.\n" +
        "- Keep every reason under 12 words.\n" +
        "When finished, call submit_result with your answer.";

    private const string SubmitSchemaJson = """
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
              "month": { "type": "integer" },
              "topic": { "type": "string" },
              "target_ref": { "type": "integer" },
              "reason": { "type": "string" },
              "confidence": { "type": "number" },
              "send_dates": { "type": "array", "items": { "type": "string" } },
              "evidence": { "type": "array", "items": {
                "type": "object", "additionalProperties": false,
                "required": ["sheet", "cell"],
                "properties": {
                  "sheet": { "type": "string" },
                  "cell": { "type": "string" }
                } } },
              "values": { "type": "array", "items": {
                "type": "object", "additionalProperties": false,
                "required": ["metric", "value", "source_sheet", "source_cell"],
                "properties": {
                  "metric": { "type": "string" },
                  "value": { "type": "number" },
                  "source_sheet": { "type": "string" },
                  "source_cell": { "type": "string" }
                } } }
            } } }
        } } } }
    }
    """;

    // ── Extraction mode: files no deterministic adapter could parse ──────────
    private static string BuildExtractSystemPrompt(MetricVocabulary vocabulary) =>
        "You extract media results from a client spreadsheet whose layout our deterministic parser does not recognise.\n" +
        "- Use the tools to read the tabs and work out the layout, then report every placement-level result you find: display/banner campaigns, eDM or email sends, sponsored content, education modules.\n" +
        "- One block per placement/campaign/send, named the way the file names it. If one month has several separately-reported sends, make each send its own block.\n" +
        "- Every value needs the calendar month (1-12) it belongs to; a single-send report belongs to its send or delivery date's month. Never spread a total across months.\n" +
        "- Never invent, sum or derive a number - only report values you read from a cell via a tool, each with the exact source_sheet and source_cell you read it from.\n" +
        $"- metric must be one of: {string.Join(", ", vocabulary.AllKeys)}. label is the file's own name for that number (e.g. \"Recipients Who Opened\"). Totals map to opens/clicks/sends; per-recipient counts map to the unique_ metrics. Skip rates, percentages and anything with no fitting metric.\n" +
        "- If the file states actual send dates for an email, return them in that month's send_dates as ISO YYYY-MM-DD dates.\n" +
        "- notes: short human-written guidance a reviewer should see (per block, or per month via note).\n" +
        "When finished, call submit_result.";

    private const string ExtractSchemaJson = """
    {
      "type": "object", "additionalProperties": false, "required": ["blocks"],
      "properties": { "blocks": { "type": "array", "items": {
        "type": "object", "additionalProperties": false, "required": ["name", "months"],
        "properties": {
          "name": { "type": "string" },
          "brand": { "type": "string" },
          "notes": { "type": "array", "items": { "type": "string" } },
          "months": { "type": "array", "items": {
            "type": "object", "additionalProperties": false, "required": ["month", "values"],
            "properties": {
              "month": { "type": "integer" },
              "note": { "type": "string" },
              "send_dates": { "type": "array", "items": { "type": "string" } },
              "values": { "type": "array", "items": {
                "type": "object", "additionalProperties": false,
                "required": ["metric", "label", "value", "source_sheet", "source_cell"],
                "properties": {
                  "metric": { "type": "string" },
                  "label": { "type": "string" },
                  "value": { "type": "number" },
                  "source_sheet": { "type": "string" },
                  "source_cell": { "type": "string" }
                } } }
            } } }
        } } } }
    }
    """;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDbContext _context;
    private readonly IAnthropicService _anthropic;
    private readonly IImportProgress _progress;
    private readonly ILogger<ImportReconciliationService> _logger;

    public ImportReconciliationService(
        AppDbContext context, IAnthropicService anthropic, IImportProgress progress, ILogger<ImportReconciliationService> logger)
    {
        _context = context;
        _anthropic = anthropic;
        _progress = progress;
        _logger = logger;
    }

    public bool IsEnabled => _anthropic.IsEnabled;

    public async Task<ReconResult> SuggestAsync(
        Guid clientId,
        ImportDocument doc,
        IReadOnlyList<int> flaggedIndices,
        IReadOnlyDictionary<string, string> fileHashByName,
        IReadOnlyList<ReconCandidate> candidates,
        Guid? userId,
        bool allowLive,
        Guid jobId,
        CancellationToken ct)
    {
        var result = new Dictionary<int, List<PlacementSuggestionDto>>();
        var failedFiles = new List<string>();
        if (!IsEnabled || flaggedIndices.Count == 0) return new ReconResult(result, failedFiles);

        foreach (var grp in flaggedIndices.GroupBy(i => doc.Placements[i].Source))
        {
            var file = grp.Key;
            var indices = grp.ToList();
            fileHashByName.TryGetValue(file, out var hash);

            List<CachedBlock>? blocks = await TryReadCacheAsync(clientId, hash, ct);
            // A cached answer names the placements it chose. If any of those are gone,
            // it was reasoning about a set of placements that no longer exists, so it
            // gets thrown away and worked out again rather than patched up.
            if (blocks is not null && !TargetsStillExist(blocks, candidates))
            {
                _logger.LogInformation("Discarding cached AI answer for {File}: it points at placements that no longer exist", file);
                blocks = null;
            }
            if (blocks is null)
            {
                if (!allowLive) continue; // preview path: cache-only, no AI calls
                _progress.Report(jobId, $"The AI is reading the notes in {file} - this is the slow bit, usually a minute or two...");
                bool completed;
                try
                {
                    (blocks, completed) = await RunFileAsync(clientId, doc, file, hash, indices, candidates, userId, jobId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI reconciliation failed for file {File}", file);
                    failedFiles.Add(file);
                    continue;
                }
                // Never cache an incomplete run; it must retry next time.
                if (completed) await WriteCacheAsync(clientId, hash, blocks, ct);
                else failedFiles.Add(file);
            }

            foreach (var i in indices)
            {
                var pp = doc.Placements[i];
                var block = blocks.FirstOrDefault(b =>
                    string.Equals(b.Name, pp.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(b.Brand, pp.Brand, StringComparison.OrdinalIgnoreCase));
                if (block is { Sends.Count: > 0 }) result[i] = block.Sends;
            }
        }

        return new ReconResult(result, failedFiles);
    }

    private static bool TargetsStillExist(List<CachedBlock> blocks, IReadOnlyList<ReconCandidate> candidates)
    {
        var live = candidates.Select(c => c.Id).ToHashSet();
        return blocks
            .SelectMany(b => b.Sends)
            .Select(s => s.TargetPlacementId)
            .OfType<Guid>()
            .All(live.Contains);
    }

    public async Task<ExtractResult> ExtractAsync(
        Guid clientId,
        ImportDocument doc,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, string> fileHashByName,
        MetricVocabulary vocabulary,
        Guid? userId,
        bool allowLive,
        Guid jobId,
        CancellationToken ct)
    {
        var byFile = new Dictionary<string, List<ParsedPlacement>>(StringComparer.Ordinal);
        var failedFiles = new List<string>();
        if (!IsEnabled || files.Count == 0) return new ExtractResult(byFile, failedFiles);

        foreach (var file in files)
        {
            fileHashByName.TryGetValue(file, out var hash);
            // Distinct key per mode: the two modes cache different JSON shapes.
            var cacheHash = ExtractCacheHash(hash);

            var cached = await TryReadExtractCacheAsync(clientId, cacheHash, ct);
            if (cached is null)
            {
                if (!allowLive) continue;
                _progress.Report(jobId, $"The AI is working out the layout of {file} - this is the slow bit, usually a minute or two...");
                bool completed;
                try
                {
                    (cached, completed) = await RunExtractFileAsync(clientId, doc, file, hash, vocabulary, userId, jobId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI extraction failed for file {File}", file);
                    failedFiles.Add(file);
                    continue;
                }
                if (completed) await WriteCacheJsonAsync(clientId, cacheHash, JsonSerializer.Serialize(cached), ct);
                else { failedFiles.Add(file); continue; }
            }

            byFile[file] = ToPlacements(file, cached, vocabulary);
        }

        return new ExtractResult(byFile, failedFiles);
    }

    private async Task<(List<CachedExtractBlock> Blocks, bool Completed)> RunExtractFileAsync(
        Guid clientId, ImportDocument doc, string file, string? hash, MetricVocabulary vocabulary,
        Guid? userId, Guid jobId, CancellationToken ct)
    {
        var sheets = doc.Snapshot.Where(s => string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"FILE: {file} (reporting year {doc.Year}). Our parser recognised nothing in it - extract the results yourself.");
        sb.AppendLine();
        sb.AppendLine("WORKBOOK TABS you can inspect with the tools (list_tabs/read_tab/read_cells/read_comments):");
        foreach (var s in sheets)
            sb.AppendLine($"    {s.Sheet} ({s.Rows} rows x {s.Cols} cols, {s.Comments.Count} comment(s))");

        var systemPrompt = BuildExtractSystemPrompt(vocabulary);
        var cellsRead = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tools = BuildTools(ExtractSchemaJson, "Submit every placement block you extracted from the file.");
        var request = new AgentRunRequest(
            systemPrompt, sb.ToString(), tools, "submit_result",
            (name, input, c) =>
            {
                _progress.Report(jobId, DescribeToolCall(name, input, file));
                return Task.FromResult(ExecuteTool(name, input, sheets, cellsRead));
            },
            OnStatus: msg => _progress.Report(jobId, msg));

        var sw = Stopwatch.StartNew();
        var run = await _anthropic.RunToolLoopAsync(request, ct);
        sw.Stop();

        if (run.HitMaxIterations)
            _logger.LogWarning(
                "AI extraction for {File} hit the {Max}-iteration cap - result may be incomplete",
                file, request.MaxIterations);

        _progress.Report(jobId, "Double-checking the AI's numbers against your file...");
        var verification = new JsonArray();
        var grounding = new JsonArray();
        var blocks = BuildExtractedBlocks(run.Answer, sheets, cellsRead, vocabulary, verification, grounding);

        await WriteLogAsync(clientId, file, hash, userId, systemPrompt, run, cellsRead, verification, grounding, (int)sw.ElapsedMilliseconds, ct);

        var completed = run.Answer is JsonElement a
            && a.ValueKind == JsonValueKind.Object
            && a.TryGetProperty("blocks", out _);
        return (blocks, completed);
    }

    private List<CachedExtractBlock> BuildExtractedBlocks(
        JsonElement? answer,
        List<SheetSnapshot> sheets,
        Dictionary<string, string> cellsRead,
        MetricVocabulary vocabulary,
        JsonArray verification,
        JsonArray grounding)
    {
        var output = new List<CachedExtractBlock>();
        if (answer is not JsonElement ans) return output;

        var validMetrics = vocabulary.AllKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parsed = ans.Deserialize<AiExtractResponse>(Json);
        foreach (var b in parsed?.Blocks ?? new())
        {
            if (string.IsNullOrWhiteSpace(b.Name)) continue;
            var block = new CachedExtractBlock
            {
                Name = b.Name.Trim(),
                Brand = b.Brand?.Trim() ?? "",
                Notes = (b.Notes ?? new()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList(),
            };
            foreach (var m in b.Months ?? new())
            {
                bool monthOk = m.Month is >= 1 and <= 12;
                verification.Add(new JsonObject
                {
                    ["block"] = block.Name,
                    ["month"] = m.Month,
                    ["kept"] = monthOk,
                    ["reason"] = monthOk ? null : "month out of range",
                });
                if (!monthOk) continue;

                var month = new CachedExtractMonth
                {
                    Month = m.Month,
                    Note = string.IsNullOrWhiteSpace(m.Note) ? null : m.Note.Trim(),
                    SendDates = NormalizeSendDates(m.SendDates).ToList(),
                };
                foreach (var v in m.Values ?? new())
                {
                    var metric = (v.Metric ?? "").Trim().ToLowerInvariant();
                    var sheetName = (v.SourceSheet ?? "").Trim();
                    var cell = (v.SourceCell ?? "").Trim().ToUpperInvariant();
                    var key = $"{sheetName}!{cell}";
                    var sheet = FindSheet(sheets, sheetName);

                    string verdict;
                    decimal? actual = null;
                    if (!validMetrics.Contains(metric)) verdict = "unknown_metric";
                    else if (!cellsRead.ContainsKey(key)) verdict = "not_read";
                    else if (sheet is null || !sheet.Cells.TryGetValue(cell, out var raw)) verdict = "missing";
                    else if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)) verdict = "not_numeric";
                    else { actual = a; verdict = Math.Abs(a - v.Value) <= GroundingTolerance ? "ok" : "mismatch"; }

                    grounding.Add(new JsonObject
                    {
                        ["kind"] = "extract",
                        ["metric"] = metric,
                        ["sheet"] = sheetName,
                        ["cell"] = cell,
                        ["claimed"] = v.Value,
                        ["actual"] = actual,
                        ["verdict"] = verdict,
                    });

                    if (verdict == "ok")
                        month.Values.Add(new CachedExtractValue
                        {
                            Metric = metric,
                            Label = v.Label?.Trim() ?? "",
                            Value = v.Value,
                            Sheet = sheetName,
                            Cell = cell,
                        });
                }
                if (month.Values.Count > 0 || month.Note is not null) block.Months.Add(month);
            }
            if (block.Months.Count > 0) output.Add(block);
        }
        return output;
    }

    private static List<ParsedPlacement> ToPlacements(string file, List<CachedExtractBlock> blocks, MetricVocabulary vocabulary)
    {
        var result = new List<ParsedPlacement>();
        foreach (var b in blocks)
        {
            var keys = b.Months.SelectMany(m => m.Values).Select(v => v.Metric).Distinct().ToList();
            if (keys.Count == 0) continue;
            var pp = new ParsedPlacement
            {
                Source = file,
                Brand = b.Brand,
                Audience = null,
                Publisher = "",
                Template = vocabulary.InferTemplate(b.Name, keys),
                Name = b.Name,
                Objective = Spreadsheet.InferObjective(b.Name),
                FromAi = true,
            };
            foreach (var m in b.Months.OrderBy(x => x.Month))
            {
                if (m.Note is not null) pp.MonthNotes[m.Month] = m.Note;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in m.Values)
                {
                    if (!seen.Add(v.Metric)) continue;
                    var normLabel = MetricNaming.Normalize(v.Label);
                    pp.Actuals.Add(new ParsedActual
                    {
                        Metric = v.Metric,
                        Month = m.Month,
                        Value = v.Value,
                        Note = normLabel.Length == 0 || string.Equals(normLabel, v.Metric, StringComparison.OrdinalIgnoreCase)
                            ? null : $"file calls this '{v.Label}'",
                        SourceSheet = v.Sheet,
                        SourceCell = v.Cell,
                    });
                }
                if (m.SendDates.Count > 0)
                    pp.Notes.Add($"Send date{(m.SendDates.Count > 1 ? "s" : "")}: {string.Join(", ", m.SendDates)}");
            }
            pp.Notes.AddRange(b.Notes);
            if (pp.Actuals.Count > 0) result.Add(pp);
        }
        return result;
    }

    private static string ExtractCacheHash(string? hash)
        => string.IsNullOrEmpty(hash)
            ? ""
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("extract|" + hash))).ToLowerInvariant();

    private async Task<List<CachedExtractBlock>?> TryReadExtractCacheAsync(Guid clientId, string cacheHash, CancellationToken ct)
    {
        if (cacheHash.Length == 0) return null;
        var row = await _context.ImportAiCaches.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.ContentHash == cacheHash, ct);
        if (row is null) return null;
        try { return JsonSerializer.Deserialize<List<CachedExtractBlock>>(row.SuggestionsJson, Json); }
        catch { return null; }
    }

    private async Task<(List<CachedBlock> Blocks, bool Completed)> RunFileAsync(
        Guid clientId, ImportDocument doc, string file, string? hash, List<int> indices,
        IReadOnlyList<ReconCandidate> candidates, Guid? userId, Guid jobId, CancellationToken ct)
    {
        var flagged = indices.Select(i => doc.Placements[i]).ToList();
        var blocksByRef = new Dictionary<string, ParsedPlacement>(StringComparer.OrdinalIgnoreCase);

        var relevant = candidates
            .Where(c => flagged.Any(f =>
                string.Equals(c.Publisher, f.Publisher, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Brand, f.Brand, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (relevant.Count == 0) relevant = candidates.ToList();
        relevant = relevant.OrderBy(c => c.Name).Take(80).ToList();
        var candByRef = relevant.Select((c, idx) => (Ref: idx + 1, Cand: c)).ToDictionary(x => x.Ref, x => x.Cand);

        var sheets = doc.Snapshot.Where(s => string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase)).ToList();
        var userContent = BuildUserContent(flagged, blocksByRef, candByRef, sheets);

        var cellsRead = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tools = BuildTools(SubmitSchemaJson, "Submit the final per-send reconciliation for every flagged block.");
        var request = new AgentRunRequest(
            SystemPrompt, userContent, tools, "submit_result",
            (name, input, c) =>
            {
                _progress.Report(jobId, DescribeToolCall(name, input, file));
                return Task.FromResult(ExecuteTool(name, input, sheets, cellsRead));
            },
            OnStatus: msg => _progress.Report(jobId, msg));

        var sw = Stopwatch.StartNew();
        var run = await _anthropic.RunToolLoopAsync(request, ct);
        sw.Stop();

        if (run.HitMaxIterations)
            _logger.LogWarning(
                "AI reconciliation for {File} hit the {Max}-iteration cap - result may be incomplete",
                file, request.MaxIterations);

        _progress.Report(jobId, "Double-checking the AI's numbers against your file...");
        var verification = new JsonArray();
        var grounding = new JsonArray();
        var output = BuildSuggestions(run.Answer, blocksByRef, candByRef, sheets, cellsRead, verification, grounding);

        await WriteLogAsync(clientId, file, hash, userId, SystemPrompt, run, cellsRead, verification, grounding, (int)sw.ElapsedMilliseconds, ct);

        // A truncated submit_result arrives as {}, so require the "blocks" property.
        var completed = run.Answer is JsonElement a
            && a.ValueKind == JsonValueKind.Object
            && a.TryGetProperty("blocks", out _);
        return (output, completed);
    }

    private static string BuildUserContent(
        List<ParsedPlacement> flagged,
        Dictionary<string, ParsedPlacement> blocksByRef,
        Dictionary<int, ReconCandidate> candByRef,
        List<SheetSnapshot> sheets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FLAGGED BLOCKS (resolve each into per-send mappings):");
        for (int n = 0; n < flagged.Count; n++)
        {
            var f = flagged[n];
            var rf = ((char)('A' + n)).ToString();
            blocksByRef[rf] = f;
            sb.AppendLine($"[{rf}] \"{f.Name}\" - brand \"{f.Brand}\", publisher {f.Publisher}, template {f.Template}");
            var dataMonths = f.Actuals.Select(a => a.Month).Distinct().OrderBy(m => m).ToList();
            if (dataMonths.Count > 0) sb.AppendLine($"    months with a value: {string.Join(", ", dataMonths)}");
            foreach (var mn in f.MonthNotes.OrderBy(x => x.Key))
                sb.AppendLine($"    month {mn.Key} note: {mn.Value}");
            foreach (var bn in f.Notes.Where(x => !f.MonthNotes.Values.Contains(x)).Take(6))
                sb.AppendLine($"    note: {bn}");
        }

        sb.AppendLine();
        sb.AppendLine("WORKBOOK TABS you can inspect with the tools (list_tabs/read_tab/read_cells/read_comments):");
        foreach (var s in sheets)
            sb.AppendLine($"    {s.Sheet} ({s.Rows} rows x {s.Cols} cols, {s.Comments.Count} comment(s))");

        sb.AppendLine();
        sb.AppendLine("EXISTING PLACEMENTS you may map to (use the number as target_ref, or 0 for none):");
        foreach (var (rf, c) in candByRef.OrderBy(x => x.Key))
        {
            var held = c.Months.Count > 0 ? string.Join(", ", c.Months) : "none";
            sb.AppendLine($"[{rf}] {c.Name} ({c.Template}) - already has numbers for month(s): {held}");
        }
        return sb.ToString();
    }

    private static IReadOnlyList<AgentTool> BuildTools(string submitSchemaJson, string submitDescription) => new[]
    {
        new AgentTool("list_tabs", "List the workbook's sheet names with their row/column extents.",
            JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{}}""")!),
        new AgentTool("read_tab", "Read a whole sheet as a text grid of its non-empty cells (with A1 references).",
            JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["sheet"],"properties":{"sheet":{"type":"string"}}}""")!),
        new AgentTool("read_cells", "Read specific cells of a sheet by their A1 references.",
            JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["sheet","cells"],"properties":{"sheet":{"type":"string"},"cells":{"type":"array","items":{"type":"string"}}}}""")!),
        new AgentTool("read_comments", "Read the Excel cell-comments on a sheet (or all sheets if none given), each with its cell reference.",
            JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{"sheet":{"type":"string"}}}""")!),
        new AgentTool("submit_result", submitDescription,
            JsonNode.Parse(submitSchemaJson)!),
    };

    private static string ExecuteTool(string name, JsonElement input, List<SheetSnapshot> sheets, Dictionary<string, string> cellsRead)
    {
        switch (name)
        {
            case "list_tabs":
                return string.Join("\n", sheets.Select(s => $"{s.Sheet} ({s.Rows} rows x {s.Cols} cols)"));

            case "read_tab":
            {
                var sheet = FindSheet(sheets, GetString(input, "sheet"));
                if (sheet is null) return "error: sheet not found";
                var sb = new StringBuilder();
                for (int r = 1; r <= sheet.Rows; r++)
                {
                    var cells = new List<string>();
                    for (int c = 1; c <= sheet.Cols; c++)
                    {
                        var a1 = $"{Spreadsheet.ColLetter(c)}{r}";
                        if (sheet.Cells.TryGetValue(a1, out var v))
                        {
                            cells.Add($"{Spreadsheet.ColLetter(c)}={v}");
                            cellsRead[$"{sheet.Sheet}!{a1}"] = v;
                        }
                    }
                    if (cells.Count > 0) sb.Append('r').Append(r).Append(": ").AppendLine(string.Join(" | ", cells));
                }
                return sb.Length == 0 ? "(empty sheet)" : sb.ToString();
            }

            case "read_cells":
            {
                var sheet = FindSheet(sheets, GetString(input, "sheet"));
                if (sheet is null) return "error: sheet not found";
                var lines = new List<string>();
                if (input.TryGetProperty("cells", out var cellsEl) && cellsEl.ValueKind == JsonValueKind.Array)
                    foreach (var cellEl in cellsEl.EnumerateArray())
                    {
                        var a1 = (cellEl.GetString() ?? "").Trim().ToUpperInvariant();
                        if (a1.Length == 0) continue;
                        var v = sheet.Cells.TryGetValue(a1, out var val) ? val : "(empty)";
                        lines.Add($"{a1}={v}");
                        cellsRead[$"{sheet.Sheet}!{a1}"] = v;
                    }
                return lines.Count == 0 ? "(no cells)" : string.Join("\n", lines);
            }

            case "read_comments":
            {
                var wanted = GetString(input, "sheet");
                var targets = string.IsNullOrWhiteSpace(wanted)
                    ? sheets
                    : sheets.Where(s => string.Equals(s.Sheet, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
                var lines = new List<string>();
                foreach (var s in targets)
                    foreach (var cm in s.Comments)
                        lines.Add($"{s.Sheet}!{cm.Cell}: {cm.Text}");
                return lines.Count == 0 ? "(no comments)" : string.Join("\n", lines);
            }

            default:
                return $"error: unknown tool {name}";
        }
    }

    private List<CachedBlock> BuildSuggestions(
        JsonElement? answer,
        Dictionary<string, ParsedPlacement> blocksByRef,
        Dictionary<int, ReconCandidate> candByRef,
        List<SheetSnapshot> sheets,
        Dictionary<string, string> cellsRead,
        JsonArray verification,
        JsonArray grounding)
    {
        var output = new List<CachedBlock>();
        if (answer is not JsonElement ans) return output;

        var parsed = ans.Deserialize<AiResponse>(Json);
        foreach (var b in parsed?.Blocks ?? new())
        {
            if (b.Ref is null || !blocksByRef.TryGetValue(b.Ref, out var pp)) continue;
            var sends = new List<PlacementSuggestionDto>();
            foreach (var s in b.Sends ?? new())
            {
                bool monthOk = s.Month is >= 1 and <= 12;
                bool topicOk = !string.IsNullOrWhiteSpace(s.Topic);
                Guid? targetId = null;
                string? targetName = null;
                bool targetResolved = s.TargetRef == 0 || candByRef.ContainsKey(s.TargetRef);
                var confidence = Math.Clamp(s.Confidence, 0, 1);

                if (s.TargetRef > 0 && candByRef.TryGetValue(s.TargetRef, out var cand) && confidence >= ConfidenceFloor)
                {
                    targetId = cand.Id;
                    targetName = cand.Name;
                }

                var groundedValues = GroundValues(s.Values, sheets, cellsRead, grounding);
                var groundedEvidence = GroundEvidence(s.Evidence, sheets, cellsRead, grounding);

                bool kept = monthOk && topicOk && targetResolved;
                verification.Add(new JsonObject
                {
                    ["ref"] = b.Ref,
                    ["month"] = s.Month,
                    ["topic"] = s.Topic,
                    ["targetRef"] = s.TargetRef,
                    ["confidence"] = confidence,
                    ["kept"] = kept,
                    ["reason"] = kept ? null : (!monthOk ? "month out of range" : !topicOk ? "empty topic" : "target_ref does not resolve"),
                });
                if (!kept) continue;

                sends.Add(new PlacementSuggestionDto(
                    s.Month, s.Topic!.Trim(), targetId, targetName, s.Reason?.Trim() ?? "", confidence,
                    groundedValues, NormalizeSendDates(s.SendDates), groundedEvidence));
            }
            output.Add(new CachedBlock { Name = pp.Name, Brand = pp.Brand, Sends = sends });
        }
        return output;
    }

    // Strict ISO only: a slash date is day/month ambiguous, so drop rather than guess.
    private static IReadOnlyList<string> NormalizeSendDates(List<string>? raw)
    {
        if (raw is null || raw.Count == 0) return Array.Empty<string>();
        var kept = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in raw)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            if (DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                kept.Add(d.ToString("yyyy-MM-dd"));
        }
        return kept.ToList();
    }

    // Keep a value only if that cell was served by a tool and still matches.
    private static List<SuggestionValueDto> GroundValues(
        List<AiValue>? values, List<SheetSnapshot> sheets, Dictionary<string, string> cellsRead, JsonArray grounding)
    {
        var kept = new List<SuggestionValueDto>();
        foreach (var v in values ?? new())
        {
            var sheetName = (v.SourceSheet ?? "").Trim();
            var cell = (v.SourceCell ?? "").Trim().ToUpperInvariant();
            var key = $"{sheetName}!{cell}";
            var sheet = FindSheet(sheets, sheetName);

            string verdict;
            decimal? actual = null;
            if (!cellsRead.ContainsKey(key)) verdict = "not_read";
            else if (sheet is null || !sheet.Cells.TryGetValue(cell, out var raw)) verdict = "missing";
            else if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)) verdict = "not_numeric";
            else { actual = a; verdict = Math.Abs(a - v.Value) <= GroundingTolerance ? "ok" : "mismatch"; }

            grounding.Add(new JsonObject
            {
                ["metric"] = v.Metric,
                ["sheet"] = sheetName,
                ["cell"] = cell,
                ["claimed"] = v.Value,
                ["actual"] = actual,
                ["verdict"] = verdict,
            });

            if (verdict == "ok")
                kept.Add(new SuggestionValueDto(v.Metric?.Trim() ?? "", v.Value, sheetName, cell));
        }
        return kept;
    }

    // As GroundValues, without the numeric compare.
    private static List<SuggestionCellRefDto> GroundEvidence(
        List<AiEvidence>? evidence, List<SheetSnapshot> sheets, Dictionary<string, string> cellsRead, JsonArray grounding)
    {
        var kept = new List<SuggestionCellRefDto>();
        foreach (var e in evidence ?? new())
        {
            var sheetName = (e.Sheet ?? "").Trim();
            var cell = (e.Cell ?? "").Trim().ToUpperInvariant();
            if (sheetName.Length == 0 || cell.Length == 0) continue;
            var key = $"{sheetName}!{cell}";
            var sheet = FindSheet(sheets, sheetName);

            string verdict;
            if (!cellsRead.ContainsKey(key)) verdict = "not_read";
            else if (sheet is null || !sheet.Cells.TryGetValue(cell, out var raw) || string.IsNullOrWhiteSpace(raw)) verdict = "missing";
            else verdict = "ok";

            grounding.Add(new JsonObject
            {
                ["kind"] = "evidence",
                ["sheet"] = sheetName,
                ["cell"] = cell,
                ["verdict"] = verdict,
            });

            if (verdict == "ok")
                kept.Add(new SuggestionCellRefDto(sheetName, cell));
        }
        return kept;
    }

    private async Task WriteLogAsync(
        Guid clientId, string file, string? hash, Guid? userId, string systemPrompt, AgentRunResult run,
        Dictionary<string, string> cellsRead, JsonArray verification, JsonArray grounding, int durationMs, CancellationToken ct)
    {
        try
        {
            _context.ImportAiLogs.Add(new ImportAiLog
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                FileName = file,
                ContentHash = hash ?? "",
                Model = _anthropic.Model,
                RequestedByUserId = userId,
                SystemPrompt = systemPrompt,
                TranscriptJson = run.TranscriptJson,
                AnswerJson = run.Answer?.GetRawText(),
                VerificationJson = verification.ToJsonString(),
                CellsReadJson = JsonSerializer.Serialize(cellsRead),
                GroundingJson = grounding.ToJsonString(),
                InputTokens = run.InputTokens,
                OutputTokens = run.OutputTokens,
                ToolCallCount = run.ToolCallCount,
                DurationMs = durationMs,
                CreatedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write import_ai_log for {File}", file);
        }
    }

    private static string DescribeToolCall(string name, JsonElement input, string file)
    {
        var sheet = GetString(input, "sheet");
        switch (name)
        {
            case "list_tabs":
                return "The AI is checking what tabs the file has...";
            case "read_tab":
                return $"The AI is reading the whole '{sheet}' tab...";
            case "read_comments":
                return sheet is null
                    ? "The AI is reading the comments in the file..."
                    : $"The AI is reading the comments on the '{sheet}' tab...";
            case "read_cells":
            {
                var cells = new List<string>();
                if (input.ValueKind == JsonValueKind.Object && input.TryGetProperty("cells", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (el.GetString() is { Length: > 0 } s) cells.Add(s.Trim().ToUpperInvariant());
                        if (cells.Count == 6) break;
                    }
                if (cells.Count == 0) return $"The AI is reading cells on the '{sheet}' tab...";
                var list = cells.Count > 5 ? string.Join(", ", cells.Take(5)) + "…" : string.Join(", ", cells);
                return $"The AI is reading cell{(cells.Count > 1 ? "s" : "")} {list} on the '{sheet}' tab...";
            }
            default:
                return $"The AI is working through the notes in {file}...";
        }
    }

    private static SheetSnapshot? FindSheet(List<SheetSnapshot> sheets, string? name)
        => name is null ? null : sheets.FirstOrDefault(s => string.Equals(s.Sheet, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? GetString(JsonElement input, string prop)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(prop, out var v) ? v.GetString() : null;

    private async Task<List<CachedBlock>?> TryReadCacheAsync(Guid clientId, string? hash, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(hash)) return null;
        var row = await _context.ImportAiCaches.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.ContentHash == hash, ct);
        if (row is null) return null;
        try { return JsonSerializer.Deserialize<List<CachedBlock>>(row.SuggestionsJson, Json); }
        catch { return null; }
    }

    private Task WriteCacheAsync(Guid clientId, string? hash, List<CachedBlock> blocks, CancellationToken ct)
        => WriteCacheJsonAsync(clientId, hash ?? "", JsonSerializer.Serialize(blocks), ct);

    // Overwrites rather than skips: a re-run only happens when the cached answer was
    // thrown away, so skipping the write would leave the bad row in place and make
    // every future preview pay for a fresh AI run that is never kept.
    private async Task WriteCacheJsonAsync(Guid clientId, string hash, string json, CancellationToken ct)
    {
        if (hash.Length == 0) return;
        var existing = await _context.ImportAiCaches
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.ContentHash == hash, ct);
        if (existing is not null)
        {
            existing.SuggestionsJson = json;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.ImportAiCaches.Add(new ImportAiCache
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                ContentHash = hash,
                SuggestionsJson = json,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await _context.SaveChangesAsync(ct);
    }

    private sealed class CachedBlock
    {
        public string Name { get; set; } = "";
        public string Brand { get; set; } = "";
        public List<PlacementSuggestionDto> Sends { get; set; } = new();
    }

    private sealed class CachedExtractBlock
    {
        public string Name { get; set; } = "";
        public string Brand { get; set; } = "";
        public List<string> Notes { get; set; } = new();
        public List<CachedExtractMonth> Months { get; set; } = new();
    }

    private sealed class CachedExtractMonth
    {
        public int Month { get; set; }
        public string? Note { get; set; }
        public List<string> SendDates { get; set; } = new();
        public List<CachedExtractValue> Values { get; set; } = new();
    }

    private sealed class CachedExtractValue
    {
        public string Metric { get; set; } = "";
        public string Label { get; set; } = "";
        public decimal Value { get; set; }
        public string Sheet { get; set; } = "";
        public string Cell { get; set; } = "";
    }

    private sealed class AiExtractResponse
    {
        [JsonPropertyName("blocks")] public List<AiExtractBlock>? Blocks { get; set; }
    }

    private sealed class AiExtractBlock
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("notes")] public List<string>? Notes { get; set; }
        [JsonPropertyName("months")] public List<AiExtractMonth>? Months { get; set; }
    }

    private sealed class AiExtractMonth
    {
        [JsonPropertyName("month")] public int Month { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("send_dates")] public List<string>? SendDates { get; set; }
        [JsonPropertyName("values")] public List<AiExtractValue>? Values { get; set; }
    }

    private sealed class AiExtractValue
    {
        [JsonPropertyName("metric")] public string? Metric { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("value")] public decimal Value { get; set; }
        [JsonPropertyName("source_sheet")] public string? SourceSheet { get; set; }
        [JsonPropertyName("source_cell")] public string? SourceCell { get; set; }
    }

    private sealed class AiResponse
    {
        [JsonPropertyName("blocks")] public List<AiBlock>? Blocks { get; set; }
    }

    private sealed class AiBlock
    {
        [JsonPropertyName("ref")] public string? Ref { get; set; }
        [JsonPropertyName("sends")] public List<AiSend>? Sends { get; set; }
    }

    private sealed class AiSend
    {
        [JsonPropertyName("month")] public int Month { get; set; }
        [JsonPropertyName("topic")] public string? Topic { get; set; }
        [JsonPropertyName("target_ref")] public int TargetRef { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("confidence")] public double Confidence { get; set; }
        [JsonPropertyName("send_dates")] public List<string>? SendDates { get; set; }
        [JsonPropertyName("evidence")] public List<AiEvidence>? Evidence { get; set; }
        [JsonPropertyName("values")] public List<AiValue>? Values { get; set; }
    }

    private sealed class AiEvidence
    {
        [JsonPropertyName("sheet")] public string? Sheet { get; set; }
        [JsonPropertyName("cell")] public string? Cell { get; set; }
    }

    private sealed class AiValue
    {
        [JsonPropertyName("metric")] public string? Metric { get; set; }
        [JsonPropertyName("value")] public decimal Value { get; set; }
        [JsonPropertyName("source_sheet")] public string? SourceSheet { get; set; }
        [JsonPropertyName("source_cell")] public string? SourceCell { get; set; }
    }
}
