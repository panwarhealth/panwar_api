using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Import;

public record ReconCandidate(Guid Id, string Name, string Template, string Brand, string Publisher);

public interface IImportReconciliationService
{
    bool IsEnabled { get; }

    // For the flagged placements only, returns AI per-send suggestions keyed by
    // the placement's index in doc.Placements. Empty when AI is disabled.
    Task<IReadOnlyDictionary<int, List<PlacementSuggestionDto>>> SuggestAsync(
        Guid clientId,
        ImportDocument doc,
        IReadOnlyList<int> flaggedIndices,
        IReadOnlyDictionary<string, string> fileHashByName,
        IReadOnlyList<ReconCandidate> candidates,
        CancellationToken ct);
}
