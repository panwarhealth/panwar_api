using Panwar.Api.Data;
using Panwar.Api.Models;

namespace Panwar.Api.Services.Write;

public class EducationWriteService : IEducationWriteService
{
    private readonly AppDbContext _context;

    public EducationWriteService(AppDbContext context) => _context = context;

    public int UpsertAssetValues(EducationAsset asset, IEnumerable<EducationValueWrite> rows)
    {
        var count = 0;
        foreach (var row in rows)
        {
            var status = (row.Status ?? "").Trim();
            if (status.Length == 0) continue;

            var existing = asset.Values.FirstOrDefault(v =>
                string.Equals(v.Status, status, StringComparison.OrdinalIgnoreCase) &&
                v.Year == row.Year && v.Month == row.Month);

            if (existing is null)
            {
                _context.EducationAssetValues.Add(new EducationAssetValue
                {
                    Id = Guid.NewGuid(),
                    EducationAssetId = asset.Id,
                    Status = status,
                    Year = row.Year,
                    Month = row.Month,
                    Value = row.Value,
                });
            }
            else
            {
                existing.Value = row.Value;
            }
            count++;
        }
        return count;
    }
}
