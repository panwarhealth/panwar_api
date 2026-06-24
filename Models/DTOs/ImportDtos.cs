namespace Panwar.Api.Models.DTOs;

// ── Requests ────────────────────────────────────────────────────────────────

public record ImportUploadUrlRequest(string FileName, string ContentType);
public record ImportUploadUrlResponse(string UploadUrl, string ObjectKey);

public record ImportFileRef(string ObjectKey, string FileName, string? ContentHash = null, string? FormatId = null);

public record ImportPreviewRequest(int Year, IReadOnlyList<ImportFileRef> Files);

public record ImportCommitRequest(
    int Year,
    IReadOnlyList<ImportFileRef> Files,
    IReadOnlyList<CommitPlacementDto> Placements,
    IReadOnlyList<CommitEducationDto> Education,
    bool Acknowledged);

// Either PlacementId (write to an existing placement) or NewPlacement (create one,
// mirroring how staff add a placement per send) is set.
public record CommitPlacementDto(Guid? PlacementId, NewPlacementSpec? NewPlacement, IReadOnlyList<CommitActualDto> Actuals);
public record NewPlacementSpec(string Brand, string Publisher, string? Audience, string Template, string Name, string Objective);
public record CommitActualDto(int Year, int Month, string MetricKey, decimal Value, string? Note);
public record CommitEducationDto(Guid AssetId, IReadOnlyList<CommitEducationValueDto> Values);
public record CommitEducationValueDto(string Status, int Year, int Month, decimal Value);

// ── Preview response ────────────────────────────────────────────────────────

public record ImportPreviewDto(
    int Year,
    IReadOnlyList<ImportSourceDto> Sources,
    ImportHeadlineDto Headline,
    IReadOnlyList<PlacementDiffDto> Placements,
    IReadOnlyList<EducationDiffDto> Education,
    IReadOnlyList<string> Warnings);

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
    IReadOnlyList<PlacementCandidateDto> Candidates,
    IReadOnlyList<ActualDiffDto> Rows,
    IReadOnlyList<string> Notes,    // human guidance from the file - takes priority over raw cells
    bool NeedsReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<PlacementSuggestionDto> Suggestions); // AI per-send proposals (empty if AI disabled)

// One AI-proposed per-send resolution for a flagged block: which month, what the
// note says it is, and the existing placement it maps to.
public record PlacementSuggestionDto(
    int Month,
    string TopicLabel,
    Guid? TargetPlacementId,
    string? TargetName,
    string Reason,
    double Confidence);

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
