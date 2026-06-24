using Panwar.Api.Data;
using Panwar.Api.Models;

namespace Panwar.Api.Services.Write;

public class PlacementWriteService : IPlacementWriteService
{
    private const int MaxNoteLength = 500;
    private readonly AppDbContext _context;

    public PlacementWriteService(AppDbContext context) => _context = context;

    public int UpsertActuals(Placement placement, IEnumerable<ActualWrite> rows)
    {
        var count = 0;
        foreach (var row in rows)
        {
            var key = (row.MetricKey ?? "").Trim().ToLowerInvariant();
            if (key.Length == 0) continue;
            var note = string.IsNullOrWhiteSpace(row.Note) ? null : row.Note.Trim();
            if (note is not null && note.Length > MaxNoteLength) note = note[..MaxNoteLength];

            var existing = placement.Actuals.FirstOrDefault(a =>
                a.Year == row.Year && a.Month == row.Month &&
                string.Equals(a.MetricKey, key, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _context.PlacementActuals.Add(new PlacementActual
                {
                    Id = Guid.NewGuid(),
                    PlacementId = placement.Id,
                    Year = row.Year,
                    Month = row.Month,
                    MetricKey = key,
                    Value = row.Value,
                    Note = note,
                });
            }
            else
            {
                existing.Value = row.Value;
                existing.Note = note;
            }
            count++;
        }
        return count;
    }
}
