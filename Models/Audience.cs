namespace Panwar.Api.Models;

public class Audience
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Client Client { get; set; } = null!;
    public ICollection<Placement> Placements { get; set; } = new List<Placement>();
}
