namespace Panwar.Api.Models;

/// <summary>
/// Per-client negotiated baselines: e.g. "for Reckitt, AJP digital banner
/// impressions baseline is 175,560 per month at $0.0013 CTR target".
/// These default into placement KPIs on creation; editors can override.
/// </summary>
public class ClientPublisherBaseline
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid PublisherId { get; set; }
    public Guid TemplateId { get; set; }
    public required string MetricKey { get; set; }
    public decimal Value { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Client Client { get; set; } = null!;
    public Publisher Publisher { get; set; } = null!;
    public MetricTemplate Template { get; set; } = null!;
}
