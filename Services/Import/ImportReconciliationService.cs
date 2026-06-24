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

// Suggestion engine that runs BEFORE the human gate. For flagged blocks only, it
// asks Claude to read the per-month notes/comments + any referenced tab and split
// each block into per-send proposals, mapping each to an existing placement. Every
// proposal is verified deterministically here; the AI never writes - the admin
// approves and the existing commit path does the write.
public class ImportReconciliationService : IImportReconciliationService
{
    private const string SystemPrompt =
        "You reconcile rows from a media-results spreadsheet against a client's existing placements.\n" +
        "RULES:\n" +
        "- Human notes and comments OVERRIDE the raw cell positions. If a note gives a different date/month or topic than where a number sits, trust the note.\n" +
        "- A single block can represent MULTIPLE sends across the year; each month's note names that send's topic and its real send date.\n" +
        "- For each flagged block, return one send entry per month that has a note or a value, using the note to set the topic and the correct month.\n" +
        "- Map each send to the single best matching existing placement by topic via target_ref, or 0 if none clearly fits.\n" +
        "- Be conservative: only give high confidence (>0.8) when a note or referenced tab makes the mapping clear.\n" +
        "Return JSON only, matching the provided schema.";

    private const string SchemaJson = """
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["blocks"],
      "properties": {
        "blocks": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["ref", "sends"],
            "properties": {
              "ref": { "type": "string" },
              "sends": {
                "type": "array",
                "items": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["month", "topic", "target_ref", "reason", "confidence"],
                  "properties": {
                    "month": { "type": "integer" },
                    "topic": { "type": "string" },
                    "target_ref": { "type": "integer" },
                    "reason": { "type": "string" },
                    "confidence": { "type": "number" }
                  }
                }
              }
            }
          }
        }
      }
    }
    """;

    private static readonly JsonNode Schema = JsonNode.Parse(SchemaJson)!;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDbContext _context;
    private readonly IAnthropicService _anthropic;
    private readonly ILogger<ImportReconciliationService> _logger;

    public ImportReconciliationService(AppDbContext context, IAnthropicService anthropic, ILogger<ImportReconciliationService> logger)
    {
        _context = context;
        _anthropic = anthropic;
        _logger = logger;
    }

    public bool IsEnabled => _anthropic.IsEnabled;

    public async Task<IReadOnlyDictionary<int, List<PlacementSuggestionDto>>> SuggestAsync(
        Guid clientId,
        ImportDocument doc,
        IReadOnlyList<int> flaggedIndices,
        IReadOnlyDictionary<string, string> fileHashByName,
        IReadOnlyList<ReconCandidate> candidates,
        CancellationToken ct)
    {
        var result = new Dictionary<int, List<PlacementSuggestionDto>>();
        if (!IsEnabled || flaggedIndices.Count == 0) return result;

        foreach (var grp in flaggedIndices.GroupBy(i => doc.Placements[i].Source))
        {
            var file = grp.Key;
            var indices = grp.ToList();
            fileHashByName.TryGetValue(file, out var hash);

            List<CachedBlock>? blocks = await TryReadCacheAsync(clientId, hash, ct);
            if (blocks is null)
            {
                try
                {
                    blocks = await RunFileAsync(doc, file, indices, candidates, ct);
                }
                catch (Exception ex)
                {
                    // Graceful: a failed AI call leaves the deterministic UX intact.
                    _logger.LogWarning(ex, "AI reconciliation failed for file {File}", file);
                    continue;
                }
                await WriteCacheAsync(clientId, hash, blocks, ct);
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

        return result;
    }

    private async Task<List<CachedBlock>> RunFileAsync(
        ImportDocument doc, string file, List<int> indices, IReadOnlyList<ReconCandidate> candidates, CancellationToken ct)
    {
        // Number the flagged blocks (ref A, B, ...) and the candidate placements
        // (1, 2, ...) so the model echoes a short ref rather than a GUID it could mangle.
        var blocksByRef = new Dictionary<string, ParsedPlacement>(StringComparer.OrdinalIgnoreCase);
        var flagged = indices.Select(i => doc.Placements[i]).ToList();

        var relevant = candidates
            .Where(c => flagged.Any(f =>
                string.Equals(c.Publisher, f.Publisher, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Brand, f.Brand, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (relevant.Count == 0) relevant = candidates.ToList();
        relevant = relevant.OrderBy(c => c.Name).Take(80).ToList();
        var candByRef = relevant.Select((c, idx) => (Ref: idx + 1, Cand: c)).ToDictionary(x => x.Ref, x => x.Cand);

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

        // Include any tab a note points to ("refer to the X tab").
        var noteText = string.Join(" ", flagged.SelectMany(f => f.Notes.Concat(f.MonthNotes.Values))).ToLowerInvariant();
        var refTabs = doc.RawTabs
            .Where(t => string.Equals(t.File, file, StringComparison.OrdinalIgnoreCase)
                        && t.Sheet.Length > 2 && noteText.Contains(t.Sheet.ToLowerInvariant()))
            .Take(2).ToList();
        if (refTabs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("REFERENCED TAB DATA:");
            foreach (var t in refTabs)
            {
                sb.AppendLine($"--- {t.Sheet} ---");
                sb.AppendLine(t.Text.Length > 4000 ? t.Text[..4000] : t.Text);
            }
        }

        sb.AppendLine();
        sb.AppendLine("EXISTING PLACEMENTS you may map to (use the number as target_ref, or 0 for none):");
        foreach (var (rf, c) in candByRef.OrderBy(x => x.Key))
            sb.AppendLine($"[{rf}] {c.Name} ({c.Template})");

        var raw = await _anthropic.CompleteAsync(SystemPrompt, sb.ToString(), Schema, ct);
        var parsed = JsonSerializer.Deserialize<AiResponse>(raw, Json);

        var output = new List<CachedBlock>();
        foreach (var b in parsed?.Blocks ?? new())
        {
            if (b.Ref is null || !blocksByRef.TryGetValue(b.Ref, out var pp)) continue;
            var sends = new List<PlacementSuggestionDto>();
            foreach (var s in b.Sends ?? new())
            {
                // Deterministic verification: drop anything not grounded in real data.
                if (s.Month is < 1 or > 12) continue;
                if (string.IsNullOrWhiteSpace(s.Topic)) continue;
                Guid? targetId = null;
                string? targetName = null;
                if (s.TargetRef > 0 && candByRef.TryGetValue(s.TargetRef, out var cand))
                {
                    targetId = cand.Id;
                    targetName = cand.Name;
                }
                var confidence = Math.Clamp(s.Confidence, 0, 1);
                sends.Add(new PlacementSuggestionDto(s.Month, s.Topic.Trim(), targetId, targetName, s.Reason?.Trim() ?? "", confidence));
            }
            output.Add(new CachedBlock { Name = pp.Name, Brand = pp.Brand, Sends = sends });
        }
        return output;
    }

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
    }
}
