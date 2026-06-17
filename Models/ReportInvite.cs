namespace Panwar.Api.Models;

public class ReportInvite
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid RecipientUserId { get; set; }
    public required string RecipientEmail { get; set; }

    public required string Template { get; set; }
    public int Year { get; set; }
    public int? StartMonth { get; set; }
    public int? EndMonth { get; set; }

    public required string Token { get; set; }

    public DateTime SentAt { get; set; }
    public Guid? SentBy { get; set; }
    public int SendCount { get; set; }

    public DateTime? ClickedAt { get; set; }
    public int ClickCount { get; set; }
    public DateTime? ViewedAt { get; set; }
    public int ViewCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Client Client { get; set; } = null!;
    public AppUser Recipient { get; set; } = null!;
    public ICollection<InviteEvent> Events { get; set; } = new List<InviteEvent>();
}
