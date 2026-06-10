namespace Panwar.Api.Models;

/// <summary>
/// Year-scoped KPI targets per (client, publisher, template, metric): e.g.
/// "for Reckitt 2026, AJP digital banner impressions target is 175,560".
/// Clients hand these over before the year starts; they default into placement
/// KPIs on creation for that year, and editors can override per placement.
/// </summary>
public class ClientPublisherBaseline
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid PublisherId { get; set; }
    public Guid TemplateId { get; set; }
    public int Year { get; set; }
    public required string MetricKey { get; set; }
    public decimal Value { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Client Client { get; set; } = null!;
    public Publisher Publisher { get; set; } = null!;
    public MetricTemplate Template { get; set; } = null!;
}
