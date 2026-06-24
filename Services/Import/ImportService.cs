using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
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
    private static readonly Regex NormWhitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly AppDbContext _context;
    private readonly ICloudflareR2Service _r2;
    private readonly IPlacementWriteService _placementWrite;
    private readonly IEducationWriteService _educationWrite;
    private readonly IImportReconciliationService _recon;

    public ImportService(
        AppDbContext context,
        ICloudflareR2Service r2,
        IPlacementWriteService placementWrite,
        IEducationWriteService educationWrite,
        IImportReconciliationService recon)
    {
        _context = context;
        _r2 = r2;
        _placementWrite = placementWrite;
        _educationWrite = educationWrite;
        _recon = recon;
    }

    public async Task<ImportPreviewDto> BuildPreviewAsync(string clientSlug, ImportPreviewRequest request, CancellationToken ct)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Slug == clientSlug, ct)
            ?? throw new ImportConflictException($"Client '{clientSlug}' not found");

        var parser = ImportParser.Default();
        var doc = new ImportDocument { ClientSlug = clientSlug, Year = request.Year };
        // Track as ordered list so index aligns with doc.Sources (one entry per ParseInto call).
        var fileData = new List<(string FileName, string ObjectKey, string Hash)>();

        foreach (var f in request.Files)
        {
            var bytes = await _r2.DownloadAsync(f.ObjectKey, ct);
            fileData.Add((f.FileName, f.ObjectKey, Sha256Hex(bytes)));
            using var wb = WorkbookLoader.Load(bytes);
            parser.ParseInto(wb, new ParseContext { ClientSlug = clientSlug, Year = request.Year, FileName = f.FileName }, doc);
        }

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
        var assetsByTitle = eduAssets
            .GroupBy(a => Norm(a.Title))
            .ToDictionary(g => g.Key, g => g.ToList());

        var placementDiffs = new List<PlacementDiffDto>();
        foreach (var pp in doc.Placements)
        {
            var norm = Norm(pp.Name);
            placementsByName.TryGetValue(norm, out var hits);

            string matchStatus;
            Placement? matched = null;
            var candidates = new List<PlacementCandidateDto>();

            if (hits is { Count: 1 })
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

            var reasons = new List<string>();
            if (matchStatus == "unmatched") reasons.Add("No match to an existing placement");
            else if (matchStatus == "ambiguous") reasons.Add("Matches more than one existing placement");
            var invalidMetrics = rows.Where(r => r.Outcome == "invalid").Select(r => r.Metric).Distinct().ToList();
            if (invalidMetrics.Count > 0) reasons.Add($"Metric(s) with no data store: {string.Join(", ", invalidMetrics)}");
            if (pp.Notes.Count > 0) reasons.Add($"Note in file: {pp.Notes[0]}");
            if (doc.Warnings.Any(w => w.Message.Contains($"'{pp.Name}'") && w.Message.Contains("months sum to")))
                reasons.Add("Months don't add up to the file total");
            var needsReview = reasons.Count > 0 || rows.Any(r => r.Outcome == "change");

            placementDiffs.Add(new PlacementDiffDto(
                pp.Source, pp.Name, pp.Brand, pp.Audience, pp.Publisher, pp.Template, pp.Objective,
                matchStatus, matched?.Id, matched?.Name, candidates, rows,
                pp.Notes, needsReview, reasons, Array.Empty<PlacementSuggestionDto>()));
        }

        // AI reconciliation runs on flagged blocks only (no-op when AI is disabled),
        // pre-filling per-send suggestions the admin then approves. Never auto-writes.
        if (_recon.IsEnabled)
        {
            var flaggedIndices = placementDiffs
                .Select((d, i) => (d, i)).Where(x => x.d.NeedsReview).Select(x => x.i).ToList();
            if (flaggedIndices.Count > 0)
            {
                var reconCandidates = placements
                    .Select(p => new ReconCandidate(p.Id, p.Name, p.Template.Code.ToString(), p.Brand.Name, p.Publisher.Slug))
                    .ToList();
                var fileHashByName = fileData
                    .GroupBy(fd => fd.FileName)
                    .ToDictionary(g => g.Key, g => g.First().Hash, StringComparer.Ordinal);

                var suggestions = await _recon.SuggestAsync(client.Id, doc, flaggedIndices, fileHashByName, reconCandidates, ct);
                if (suggestions.Count > 0)
                    placementDiffs = placementDiffs
                        .Select((d, i) => suggestions.TryGetValue(i, out var s) ? d with { Suggestions = s } : d)
                        .ToList();
            }
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
                ea.Source, ea.Brand, ea.Type, ea.Title, ea.Author, ea.Expiry,
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

        return new ImportPreviewDto(request.Year, sources, headline, placementDiffs, educationDiffs, globalWarnings);
    }

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

            var n = _placementWrite.UpsertActuals(placement,
                cp.Actuals.Select(a => new ActualWrite(a.Year, a.Month, a.MetricKey, a.Value, a.Note)));
            valuesWritten += n;
            placementsWritten++;
        }

        foreach (var ce in request.Education)
        {
            if (ce.Values.Count == 0) continue;
            var asset = await _context.EducationAssets
                .Include(a => a.Values)
                .FirstOrDefaultAsync(a => a.Id == ce.AssetId && a.Page.ClientId == client.Id, ct)
                ?? throw new ImportConflictException($"Education asset {ce.AssetId} not found for this client");

            var n = _educationWrite.UpsertAssetValues(asset,
                ce.Values.Select(v => new EducationValueWrite(v.Status, v.Year, v.Month, v.Value)));
            eduValuesWritten += n;
            eduAssetsWritten++;
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

        return new ImportCommitResultDto(placementsWritten, valuesWritten, eduAssetsWritten, eduValuesWritten);
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

    private static string Norm(string s) => NormWhitespace.Replace((s ?? "").Trim().ToLowerInvariant(), " ");
}
