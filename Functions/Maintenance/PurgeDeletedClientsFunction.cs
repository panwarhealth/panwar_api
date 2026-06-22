using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;

namespace Panwar.Api.Functions.Maintenance;

// Daily sweep that permanently purges workbook (client) data 30+ days after it was soft-deleted.
public class PurgeDeletedClientsFunction
{
    private readonly ILogger<PurgeDeletedClientsFunction> _logger;
    private readonly AppDbContext _context;

    public PurgeDeletedClientsFunction(ILogger<PurgeDeletedClientsFunction> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Function("PurgeDeletedClients")]
    public async Task Run([TimerTrigger("0 0 3 * * *")] TimerInfo timer, FunctionContext context)
    {
        var ct = context.CancellationToken;
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var ids = await _context.Clients
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null && c.DeletedAt < cutoff)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (ids.Count == 0) return;

        var purged = 0;
        foreach (var id in ids)
        {
            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT panwar_portals.purge_client({id})", ct);
                await tx.CommitAsync(ct);
                purged++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge soft-deleted client {Id}", id);
            }
        }

        _logger.LogInformation("Purged {Purged} of {Total} clients past the 30-day soft-delete window", purged, ids.Count);
    }
}
