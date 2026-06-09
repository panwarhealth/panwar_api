using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

/// <summary>
/// Rolls up every placement for a client into the overview payload, within a
/// month window. Performance metrics are windowed; KPI targets and spend are
/// period-level. All in-memory aggregation after a single load.
/// </summary>
public class ClientSummaryService : IClientSummaryService
{
    private const int FallbackYear = 2025;

    private readonly AppDbContext _context;

    public ClientSummaryService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ClientSummaryResponse?> GetSummaryAsync(
        Guid clientId,
        string? from,
        string? to,
        CancellationToken cancellationToken)
    {
        var client = await _context.Clients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);
        if (client is null) return null;

        var placements = await _context.Placements
            .AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Audience)
            .Include(p => p.Publisher)
            .Include(p => p.Kpis)
            .Include(p => p.Actuals)
            .Where(p => p.Brand.ClientId == clientId)
            .ToListAsync(cancellationToken);

        var allActuals = placements.SelectMany(p => p.Actuals).ToList();
        int? availFromOrd = allActuals.Count > 0 ? allActuals.Min(a => PeriodWindow.Ord(a.Year, a.Month)) : null;
        int? availToOrd = allActuals.Count > 0 ? allActuals.Max(a => PeriodWindow.Ord(a.Year, a.Month)) : null;

        var fromOrd = PeriodWindow.TryParse(from, out var f) ? f : availFromOrd ?? PeriodWindow.Ord(FallbackYear, 1);
        var toOrd = PeriodWindow.TryParse(to, out var t) ? t : availToOrd ?? PeriodWindow.Ord(FallbackYear, 12);
        if (toOrd < fromOrd) (fromOrd, toOrd) = (toOrd, fromOrd);

        bool InWindow(PlacementActual a)
        {
            var o = PeriodWindow.Ord(a.Year, a.Month);
            return o >= fromOrd && o <= toOrd;
        }
        static void Add(Dictionary<string, decimal> acc, string key, decimal value)
        {
            acc.TryGetValue(key, out var cur);
            acc[key] = cur + value;
        }

        Dictionary<string, decimal> WindowMetrics(IEnumerable<Placement> ps)
        {
            var d = new Dictionary<string, decimal>();
            foreach (var a in ps.SelectMany(p => p.Actuals).Where(InWindow)) Add(d, a.MetricKey, a.Value);
            return d;
        }
        Dictionary<string, decimal> Targets(IEnumerable<Placement> ps)
        {
            var d = new Dictionary<string, decimal>();
            foreach (var k in ps.SelectMany(p => p.Kpis)) Add(d, k.MetricKey, k.TargetValue);
            return d;
        }
        decimal? PlannedSum(IReadOnlyCollection<Placement> ps) =>
            ps.Any(p => p.PlannedMediaCost.HasValue) ? ps.Sum(p => p.PlannedMediaCost ?? 0) : null;

        var totals = new DashboardTotalsDto(
            PlacementCount: placements.Count,
            MediaCost: placements.Sum(p => p.MediaCost),
            PlannedMediaCost: PlannedSum(placements),
            CpdInvestmentCost: placements.Sum(p => p.CpdInvestmentCost ?? 0),
            Metrics: WindowMetrics(placements),
            TargetMetrics: Targets(placements));

        var byBrandAudience = placements
            .GroupBy(p => new { p.BrandId, p.AudienceId })
            .Select(g =>
            {
                var first = g.First();
                var list = g.ToList();
                return new SummaryRowDto(
                    Label: $"{first.Brand.Name} · {first.Audience.Name}",
                    BrandSlug: first.Brand.Slug,
                    AudienceSlug: first.Audience.Slug,
                    PlacementCount: list.Count,
                    MediaCost: list.Sum(p => p.MediaCost),
                    PlannedMediaCost: PlannedSum(list),
                    CpdInvestmentCost: list.Sum(p => p.CpdInvestmentCost ?? 0),
                    Metrics: WindowMetrics(list),
                    TargetMetrics: Targets(list));
            })
            .OrderBy(r => r.Label)
            .ToList();

        var byPublisher = placements
            .GroupBy(p => p.PublisherId)
            .Select(g =>
            {
                var list = g.ToList();
                return new SummaryRowDto(
                    Label: g.First().Publisher.Name,
                    BrandSlug: null,
                    AudienceSlug: null,
                    PlacementCount: list.Count,
                    MediaCost: list.Sum(p => p.MediaCost),
                    PlannedMediaCost: PlannedSum(list),
                    CpdInvestmentCost: list.Sum(p => p.CpdInvestmentCost ?? 0),
                    Metrics: WindowMetrics(list),
                    TargetMetrics: Targets(list));
            })
            .OrderByDescending(r => r.MediaCost)
            .ThenBy(r => r.Label)
            .ToList();

        var period = new DashboardPeriodDto(
            From: PeriodWindow.ToYm(fromOrd),
            To: PeriodWindow.ToYm(toOrd),
            AvailableFrom: availFromOrd.HasValue ? PeriodWindow.ToYm(availFromOrd.Value) : null,
            AvailableTo: availToOrd.HasValue ? PeriodWindow.ToYm(availToOrd.Value) : null);

        return new ClientSummaryResponse(
            Client: new ClientSummaryClientDto(client.Id, client.Name, client.Slug),
            Period: period,
            Totals: totals,
            ByBrandAudience: byBrandAudience,
            ByPublisher: byPublisher);
    }
}
