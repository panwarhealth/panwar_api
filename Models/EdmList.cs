using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

public class EdmList
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public EdmListKind Kind { get; set; }
    public EdmSyncSource? SyncSource { get; set; }
    public Guid SenderId { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }
    // Null for the synced lists, which the system creates (Migrations/*_SeedEdmSyncedLists.cs).
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public EdmSender Sender { get; set; } = null!;
    public AppUser? Creator { get; set; }
    public ICollection<EdmContact> Contacts { get; set; } = new List<EdmContact>();
}
