namespace Panwar.Api.Models;

public class InviteEvent
{
    public Guid Id { get; set; }
    public Guid InviteId { get; set; }
    public required string Type { get; set; }
    public DateTime At { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public bool IsBot { get; set; }

    public ReportInvite Invite { get; set; } = null!;
}
