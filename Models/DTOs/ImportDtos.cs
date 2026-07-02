namespace Panwar.Api.Models.DTOs;

// ── Requests ────────────────────────────────────────────────────────────────

public record ImportUploadUrlRequest(string FileName, string ContentType);
public record ImportUploadUrlResponse(string UploadUrl, string ObjectKey);

public record ImportFileRef(string ObjectKey, string FileName, string? ContentHash = null, string? FormatId = null);

// JobId lets the frontend poll live progress while the preview builds (optional).
public record ImportPreviewRequest(int Year, IReadOnlyList<ImportFileRef> Files, Guid? JobId = null);

public record ImportCommitRequest(
    int Year,
    IReadOnlyList<ImportFileRef> Files,
    IReadOnlyList<CommitPlacementDto> Placements,
    IReadOnlyList<CommitEducationDto> Education,
    bool Acknowledged);

// Either PlacementId (write to an existing placement) or NewPlacement (create one,
// mirroring how staff add a placement per send) is set. Source is the origin file
// name (matches PlacementDiffDto.Source) - needed so a multi-file commit can stamp
// each file's import_ai_log with only ITS OWN placements, not every file's.
// ParsedName is the file's block name; when set alongside PlacementId, the commit
// remembers the mapping (import_name_alias) so next month's import auto-matches.
public record CommitPlacementDto(string Source, string? ParsedName, Guid? PlacementId, NewPlacementSpec? NewPlacement, IReadOnlyList<CommitActualDto> Actuals, IReadOnlyList<string>? SendDates = null);
public record NewPlacementSpec(string Brand, string Publisher, string? Audience, string Template, string Name, string Objective);
public record CommitActualDto(int Year, int Month, string MetricKey, decimal Value, string? Note);
public record CommitEducationDto(Guid? AssetId, IReadOnlyList<CommitEducationValueDto> Values, string? Expiry = null, NewEducationAssetSpec? NewAsset = null);
public record NewEducationAssetSpec(Guid PageId, string? Group, string Brand, string? Type, string Title, string? Author);
public record CommitEducationValueDto(string Status, int Year, int Month, decimal Value);

// ── Preview response ────────────────────────────────────────────────────────

public record ImportPreviewDto(
    int Year,
    IReadOnlyList<ImportSourceDto> Sources,
    ImportHeadlineDto Headline,
    IReadOnlyList<PlacementDiffDto> Placements,
    IReadOnlyList<EducationDiffDto> Education,
    IReadOnlyList<string> Warnings,
    bool AiEnabled,    // false only when no ANTHROPIC_API_KEY is configured (AI layer off)
    bool AiFailed);    // true when the AI run failed for at least one file - suggestions may be missing

public record ImportSourceDto(
    string File,
    string ObjectKey,
    string FormatId,
    string Match,
    string ContentHash,
    AlreadyImportedDto? AlreadyImported,
    IReadOnlyList<string> Warnings);

public record AlreadyImportedDto(DateTime Date, string? By);

public record ImportHeadlineDto(int Match, int Change, int New, int Invalid, int UnmatchedPlacements, int TotalValues);

public record PlacementDiffDto(
    string Source,
    string ParsedName,
    string Brand,
    string? Audience,
    string Publisher,
    string Template,
    string Objective,
    string MatchStatus,            // "matched" | "ambiguous" | "unmatched"
    Guid? PlacementId,
    string? MatchedName,
    bool MatchedByMemory,          // matched because the admin mapped this exact block name before
    IReadOnlyList<PlacementCandidateDto> Candidates,
    IReadOnlyList<ActualDiffDto> Rows,
    IReadOnlyList<string> Notes,    // human guidance from the file - takes priority over raw cells
    bool NeedsReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<PlacementSuggestionDto> Suggestions, // AI per-send proposals (empty if AI disabled)
    IReadOnlyList<SourceViewDto> SourceViews);         // spreadsheet-style excerpts of the cells the AI used, for cross-checking

// A small spreadsheet-style excerpt straight from the uploaded file - the cells
// the AI pulled numbers from, so the user can eyeball the source. Highlighted
// cells are the exact ones the AI cited.
public record SourceViewDto(string Sheet, IReadOnlyList<string> Tabs, IReadOnlyList<SourceGridRowDto> Rows);
public record SourceGridRowDto(int Row, IReadOnlyList<SourceGridCellDto> Cells);
public record SourceGridCellDto(string Col, string Value, bool Highlight);

// One AI-proposed per-send resolution for a flagged block: which month, what the
// note says it is, and the existing placement it maps to. Values (if any) are
// numbers the AI pulled from a cited cell and that passed the grounding pass.
public record PlacementSuggestionDto(
    int Month,
    string TopicLabel,
    Guid? TargetPlacementId,
    string? TargetName,
    string Reason,
    double Confidence,
    IReadOnlyList<SuggestionValueDto> Values,
    IReadOnlyList<string> SendDates,           // eDM send dates the AI read from the note (ISO yyyy-MM-dd)
    IReadOnlyList<SuggestionCellRefDto> Evidence); // cells that told the AI when/what (highlighted as proof)

// A cell the AI points at as justification (a note, a date row) rather than a number.
// Verified like values: only cells the AI actually read survive.
public record SuggestionCellRefDto(string Sheet, string Cell);

// A grounded value the AI cited from a specific cell (used for the cross-tab
// "get the data from tab X" case). Only values whose citation matched the
// workbook snapshot are surfaced here.
public record SuggestionValueDto(
    string Metric,
    decimal Value,
    string SourceSheet,
    string SourceCell);

public record PlacementCandidateDto(Guid PlacementId, string Name, string Template);

public record ActualDiffDto(
    string Metric,
    int Month,
    decimal NewValue,
    decimal? OldValue,
    string Outcome,                // "match" | "change" | "new" | "invalid"
    string? Note);

public record EducationDiffDto(
    string Source,
    string? Group,
    string Brand,
    string? Type,
    string Title,
    string? Author,
    string? Expiry,
    string MatchStatus,            // "matched" | "ambiguous" | "unmatched"
    Guid? AssetId,
    Guid? PageId,
    string? PageName,
    IReadOnlyList<EducationCandidateDto> Candidates,
    IReadOnlyList<EducationValueDiffDto> Rows);

public record EducationCandidateDto(Guid AssetId, Guid PageId, string PageName, string Title);

public record EducationValueDiffDto(
    string Status,
    int Year,
    int Month,
    decimal NewValue,
    decimal? OldValue,
    string Outcome);               // "match" | "change" | "new"

// ── Commit response ─────────────────────────────────────────────────────────

public record ImportCommitResultDto(int PlacementsWritten, int ValuesWritten, int EducationAssetsWritten, int EducationValuesWritten);
