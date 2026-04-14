namespace Panwar.Api.Models;

/// <summary>
/// Junction table — a client user can belong to multiple clients (e.g. a
/// consultant at an agency working with several pharma companies). Employees
/// do NOT live here; they access all clients via their portal roles.
/// </summary>
public class UserClient
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ClientId { get; set; }
    public DateTime CreatedAt { get; set; }

    public AppUser User { get; set; } = null!;
    public Client Client { get; set; } = null!;
}
