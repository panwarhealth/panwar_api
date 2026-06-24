using Panwar.Api.Models;

namespace Panwar.Api.Services.Write;

public record EducationValueWrite(string Status, int Year, int Month, decimal Value);

public interface IEducationWriteService
{
    int UpsertAssetValues(EducationAsset asset, IEnumerable<EducationValueWrite> rows);
}
