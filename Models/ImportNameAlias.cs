namespace Panwar.Api.Models;

/// <summary>
/// Remembers where the admin mapped a file block last time: "this block name in
/// this client's workbooks goes to that placement". Written when a commit maps a
/// block to an existing placement; used by the preview matcher so repeat imports
/// auto-match instead of asking the same question every month.
/// </summary>
public class ImportNameAlias
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public required string SourceName { get; set; }   // normalized parsed block name
    public Guid PlacementId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
