namespace Panwar.Api.Models;

public class TsTask : IHarvestRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool IsCommon { get; set; }
    public bool Archived { get; set; }
    public long? HarvestId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
