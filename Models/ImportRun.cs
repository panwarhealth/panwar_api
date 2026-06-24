namespace Panwar.Api.Models;

/// <summary>
/// Ledger of committed media-results imports. One row per source file committed,
/// keyed by content hash so a re-upload of the same file is detected and the
/// admin warned before committing it again.
/// </summary>
public class ImportRun
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public int Year { get; set; }
    public required string FileName { get; set; }
    public required string ContentHash { get; set; }   // SHA-256 hex of the uploaded bytes
    public required string FormatId { get; set; }       // adapter FormatId, e.g. "results-template"
    public int PlacementsWritten { get; set; }
    public int ValuesWritten { get; set; }
    public Guid? ImportedByUserId { get; set; }
    public DateTime ImportedAt { get; set; }
}
