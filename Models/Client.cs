namespace Panwar.Api.Models;

public class Client
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? AccentColor { get; set; }

    /// <summary>Show the monthly touchpoints-by-brand chart on the client overview.</summary>
    public bool ShowBrandMonthlyChart { get; set; } = true;

    /// <summary>Show the touchpoints-vs-engagements-by-publisher chart on the client overview.</summary>
    public bool ShowPublisherChart { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<Brand> Brands { get; set; } = new List<Brand>();
    public ICollection<Audience> Audiences { get; set; } = new List<Audience>();
}
