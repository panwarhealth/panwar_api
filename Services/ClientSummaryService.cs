using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

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
            .Include(p => p.Template)
            .Include(p => p.Kpis)
            .Include(p => p.Actuals)
            .Where(p => p.Brand.ClientId == clientId)
            .ToListAsync(cancellationToken);

        // Available span covers actuals AND live periods so planned future years are selectable.
        // Default window is the latest year with actuals; falls back to the latest planned year.
        var spanActuals = placements.SelectMany(p => p.Actuals).ToList();
        var liveSpans = placements.Select(PeriodWindow.LiveSpan).ToList();
        int? actualToOrd = spanActuals.Count > 0 ? spanActuals.Max(a => PeriodWindow.Ord(a.Year, a.Month)) : null;
        int? availFromOrd = null, availToOrd = null;
        if (spanActuals.Count > 0 || liveSpans.Count > 0)
        {
            availFromOrd = Math.Min(
                spanActuals.Count > 0 ? spanActuals.Min(a => PeriodWindow.Ord(a.Year, a.Month)) : int.MaxValue,
                liveSpans.Count > 0 ? liveSpans.Min(s => s.fromOrd) : int.MaxValue);
            availToOrd = Math.Max(
                actualToOrd ?? int.MinValue,
                liveSpans.Count > 0 ? liveSpans.Max(s => s.toOrd) : int.MinValue);
        }

        // Summary years widen the span so clients with plan notes but no placements can still navigate.
        var summaryYears = await _context.ClientYearSummaries
            .AsNoTracking()
            .Where(s => s.ClientId == clientId)
            .Select(s => s.Year)
            .ToListAsync(cancellationToken);
        if (summaryYears.Count > 0)
        {
            var summaryFromOrd = PeriodWindow.Ord(summaryYears.Min(), 1);
            var summaryToOrd = PeriodWindow.Ord(summaryYears.Max(), 12);
            availFromOrd = Math.Min(availFromOrd ?? summaryFromOrd, summaryFromOrd);
            availToOrd = Math.Max(availToOrd ?? summaryToOrd, summaryToOrd);
        }

        int latestYear = (actualToOrd ?? availToOrd).HasValue ? (actualToOrd ?? availToOrd)!.Value / 12 : FallbackYear;

        var fromOrd = PeriodWindow.TryParse(from, out var f) ? f : PeriodWindow.Ord(latestYear, 1);
        var toOrd = PeriodWindow.TryParse(to, out var t) ? t : PeriodWindow.Ord(latestYear, 12);
        if (toOrd < fromOrd) (fromOrd, toOrd) = (toOrd, fromOrd);

        placements = placements.Where(p => PeriodWindow.AppearsInWindow(p, fromOrd, toOrd)).ToList();

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
            foreach (var p in ps)
            {
                var fraction = PeriodWindow.TargetFraction(p, fromOrd, toOrd);
                foreach (var k in p.Kpis) Add(d, k.MetricKey, k.TargetValue * fraction);
            }
            return d;
        }
        IEnumerable<Placement> Costing(IEnumerable<Placement> ps) =>
            ps.Where(p => PeriodWindow.CostsCountInWindow(p, fromOrd, toOrd));
        decimal? PlannedSum(IEnumerable<Placement> ps)
        {
            var costing = Costing(ps).ToList();
            return costing.Any(p => p.PlannedMediaCost.HasValue) ? costing.Sum(p => p.PlannedMediaCost ?? 0) : null;
        }

        var totals = new DashboardTotalsDto(
            PlacementCount: placements.Count,
            MediaCost: Costing(placements).Sum(p => p.MediaCost),
            PlannedMediaCost: PlannedSum(placements),
            CpdInvestmentCost: Costing(placements).Sum(p => p.CpdInvestmentCost ?? 0),
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
                    MediaCost: Costing(list).Sum(p => p.MediaCost),
                    PlannedMediaCost: PlannedSum(list),
                    CpdInvestmentCost: Costing(list).Sum(p => p.CpdInvestmentCost ?? 0),
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
                    MediaCost: Costing(list).Sum(p => p.MediaCost),
                    PlannedMediaCost: PlannedSum(list),
                    CpdInvestmentCost: Costing(list).Sum(p => p.CpdInvestmentCost ?? 0),
                    Metrics: WindowMetrics(list),
                    TargetMetrics: Targets(list));
            })
            .OrderByDescending(r => r.MediaCost)
            .ThenBy(r => r.Label)
            .ToList();

        var byAsset = placements
            .Select(p => new AssetRowDto(
                Name: p.Name,
                BrandName: p.Brand.Name,
                BrandSlug: p.Brand.Slug,
                AudienceName: p.Audience.Name,
                PublisherName: p.Publisher.Name,
                Objective: p.Objective.ToString(),
                TemplateCode: PlacementEnumNames.ToName(p.Template.Code),
                MediaCost: Costing(new[] { p }).Sum(x => x.MediaCost),
                CpdInvestmentCost: Costing(new[] { p }).Sum(x => x.CpdInvestmentCost ?? 0),
                Metrics: WindowMetrics(new[] { p }),
                TargetMetrics: Targets(new[] { p })))
            .OrderBy(a => a.BrandName)
            .ThenBy(a => a.PublisherName)
            .ThenBy(a => a.Name)
            .ToList();

        var period = new DashboardPeriodDto(
            From: PeriodWindow.ToYm(fromOrd),
            To: PeriodWindow.ToYm(toOrd),
            AvailableFrom: availFromOrd.HasValue ? PeriodWindow.ToYm(availFromOrd.Value) : null,
            AvailableTo: availToOrd.HasValue ? PeriodWindow.ToYm(availToOrd.Value) : null);

        var isPlan = totals.Metrics.Count == 0;

        var monthlyByBrand = new List<BrandMonthlyDto>();
        if (client.ShowBrandMonthlyChart && !isPlan)
        {
            monthlyByBrand = placements
                .GroupBy(p => p.BrandId)
                .Select(g => new
                {
                    g.First().Brand,
                    Months = (IReadOnlyList<DashboardMonthDto>)g
                        .SelectMany(p => p.Actuals)
                        .Where(InWindow)
                        .GroupBy(a => (a.Year, a.Month))
                        .OrderBy(m => PeriodWindow.Ord(m.Key.Year, m.Key.Month))
                        .Select(m =>
                        {
                            var d = new Dictionary<string, decimal>();
                            foreach (var a in m) Add(d, a.MetricKey, a.Value);
                            return new DashboardMonthDto(m.Key.Year, m.Key.Month, d);
                        })
                        .ToList(),
                })
                .Where(b => b.Months.Count > 0)
                .OrderBy(b => b.Brand.Name)
                .Select(b => new BrandMonthlyDto(b.Brand.Name, b.Brand.Slug, b.Months))
                .ToList();
        }

        var summaryYear = toOrd / 12;
        var summary = await _context.ClientYearSummaries
            .AsNoTracking()
            .Where(s => s.ClientId == clientId && s.Year == summaryYear)
            .Select(s => new YearSummaryDto(s.Year, s.Text))
            .FirstOrDefaultAsync(cancellationToken);

        return new ClientSummaryResponse(
            Client: new ClientSummaryClientDto(client.Id, client.Name, client.Slug),
            Period: period,
            Totals: totals,
            ByBrandAudience: byBrandAudience,
            ByPublisher: byPublisher,
            IsPlan: isPlan,
            Summary: summary,
            ShowBrandMonthlyChart: client.ShowBrandMonthlyChart,
            ShowPublisherChart: client.ShowPublisherChart,
            MonthlyByBrand: monthlyByBrand,
            ByAsset: byAsset);
    }
}
