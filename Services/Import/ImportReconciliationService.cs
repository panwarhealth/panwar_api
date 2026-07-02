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

// Agentic suggestion engine that runs BEFORE the human gate. For flagged blocks
// with an actionable human signal, it lets Claude read the workbook via tools
// (list_tabs/read_tab/read_cells/read_comments), reconcile each block into per-send
// proposals, and cite the source cell of any value it pulls. Every proposal is
// verified deterministically and every cited value is grounded against the workbook
// snapshot here; the AI never writes. The whole run is recorded to import_ai_log.
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
                // A run that never reached submit_result (truncated by the token or
                // iteration cap) is a failure too: report it and don't cache it, so it
                // retries next time instead of freezing "no suggestions" forever.
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
        var tools = BuildTools();
        var request = new AgentRunRequest(
            SystemPrompt, userContent, tools, "submit_result",
            (name, input, c) =>
            {
                // Narrate what the AI is doing so the frontend's live status has real steps.
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

        await WriteLogAsync(clientId, file, hash, userId, run, cellsRead, verification, grounding, (int)sw.ElapsedMilliseconds, ct);

        // Completed = a real answer, not just any answer: a truncated submit_result comes
        // back as {} and must count as a failure (retry next time), never be cached.
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
            sb.AppendLine($"[{rf}] {c.Name} ({c.Template})");
        return sb.ToString();
    }

    private static IReadOnlyList<AgentTool> BuildTools() => new[]
    {
        new AgentTool("list_tabs", "List the workbook's sheet names with their row/column extents.",
            JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{}}""")!),
        new AgentTool("read_tab", "Read a whole sheet as a text grid of its non-empty cells (with A1 references).",
            JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["sheet"],"properties":{"sheet":{"type":"string"}}}""")!),
        new AgentTool("read_cells", "Read specific cells of a sheet by their A1 references.",
            JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["sheet","cells"],"properties":{"sheet":{"type":"string"},"cells":{"type":"array","items":{"type":"string"}}}}""")!),
        new AgentTool("read_comments", "Read the Excel cell-comments on a sheet (or all sheets if none given), each with its cell reference.",
            JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{"sheet":{"type":"string"}}}""")!),
        new AgentTool("submit_result", "Submit the final per-send reconciliation for every flagged block.",
            JsonNode.Parse(SubmitSchemaJson)!),
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

    // The AI is told to return ISO dates; hold it to that. Strict yyyy-MM-dd only -
    // a slash-format date is ambiguous (day-first vs month-first) and silently
    // swapping 03/06 would be worse than dropping it, since the human can see the
    // missing chip and add the date on the card.
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

    // Anti-hallucination: only keep a cited value if the cell was actually served to
    // the AI via a tool AND its snapshot value matches the claimed value.
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

    // Evidence cells (a note, a date label) get the same discipline as values, minus
    // the numeric comparison: the AI must have actually read the cell and it must
    // hold something - otherwise the citation is dropped and logged.
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
        Guid clientId, string file, string? hash, Guid? userId, AgentRunResult run,
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
                SystemPrompt = SystemPrompt,
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
            // The audit log must never break the import; a failed write is logged and swallowed.
            _logger.LogWarning(ex, "Failed to write import_ai_log for {File}", file);
        }
    }

    // Spells out exactly what the AI asked to read, for the live status line.
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

    private async Task WriteCacheAsync(Guid clientId, string? hash, List<CachedBlock> blocks, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(hash)) return;
        var exists = await _context.ImportAiCaches.AnyAsync(c => c.ClientId == clientId && c.ContentHash == hash, ct);
        if (exists) return;
        _context.ImportAiCaches.Add(new ImportAiCache
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ContentHash = hash,
            SuggestionsJson = JsonSerializer.Serialize(blocks),
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);
    }

    private sealed class CachedBlock
    {
        public string Name { get; set; } = "";
        public string Brand { get; set; } = "";
        public List<PlacementSuggestionDto> Sends { get; set; } = new();
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
