using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.CloudflareR2;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;
using Panwar.Api.Services.Write;

namespace Panwar.Api.Services.Import;

public sealed class ImportConflictException : Exception
{
    public ImportConflictException(string message) : base(message) { }
}

public class ImportService : IImportService
{
    private readonly AppDbContext _context;
    private readonly ICloudflareR2Service _r2;
    private readonly IPlacementWriteService _placementWrite;
    private readonly IEducationWriteService _educationWrite;
    private readonly IImportReconciliationService _recon;
    private readonly IImportProgress _progress;
    private readonly ILogger<ImportService> _logger;

    public ImportService(
        AppDbContext context,
        ICloudflareR2Service r2,
        IPlacementWriteService placementWrite,
        IEducationWriteService educationWrite,
        IImportReconciliationService recon,
        IImportProgress progress,
        ILogger<ImportService> logger)
    {
        _context = context;
        _r2 = r2;
        _placementWrite = placementWrite;
        _educationWrite = educationWrite;
        _recon = recon;
        _progress = progress;
        _logger = logger;
    }

    public async Task<ImportPreviewDto> BuildPreviewAsync(string clientSlug, ImportPreviewRequest request, Guid? userId, CancellationToken ct)
    {
        var jobId = request.JobId ?? Guid.Empty;
        var d = await BuildDiffAsync(clientSlug, request, ct);
        var placementDiffs = d.PlacementDiffs;
        var aiFailed = false;

        // Run AI reconciliation inline (cache-first, live on a miss) so the returned
        // preview already carries every suggestion - the frontend renders once, fully done.
        if (_recon.IsEnabled)
        {
            var (flagged, cands, hashByName) = ComputeReconInputs(d);
            if (flagged.Count > 0)
            {
                var recon = await _recon.SuggestAsync(d.Client.Id, d.Doc, flagged, hashByName, cands, userId, allowLive: true, jobId, ct);
                aiFailed = recon.FailedFiles.Count > 0;
                if (recon.Suggestions.Count > 0)
                    placementDiffs = placementDiffs
                        .Select((x, i) => recon.Suggestions.TryGetValue(i, out var s)
                            ? x with
                            {
                                // Cited views come from the RAW values so the highlights show
                                // everything the AI read; the send lines get only the values
                                // that can actually be saved.
                                Suggestions = FillTargetsByTopic(StorableSuggestionValues(s, d.ValidKeysByTemplateCode.GetValueOrDefault(x.Template)), x, d.Placements),
                                SourceViews = MergeSourceViews(x.SourceViews, SourceViewBuilder.BuildCitedSourceViews(d.Doc, x.Source, s)),
                            }
                            : x)
                        .ToList();
            }
        }

        _progress.Report(jobId, "Putting the preview together...");
        return new ImportPreviewDto(request.Year, d.Sources, d.Headline, placementDiffs, d.EducationDiffs, d.GlobalWarnings, _recon.IsEnabled, aiFailed);
    }

    // Deterministic backstop for the AI's mapping nerves: when a send has no target
    // (the AI said "nothing clearly matches" or its confidence fell below the floor),
    // rank same-template placements by name similarity against "{block} - {topic}"
    // (the house naming convention) and pre-select the winner. Runs post-cache so it
    // also repairs already-cached answers.
    private static List<PlacementSuggestionDto> FillTargetsByTopic(
        List<PlacementSuggestionDto> sends, PlacementDiffDto x, List<Placement> placements)
        => sends.Select(s =>
        {
            if (s.TargetPlacementId is not null || string.IsNullOrWhiteSpace(s.TopicLabel)) return s;
            var proposed = Norm($"{x.ParsedName} - {s.TopicLabel}");
            var topic = Norm(s.TopicLabel);
            var best = placements
                .Where(pl => pl.Template.Code.ToString().Equals(x.Template, StringComparison.OrdinalIgnoreCase))
                .Select(pl =>
                {
                    var name = Norm(pl.Name);
                    var score = NameSimilarity(proposed, name)
                        + (topic.Length >= 4 && name.Contains(topic) ? 0.15 : 0)
                        + (!string.IsNullOrEmpty(x.Publisher) && pl.Publisher.Slug == x.Publisher ? 0.05 : 0);
                    return (pl, score);
                })
                .OrderByDescending(t => t.score)
                .FirstOrDefault();
            return best.pl is not null && best.score >= 0.6
                ? s with { TargetPlacementId = best.pl.Id, TargetName = best.pl.Name }
                : s;
        }).ToList();

    // Character-bigram Dice similarity on normalized names: "ap solus edms - msk pain"
    // vs "ap solus edm - msk pain" ~0.96, vs a different card sharing only the topic
    // well under the 0.6 pre-select bar.
    private static double NameSimilarity(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2) return 0;
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < a.Length - 1; i++)
        {
            var g = a.Substring(i, 2);
            counts[g] = counts.GetValueOrDefault(g) + 1;
        }
        int inter = 0;
        for (int i = 0; i < b.Length - 1; i++)
        {
            var g = b.Substring(i, 2);
            if (counts.GetValueOrDefault(g) > 0) { counts[g]--; inter++; }
        }
        return 2.0 * inter / (a.Length - 1 + b.Length - 1);
    }

    // The AI's cited views carry highlights, so they supersede a plain referenced-tab
    // view of the same sheet - keeping both would show the tab twice. The block's own
    // view (always first) stays regardless.
    private static List<SourceViewDto> MergeSourceViews(IReadOnlyList<SourceViewDto> existing, IReadOnlyList<SourceViewDto> cited)
    {
        if (cited.Count == 0) return existing.ToList();
        var citedSheets = cited.Select(v => v.Sheet).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return existing.Take(1)
            .Concat(existing.Skip(1).Where(v => !citedSheets.Contains(v.Sheet)))
            .Concat(cited)
            .ToList();
    }

    // Suggestion values arrive with the AI's free-text metric labels ("Total Sends"),
    // possibly from an older cached run - normalize each to a real metric key and keep
    // only ones the block's template can store, so every number shown on a send line
    // is one the commit will actually accept.
    private static List<PlacementSuggestionDto> StorableSuggestionValues(List<PlacementSuggestionDto> sends, HashSet<string>? validKeys)
        => sends.Select(s =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var values = new List<SuggestionValueDto>();
            foreach (var v in s.Values)
            {
                var key = Catalog.NormalizeMetric(v.Metric ?? "");
                if (key.Length == 0 || validKeys is null || !validKeys.Contains(key) || !seen.Add(key)) continue;
                values.Add(v with { Metric = key });
            }
            return s with
            {
                Values = values,
                SendDates = s.SendDates ?? Array.Empty<string>(),
                Evidence = s.Evidence ?? Array.Empty<SuggestionCellRefDto>(),
            };
        }).ToList();

    // AI fires only on flagged rows that carry an actionable human signal (a note or
    // Excel comment) - the cheap deterministic cases stay off the AI entirely. Notes
    // already includes every non-trivial MonthNotes entry (PlacementBlocks.AddNote
    // filters both through the same trivial-text check), and it's the only signal the
    // frontend can see too - so this must stay the single source of truth for both
    // sides, or the frontend's reconcile trigger and "thinking" badge silently diverge
    // from what the backend actually does.
    private static (List<int> Flagged, List<ReconCandidate> Candidates, Dictionary<string, string> HashByName) ComputeReconInputs(DiffResult d)
    {
        var flagged = d.PlacementDiffs
            .Select((x, i) => (x, i))
            .Where(t => t.x.NeedsReview && d.Doc.Placements[t.i].Notes.Count > 0)
            .Select(t => t.i)
            .ToList();
        var candidates = d.Placements
            .Select(p => new ReconCandidate(p.Id, p.Name, p.Template.Code.ToString(), p.Brand.Name, p.Publisher.Slug))
            .ToList();
        var hashByName = d.FileData
            .GroupBy(fd => fd.FileName)
            .ToDictionary(g => g.Key, g => g.First().Hash, StringComparer.Ordinal);
        return (flagged, candidates, hashByName);
    }

    private async Task<DiffResult> BuildDiffAsync(string clientSlug, ImportPreviewRequest request, CancellationToken ct)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct)
            ?? throw new ImportConflictException($"Client '{clientSlug}' not found");

        var parser = ImportParser.Default();
        var doc = new ImportDocument { ClientSlug = clientSlug, Year = request.Year };
        // Track as ordered list so index aligns with doc.Sources (one entry per ParseInto call).
        var fileData = new List<(string FileName, string ObjectKey, string Hash)>();

        var jobId = request.JobId ?? Guid.Empty;
        foreach (var f in request.Files)
        {
            _progress.Report(jobId, $"Reading {f.FileName}...");
            var bytes = await _r2.DownloadAsync(f.ObjectKey, ct);
            fileData.Add((f.FileName, f.ObjectKey, Sha256Hex(bytes)));
            using var wb = WorkbookLoader.Load(bytes);
            parser.ParseInto(wb, new ParseContext { ClientSlug = clientSlug, Year = request.Year, FileName = f.FileName }, doc);
        }

        _progress.Report(jobId, "Comparing your file against what's already saved...");

        var templates = await _context.MetricTemplates.AsNoTracking().ToListAsync(ct);
        var templateIdByCode = templates.ToDictionary(t => t.Code.ToString(), t => t.Id, StringComparer.OrdinalIgnoreCase);

        var validKeysByTemplate = (await _context.MetricFields.AsNoTracking()
                .Where(mf => !mf.IsCalculated).ToListAsync(ct))
            .GroupBy(mf => mf.TemplateId)
            .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(mf => mf.Key), StringComparer.OrdinalIgnoreCase));

        var placements = await _context.Placements.AsNoTracking()
            .Include(p => p.Template).Include(p => p.Brand).Include(p => p.Publisher).Include(p => p.Audience)
            .Include(p => p.Actuals)
            .Where(p => p.Brand.ClientId == client.Id)
            .ToListAsync(ct);

        var eduAssets = await _context.EducationAssets.AsNoTracking()
            .Include(a => a.Values).Include(a => a.Page)
            .Where(a => a.Page.ClientId == client.Id)
            .ToListAsync(ct);

        var placementsByName = placements
            .GroupBy(p => Norm(p.Name))
            .ToDictionary(g => g.Key, g => g.ToList());
        // Mappings the admin made on previous imports: block name -> placement.
        var placementById = placements.ToDictionary(p => p.Id);
        var aliasByName = (await _context.ImportNameAliases.AsNoTracking()
                .Where(a => a.ClientId == client.Id).ToListAsync(ct))
            .GroupBy(a => a.SourceName)
            .ToDictionary(g => g.Key, g => g.First().PlacementId, StringComparer.Ordinal);
        var assetsByTitle = eduAssets
            .GroupBy(a => Norm(a.Title))
            .ToDictionary(g => g.Key, g => g.ToList());

        var blockLocator = SourceViewBuilder.BuildBlockLocator(doc);

        var validKeysByTemplateCode = templates.ToDictionary(
            t => t.Code.ToString(),
            t => validKeysByTemplate.GetValueOrDefault(t.Id) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var placementDiffs = new List<PlacementDiffDto>();
        foreach (var pp in doc.Placements)
        {
            var norm = Norm(pp.Name);
            placementsByName.TryGetValue(norm, out var hits);

            string matchStatus;
            Placement? matched = null;
            var matchedByMemory = false;
            var candidates = new List<PlacementCandidateDto>();

            // A mapping the admin made before beats name matching - it's an explicit
            // human decision ("this block goes there"), remembered across imports.
            if (aliasByName.TryGetValue(norm, out var aliasPid) && placementById.TryGetValue(aliasPid, out var aliasHit))
            {
                matched = aliasHit;
                matchStatus = "matched";
                matchedByMemory = true;
            }
            else if (hits is { Count: 1 })
            {
                matched = hits[0];
                matchStatus = "matched";
            }
            else if (hits is { Count: > 1 })
            {
                matchStatus = "ambiguous";
                candidates = hits.Select(h => new PlacementCandidateDto(h.Id, h.Name, h.Template.Code.ToString())).ToList();
            }
            else
            {
                matchStatus = "unmatched";
                candidates = placements
                    .Where(p => string.Equals(p.Publisher.Slug, pp.Publisher, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(p.Brand.Name, pp.Brand, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p.Name)
                    .Select(p => new PlacementCandidateDto(p.Id, p.Name, p.Template.Code.ToString()))
                    .ToList();
            }

            Guid? templateId = matched?.TemplateId
                ?? (templateIdByCode.TryGetValue(pp.Template, out var tid) ? tid : null);
            var validKeys = templateId is Guid g && validKeysByTemplate.TryGetValue(g, out var vk)
                ? vk : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var rows = new List<ActualDiffDto>();
            foreach (var a in pp.Actuals)
            {
                var key = a.Metric;
                string outcome;
                decimal? old = null;

                if (!validKeys.Contains(key))
                {
                    outcome = "invalid";
                }
                else if (matched is not null)
                {
                    var existing = matched.Actuals.FirstOrDefault(x =>
                        x.Year == request.Year && x.Month == a.Month &&
                        string.Equals(x.MetricKey, key, StringComparison.OrdinalIgnoreCase));
                    if (existing is null) outcome = "new";
                    else { old = existing.Value; outcome = Math.Abs(existing.Value - a.Value) <= 0.5m ? "match" : "change"; }
                }
                else
                {
                    outcome = "new";
                }

                rows.Add(new ActualDiffDto(key, a.Month, a.Value, old, outcome, a.Note));
            }

            // Written for the admin reviewing the card - plain sentences, no jargon.
            var reasons = new List<string>();
            if (matchStatus == "unmatched") reasons.Add("The name doesn't match anything saved yet");
            else if (matchStatus == "ambiguous") reasons.Add("The name matches more than one saved placement");
            if (rows.Any(r => r.Outcome == "invalid")) reasons.Add("Some of its numbers have nowhere to be saved yet (shown in red below)");
            if (rows.Any(r => r.Outcome == "change")) reasons.Add("Some numbers differ from what's already saved (shown in amber below, old value underneath)");
            if (pp.Notes.Count > 0) reasons.Add("There's a note in the file that may change where things go");
            if (doc.Warnings.Any(w => w.Message.Contains($"'{pp.Name}'") && w.Message.Contains("months sum to")))
                reasons.Add("The monthly numbers don't add up to the file's own total - double-check them");
            var needsReview = reasons.Count > 0;

            var blockViews = SourceViewBuilder.BuildBlockSourceView(blockLocator, doc, pp.Source, pp.Name);
            var referencedViews = SourceViewBuilder.BuildReferencedTabViews(doc, pp.Source, pp.Notes, blockViews.FirstOrDefault()?.Sheet);
            placementDiffs.Add(new PlacementDiffDto(
                pp.Source, pp.Name, pp.Brand, pp.Audience, pp.Publisher, pp.Template, pp.Objective,
                matchStatus, matched?.Id, matched?.Name, matchedByMemory, candidates, rows,
                pp.Notes, needsReview, reasons, Array.Empty<PlacementSuggestionDto>(),
                blockViews.Concat(referencedViews).ToList()));
        }

        var educationDiffs = new List<EducationDiffDto>();
        foreach (var ea in doc.Education)
        {
            var norm = Norm(ea.Title);
            assetsByTitle.TryGetValue(norm, out var hits);
            if (hits is { Count: > 1 })
            {
                var narrowed = hits.Where(h => string.Equals(Norm(h.Brand ?? ""), Norm(ea.Brand), StringComparison.Ordinal)).ToList();
                if (narrowed.Count == 1) hits = narrowed;
            }

            string matchStatus;
            EducationAsset? matched = null;
            var candidates = new List<EducationCandidateDto>();

            if (hits is { Count: 1 }) { matched = hits[0]; matchStatus = "matched"; }
            else if (hits is { Count: > 1 })
            {
                matchStatus = "ambiguous";
                candidates = hits.Select(h => new EducationCandidateDto(h.Id, h.EducationPageId, h.Page.Name, h.Title)).ToList();
            }
            else
            {
                matchStatus = "unmatched";
                candidates = eduAssets
                    .Where(a => string.Equals(Norm(a.Brand ?? ""), Norm(ea.Brand), StringComparison.Ordinal))
                    .OrderBy(a => a.Title)
                    .Select(a => new EducationCandidateDto(a.Id, a.EducationPageId, a.Page.Name, a.Title))
                    .ToList();
            }

            var rows = new List<EducationValueDiffDto>();
            foreach (var v in ea.Values)
            {
                string outcome;
                decimal? old = null;
                if (matched is not null)
                {
                    var existing = matched.Values.FirstOrDefault(x =>
                        string.Equals(x.Status, v.Status, StringComparison.OrdinalIgnoreCase) &&
                        x.Year == v.Year && x.Month == v.Month);
                    if (existing is null) outcome = "new";
                    else { old = existing.Value; outcome = Math.Abs(existing.Value - v.Value) <= 0.5m ? "match" : "change"; }
                }
                else
                {
                    outcome = "new";
                }
                rows.Add(new EducationValueDiffDto(v.Status, v.Year, v.Month, v.Value, old, outcome));
            }

            educationDiffs.Add(new EducationDiffDto(
                ea.Source, ea.Group, ea.Brand, ea.Type, ea.Title, ea.Author, NormalizeDateToIso(ea.Expiry),
                matchStatus, matched?.Id, matched?.EducationPageId, matched?.Page.Name, candidates, rows));
        }

        var allHashes = fileData.Select(fd => fd.Hash).Distinct().ToList();
        var priorRuns = await _context.ImportRuns.AsNoTracking()
            .Where(r => r.ClientId == client.Id && allHashes.Contains(r.ContentHash))
            .ToListAsync(ct);
        var latestRunByHash = priorRuns
            .GroupBy(r => r.ContentHash)
            .ToDictionary(grp => grp.Key, grp => grp.OrderByDescending(r => r.ImportedAt).First());
        var userIds = latestRunByHash.Values.Where(r => r.ImportedByUserId is not null).Select(r => r.ImportedByUserId!.Value).Distinct().ToList();
        var userById = userIds.Count == 0
            ? new Dictionary<Guid, AppUser>()
            : (await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync(ct)).ToDictionary(u => u.Id);

        var sources = new List<ImportSourceDto>();
        for (int i = 0; i < doc.Sources.Count; i++)
        {
            var s = doc.Sources[i];
            var (_, objKey, hash) = i < fileData.Count ? fileData[i] : ("", "", "");
            AlreadyImportedDto? already = null;
            if (!string.IsNullOrEmpty(hash) && latestRunByHash.TryGetValue(hash, out var run))
            {
                string? by = run.ImportedByUserId is Guid uid && userById.TryGetValue(uid, out var u) ? (u.Name ?? u.Email) : null;
                already = new AlreadyImportedDto(run.ImportedAt, by);
            }
            var srcWarnings = doc.Warnings.Where(w => w.Source == s.File).Select(w => w.Message).ToList();
            sources.Add(new ImportSourceDto(s.File, objKey, s.FormatId, s.Match, hash, already, srcWarnings));
        }

        var fileNames = doc.Sources.Select(s => s.File).ToHashSet(StringComparer.Ordinal);
        var globalWarnings = doc.Warnings.Where(w => !fileNames.Contains(w.Source)).Select(w => w.Message).ToList();

        var allRowsOutcomes = placementDiffs.SelectMany(p => p.Rows.Select(r => r.Outcome))
            .Concat(educationDiffs.SelectMany(e => e.Rows.Select(r => r.Outcome)))
            .ToList();
        var headline = new ImportHeadlineDto(
            Match: allRowsOutcomes.Count(o => o == "match"),
            Change: allRowsOutcomes.Count(o => o == "change"),
            New: allRowsOutcomes.Count(o => o == "new"),
            Invalid: allRowsOutcomes.Count(o => o == "invalid"),
            UnmatchedPlacements: placementDiffs.Count(p => p.MatchStatus != "matched"),
            TotalValues: allRowsOutcomes.Count);

        return new DiffResult(client, doc, placementDiffs, educationDiffs, placements, fileData, sources, headline, globalWarnings, validKeysByTemplateCode);
    }

    private sealed record DiffResult(
        Client Client,
        ImportDocument Doc,
        List<PlacementDiffDto> PlacementDiffs,
        List<EducationDiffDto> EducationDiffs,
        List<Placement> Placements,
        List<(string FileName, string ObjectKey, string Hash)> FileData,
        List<ImportSourceDto> Sources,
        ImportHeadlineDto Headline,
        List<string> GlobalWarnings,
        Dictionary<string, HashSet<string>> ValidKeysByTemplateCode);

    public async Task<ImportCommitResultDto> CommitAsync(string clientSlug, ImportCommitRequest request, Guid? userId, CancellationToken ct)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct)
            ?? throw new ImportConflictException($"Client '{clientSlug}' not found");

        var fileHashes = new List<(ImportFileRef File, string Hash)>();
        foreach (var f in request.Files)
        {
            var bytes = await _r2.DownloadAsync(f.ObjectKey, ct);
            fileHashes.Add((f, Sha256Hex(bytes)));
        }
        var hashes = fileHashes.Select(x => x.Hash).Distinct().ToList();
        var priorRuns = await _context.ImportRuns.AsNoTracking()
            .Where(r => r.ClientId == client.Id && hashes.Contains(r.ContentHash))
            .ToListAsync(ct);
        if (priorRuns.Count > 0 && !request.Acknowledged)
            throw new ImportConflictException("One or more of these files was already imported. Tick the acknowledge box to import again.");

        var validKeysByTemplate = (await _context.MetricFields.AsNoTracking()
                .Where(mf => !mf.IsCalculated).ToListAsync(ct))
            .GroupBy(mf => mf.TemplateId)
            .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(mf => mf.Key), StringComparer.OrdinalIgnoreCase));

        await using var tx = await _context.Database.BeginTransactionAsync(ct);

        int placementsWritten = 0, valuesWritten = 0, eduAssetsWritten = 0, eduValuesWritten = 0;
        var valuesWrittenBySource = new Dictionary<string, int>(StringComparer.Ordinal);

        // Batch-load all matched placements in one query rather than N round-trips.
        var matchedIds = request.Placements
            .Where(cp => cp.PlacementId is not null)
            .Select(cp => cp.PlacementId!.Value)
            .Distinct()
            .ToList();
        var loadedPlacements = matchedIds.Count == 0
            ? new Dictionary<Guid, Placement>()
            : (await _context.Placements
                .Include(p => p.Actuals)
                .Where(p => matchedIds.Contains(p.Id) && p.Brand.ClientId == client.Id)
                .ToListAsync(ct))
              .ToDictionary(p => p.Id);

        foreach (var cp in request.Placements)
        {
            if (cp.Actuals.Count == 0) continue;
            if (string.IsNullOrWhiteSpace(cp.Source))
                throw new ImportConflictException("Each placement to import must specify its source file");
            Placement placement;
            if (cp.PlacementId is Guid pid)
            {
                if (!loadedPlacements.TryGetValue(pid, out placement!))
                    throw new ImportConflictException($"Placement {pid} not found for this client");
            }
            else if (cp.NewPlacement is not null)
            {
                placement = await CreatePlacementAsync(client.Id, cp.NewPlacement, cp.Actuals, request.Year, userId, ct);
            }
            else
            {
                throw new ImportConflictException("A placement to import was neither matched nor marked for creation");
            }

            var valid = validKeysByTemplate.GetValueOrDefault(placement.TemplateId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in cp.Actuals)
            {
                if (a.Month is < 1 or > 12) throw new ImportConflictException($"Invalid month {a.Month} for placement {placement.Name}");
                if (a.Year != request.Year) throw new ImportConflictException($"Actual year {a.Year} does not match import year {request.Year} for placement {placement.Name}");
                var key = (a.MetricKey ?? "").Trim().ToLowerInvariant();
                if (!valid.Contains(key))
                    throw new ImportConflictException($"Metric '{a.MetricKey}' is not part of {placement.Name}'s template");
            }

            // Store send dates whenever the note carried them - true eDMs and the
            // "eDM MREC/Leaderboard" banners (DigitalDisplay template) both have them.
            ApplySendDates(placement, cp.SendDates);

            var n = _placementWrite.UpsertActuals(placement,
                cp.Actuals.Select(a => new ActualWrite(a.Year, a.Month, a.MetricKey, a.Value, a.Note)));
            valuesWritten += n;
            valuesWrittenBySource[cp.Source] = valuesWrittenBySource.GetValueOrDefault(cp.Source) + n;
            placementsWritten++;
        }

        foreach (var ce in request.Education)
        {
            var expiry = ParseIsoDate(ce.Expiry);
            if (ce.Values.Count == 0 && expiry is null) continue;

            EducationAsset asset;
            if (ce.NewAsset is { } na)
            {
                if (string.IsNullOrWhiteSpace(na.Title))
                    throw new ImportConflictException("A new course needs a title");
                var pageOk = await _context.EducationPages.AnyAsync(p => p.Id == na.PageId && p.ClientId == client.Id, ct);
                if (!pageOk) throw new ImportConflictException("Education page not found for this client");
                var maxOrder = await _context.EducationAssets.Where(a => a.EducationPageId == na.PageId)
                    .Select(a => (int?)a.SortOrder).MaxAsync(ct) ?? -1;
                asset = new EducationAsset
                {
                    Id = Guid.NewGuid(),
                    EducationPageId = na.PageId,
                    GroupLabel = !string.IsNullOrWhiteSpace(na.Group) ? na.Group.Trim()
                        : !string.IsNullOrWhiteSpace(na.Brand) ? na.Brand.Trim() : "Education",
                    Brand = string.IsNullOrWhiteSpace(na.Brand) ? null : na.Brand.Trim(),
                    Type = string.IsNullOrWhiteSpace(na.Type) ? null : na.Type.Trim(),
                    Title = na.Title.Trim(),
                    Author = string.IsNullOrWhiteSpace(na.Author) ? null : na.Author.Trim(),
                    SortOrder = maxOrder + 1,
                };
                _context.EducationAssets.Add(asset);
            }
            else if (ce.AssetId is Guid assetId)
            {
                asset = await _context.EducationAssets
                    .Include(a => a.Values)
                    .FirstOrDefaultAsync(a => a.Id == assetId && a.Page.ClientId == client.Id, ct)
                    ?? throw new ImportConflictException($"Education asset {assetId} not found for this client");
            }
            else
            {
                throw new ImportConflictException("An education row to import was neither matched nor marked for creation");
            }

            if (expiry is not null) asset.Expiry = expiry;

            var n = _educationWrite.UpsertAssetValues(asset,
                ce.Values.Select(v => new EducationValueWrite(v.Status, v.Year, v.Month, v.Value)));
            eduValuesWritten += n;
            eduAssetsWritten++;
        }

        // Remember every "this block name goes to that placement" decision, so next
        // month's import of the same workbook auto-matches instead of asking again.
        // Skipped when the names already match (name matching handles those), and for
        // AI per-send writes (one block fans out to many placements - no single alias).
        var aliasPairs = request.Placements
            .Where(cp => cp.PlacementId is not null && !string.IsNullOrWhiteSpace(cp.ParsedName) && cp.Actuals.Count > 0)
            .Select(cp => (Norm: Norm(cp.ParsedName!), Pid: cp.PlacementId!.Value))
            .Where(x => !(loadedPlacements.TryGetValue(x.Pid, out var lp) && Norm(lp.Name) == x.Norm))
            .GroupBy(x => x.Norm)
            .Select(g => g.First())
            .ToList();
        if (aliasPairs.Count > 0)
        {
            var aliasNorms = aliasPairs.Select(a => a.Norm).ToList();
            var existingAliases = await _context.ImportNameAliases
                .Where(a => a.ClientId == client.Id && aliasNorms.Contains(a.SourceName))
                .ToListAsync(ct);
            foreach (var (aliasNorm, pid) in aliasPairs)
            {
                var row = existingAliases.FirstOrDefault(a => a.SourceName == aliasNorm);
                if (row is null)
                    _context.ImportNameAliases.Add(new ImportNameAlias
                    {
                        Id = Guid.NewGuid(),
                        ClientId = client.Id,
                        SourceName = aliasNorm,
                        PlacementId = pid,
                        CreatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow,
                    });
                else row.PlacementId = pid;
            }
        }

        foreach (var (file, hash) in fileHashes)
        {
            _context.ImportRuns.Add(new ImportRun
            {
                Id = Guid.NewGuid(),
                ClientId = client.Id,
                Year = request.Year,
                FileName = file.FileName,
                ContentHash = hash,
                FormatId = string.IsNullOrWhiteSpace(file.FormatId) ? "unknown" : file.FormatId!,
                PlacementsWritten = placementsWritten,
                ValuesWritten = valuesWritten + eduValuesWritten,
                ImportedByUserId = userId,
                ImportedAt = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Best-effort audit stamp - the commit has already succeeded above, so a
        // failure here must never surface as a commit failure to the caller.
        try
        {
            await StampAiOutcomeAsync(client.Id, request, fileHashes, loadedPlacements, valuesWrittenBySource, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stamp AI outcome for client {ClientId}", client.Id);
        }

        return new ImportCommitResultDto(placementsWritten, valuesWritten, eduAssetsWritten, eduValuesWritten);
    }

    // Records what the commit actually wrote against each file's AI log, so a reviewer
    // can compare the AI's proposals with the human-approved outcome after the fact.
    // Scoped per source file (CommitPlacementDto.Source) - a multi-file commit must
    // stamp each file's log with only ITS OWN placements/values, not the whole commit's.
    private async Task StampAiOutcomeAsync(
        Guid clientId, ImportCommitRequest request, List<(ImportFileRef File, string Hash)> fileHashes,
        Dictionary<Guid, Placement> loadedPlacements, Dictionary<string, int> valuesWrittenBySource, Guid? userId, CancellationToken ct)
    {
        var hashes = fileHashes.Select(x => x.Hash).Distinct().ToList();
        var logs = await _context.ImportAiLogs
            .Where(l => l.ClientId == clientId && hashes.Contains(l.ContentHash) && l.OutcomeJson == null)
            .ToListAsync(ct);
        if (logs.Count == 0) return;

        var targetsBySource = request.Placements
            .Where(cp => cp.Actuals.Count > 0 && !string.IsNullOrEmpty(cp.Source))
            .GroupBy(cp => cp.Source, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(cp => new
                {
                    placementId = cp.PlacementId,
                    name = cp.PlacementId is Guid pid && loadedPlacements.TryGetValue(pid, out var lp) ? lp.Name : cp.NewPlacement?.Name,
                    created = cp.PlacementId is null,
                    months = cp.Actuals.Select(a => a.Month).Distinct().OrderBy(m => m).ToArray(),
                    metrics = cp.Actuals.Select(a => a.MetricKey).Distinct().ToArray(),
                }).ToList<object>(),
                StringComparer.Ordinal);

        var now = DateTime.UtcNow;
        foreach (var l in logs)
        {
            var targets = targetsBySource.TryGetValue(l.FileName, out var t) ? t : new List<object>();
            var valuesWritten = valuesWrittenBySource.GetValueOrDefault(l.FileName);
            l.OutcomeJson = JsonSerializer.Serialize(new { committedAt = now, userId, valuesWritten, targets });
        }
        await _context.SaveChangesAsync(ct);
    }

    // Strict ISO only: the parser and the card's date inputs both emit yyyy-MM-dd,
    // and anything else (a slash date) is ambiguous - drop it rather than guess.
    private static string? NormalizeDateToIso(string? raw)
        => ParseIsoDate(raw)?.ToString("yyyy-MM-dd");

    private static DateOnly? ParseIsoDate(string? raw)
        => DateOnly.TryParseExact((raw ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    // Merge the approved send dates onto an eDM placement (union with anything already
    // stored, so re-imports and multiple send lines accumulate rather than clobber), and
    // keep StartDate as the earliest send so year/range queries still work.
    private static void ApplySendDates(Placement placement, IReadOnlyList<string>? dates)
    {
        if (dates is null || dates.Count == 0) return;
        var merged = new SortedSet<DateOnly>(placement.SendDates);
        foreach (var s in dates)
            if (ParseIsoDate(s) is { } d)
                merged.Add(d);
        if (merged.Count == 0) return;
        placement.SendDates = merged.ToArray();
        placement.StartDate = merged.Min;
    }

    private async Task<Placement> CreatePlacementAsync(
        Guid clientId, NewPlacementSpec spec, IReadOnlyList<CommitActualDto> actuals, int year, Guid? userId, CancellationToken ct)
    {
        var brandName = spec.Brand.Trim().ToLowerInvariant();
        // Match in memory (a client has only a handful of brands): exact first, then
        // containment for compound names like "Nurofen Adult" -> "Nurofen".
        var clientBrands = await _context.Brands.Where(b => b.ClientId == clientId).ToListAsync(ct);
        var brand = clientBrands.FirstOrDefault(b => string.Equals(b.Name, spec.Brand.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? clientBrands.FirstOrDefault(b =>
            {
                var n = b.Name.ToLowerInvariant();
                return brandName.Contains(n) || n.Contains(brandName);
            })
            ?? throw new ImportConflictException($"Brand '{spec.Brand}' not found for this client - add it under Brands, then re-import");
        var publisher = await _context.Publishers.FirstOrDefaultAsync(p => p.Slug == spec.Publisher, ct)
            ?? throw new ImportConflictException($"Publisher '{spec.Publisher}' not found - map '{spec.Name}' to an existing placement instead");
        if (string.IsNullOrWhiteSpace(spec.Audience))
            throw new ImportConflictException($"'{spec.Name}' has no audience - pick one in the preview before creating it");
        var audience = await _context.Audiences.FirstOrDefaultAsync(a => a.ClientId == clientId && a.Slug == spec.Audience, ct)
            ?? throw new ImportConflictException($"Audience '{spec.Audience}' not found for this client");
        if (!Enum.TryParse<MetricTemplateCode>(spec.Template, ignoreCase: true, out var code))
            throw new ImportConflictException($"Unknown template '{spec.Template}' for '{spec.Name}'");
        var template = await _context.MetricTemplates.FirstOrDefaultAsync(t => t.Code == code, ct)
            ?? throw new ImportConflictException($"Template '{spec.Template}' not found");

        Enum.TryParse<PlacementObjective>(spec.Objective, ignoreCase: true, out var objective);

        var months = actuals.Select(a => a.Month).Distinct().OrderBy(m => m).ToList();
        DateOnly? start = null, end = null;
        int[] live = Array.Empty<int>();
        if (months.Count > 0)
        {
            if (code == MetricTemplateCode.Edm) start = new DateOnly(year, months[0], 1);
            else if (code == MetricTemplateCode.Education) { start = new DateOnly(year, months[0], 1); end = new DateOnly(year, months[^1], 1); }
            else live = months.ToArray();
        }

        var now = DateTime.UtcNow;
        var placement = new Placement
        {
            Id = Guid.NewGuid(),
            BrandId = brand.Id,
            AudienceId = audience.Id,
            PublisherId = publisher.Id,
            TemplateId = template.Id,
            Year = year,
            Name = spec.Name.Trim(),
            Objective = objective,
            StartDate = start,
            EndDate = end,
            LiveMonths = live,
            MediaCost = 0,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
            UpdatedBy = userId,
        };
        _context.Placements.Add(placement);

        var targetYear = start?.Year ?? year;
        var seededKpis = await _context.ClientPublisherBaselines
            .Where(b => b.ClientId == clientId
                     && b.PublisherId == publisher.Id
                     && b.TemplateId == template.Id
                     && b.Year == targetYear)
            .Select(b => new PlacementKpi
            {
                Id = Guid.NewGuid(),
                PlacementId = placement.Id,
                MetricKey = b.MetricKey,
                TargetValue = b.Value,
            })
            .ToListAsync(ct);
        _context.PlacementKpis.AddRange(seededKpis);

        return placement;
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Norm(string s) => Spreadsheet.NormalizeName(s);
}
