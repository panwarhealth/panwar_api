namespace Panwar.Api.Models;

public class Brand
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    /// <summary>Hex display colour (e.g. "#d62728") used to highlight the brand on dashboards.</summary>
    public string? Color { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Client Client { get; set; } = null!;
    public ICollection<Placement> Placements { get; set; } = new List<Placement>();
}
