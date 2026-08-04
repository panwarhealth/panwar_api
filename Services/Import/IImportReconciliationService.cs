using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Import;

// Months is what the placement already holds this year. Placements often share a
// name because each month's buy is its own row, so without it the model is choosing
// between identical-looking options.
public record ReconCandidate(Guid Id, string Name, string Template, string Brand, string Publisher, IReadOnlyList<int> Months);

// Suggestions keyed by index into doc.Placements.
public record ReconResult(
    IReadOnlyDictionary<int, List<PlacementSuggestionDto>> Suggestions,
    IReadOnlyList<string> FailedFiles);

public record ExtractResult(
    IReadOnlyDictionary<string, List<ParsedPlacement>> ByFile,
    IReadOnlyList<string> FailedFiles);

public interface IImportReconciliationService
{
    bool IsEnabled { get; }

    // allowLive false = cached results only, no AI calls.
    Task<ReconResult> SuggestAsync(
        Guid clientId,
        ImportDocument doc,
        IReadOnlyList<int> flaggedIndices,
        IReadOnlyDictionary<string, string> fileHashByName,
        IReadOnlyList<ReconCandidate> candidates,
        Guid? userId,
        bool allowLive,
        Guid jobId,
        CancellationToken ct);

    // For files the deterministic parser produced nothing from.
    Task<ExtractResult> ExtractAsync(
        Guid clientId,
        ImportDocument doc,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, string> fileHashByName,
        MetricVocabulary vocabulary,
        Guid? userId,
        bool allowLive,
        Guid jobId,
        CancellationToken ct);
}
