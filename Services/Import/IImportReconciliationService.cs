using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Import;

public record ReconCandidate(Guid Id, string Name, string Template, string Brand, string Publisher);

// Suggestions keyed by the placement's index in doc.Placements, plus the files whose
// AI run failed outright (so the caller can tell "no suggestions" from "AI fell over").
public record ReconResult(
    IReadOnlyDictionary<int, List<PlacementSuggestionDto>> Suggestions,
    IReadOnlyList<string> FailedFiles);

public interface IImportReconciliationService
{
    bool IsEnabled { get; }

    // For the flagged placements only. When allowLive is false only cached results
    // are returned (0 AI calls); when true the agentic loop runs on cache misses.
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
}
