namespace Panwar.Api.Models;

/// <summary>
/// Global catalog of publishers (AJP, AP, Arterial, Healthed, AJGP, ADG,
/// Princeton, NewsGP, MT, Praxhub, Pharmacy Club). Not scoped per client.
/// </summary>
public class Publisher
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Website { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<PublisherTemplate> PublisherTemplates { get; set; } = new List<PublisherTemplate>();
    public ICollection<ClientPublisherBaseline> ClientPublisherBaselines { get; set; } = new List<ClientPublisherBaseline>();
    public ICollection<Placement> Placements { get; set; } = new List<Placement>();
}
