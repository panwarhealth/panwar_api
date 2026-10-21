namespace Panwar.Api.Models;

public interface IHarvestRecord
{
    Guid Id { get; }
    long? HarvestId { get; set; }
    DateTime UpdatedAt { get; set; }
}
