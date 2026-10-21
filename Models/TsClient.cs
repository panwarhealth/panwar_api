namespace Panwar.Api.Models;

public class TsClient : IHarvestRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? CodePrefix { get; set; }
    public bool SuppliesOwnCodes { get; set; }
    public bool Archived { get; set; }
    public long? HarvestId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<TsJob> Jobs { get; set; } = new List<TsJob>();
}
