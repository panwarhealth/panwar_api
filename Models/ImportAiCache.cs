namespace Panwar.Api.Models;

/// <summary>
/// Cached AI reconciliation suggestions for a file, keyed by client + content
/// hash so re-previewing the same workbook makes zero further AI calls.
/// </summary>
public class ImportAiCache
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public required string ContentHash { get; set; }   // SHA-256 hex of the file bytes
    public required string SuggestionsJson { get; set; } // jsonb - the AI proposals for this file
    public DateTime CreatedAt { get; set; }
}
