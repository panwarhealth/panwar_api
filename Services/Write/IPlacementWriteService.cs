using Panwar.Api.Models;

namespace Panwar.Api.Services.Write;

public record ActualWrite(int Year, int Month, string MetricKey, decimal Value, string? Note);

public interface IPlacementWriteService
{
    int UpsertActuals(Placement placement, IEnumerable<ActualWrite> rows);
}
