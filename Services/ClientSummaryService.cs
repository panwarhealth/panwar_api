using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

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
        string? brandSlug,
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

        PlacementMetrics.EnsurePrintImpressions(placements);

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

        var eduSpans = await _context.EducationPages
            .AsNoTracking()
            .Where(p => p.ClientId == clientId)
            .Select(p => new
            {
                ValMin = p.Assets.SelectMany(a => a.Values).Min(v => (int?)v.Year),
                ValMax = p.Assets.SelectMany(a => a.Values).Max(v => (int?)v.Year),
                PtMin = p.Charts.SelectMany(c => c.Series).SelectMany(s => s.DataPoints).Min(d => (int?)d.Year),
                PtMax = p.Charts.SelectMany(c => c.Series).SelectMany(s => s.DataPoints).Max(d => (int?)d.Year),
            })
            .ToListAsync(cancellationToken);
        var eduYears = eduSpans
            .SelectMany(s => new[] { s.ValMin, s.ValMax, s.PtMin, s.PtMax })
            .Where(y => y.HasValue)
            .Select(y => y!.Value)
            .ToList();
        if (eduYears.Count > 0)
        {
            var eduFromOrd = PeriodWindow.Ord(eduYears.Min(), 1);
            var eduToOrd = PeriodWindow.Ord(eduYears.Max(), 12);
            availFromOrd = Math.Min(availFromOrd ?? eduFromOrd, eduFromOrd);
            availToOrd = Math.Max(availToOrd ?? eduToOrd, eduToOrd);
        }

        int latestYear = (actualToOrd ?? availToOrd).HasValue ? (actualToOrd ?? availToOrd)!.Value / 12 : FallbackYear;

        var fromOrd = PeriodWindow.TryParse(from, out var f) ? f : PeriodWindow.Ord(latestYear, 1);
        var toOrd = PeriodWindow.TryParse(to, out var t) ? t : PeriodWindow.Ord(latestYear, 12);
        if (toOrd < fromOrd) (fromOrd, toOrd) = (toOrd, fromOrd);

        placements = placements.Where(p => PeriodWindow.AppearsInWindow(p, fromOrd, toOrd)).ToList();

        if (!string.IsNullOrWhiteSpace(brandSlug))
            placements = placements.Where(p => p.Brand.Slug == brandSlug).ToList();

        int fromYear = fromOrd / 12, toYear = toOrd / 12;
        var cpdInvestments = await _context.CpdInvestments.AsNoTracking()
            .Where(c => c.Brand.ClientId == clientId && c.Year >= fromYear && c.Year <= toYear)
            .Where(c => string.IsNullOrWhiteSpace(brandSlug) || c.Brand.Slug == brandSlug)
            .ToListAsync(cancellationToken);
        var cpdTotal = cpdInvestments.Sum(c => c.Cost);
        var cpdByBrandAudience = cpdInvestments
            .GroupBy(c => new { c.BrandId, c.AudienceId })
            .ToDictionary(g => (g.Key.BrandId, g.Key.AudienceId), g => g.Sum(c => c.Cost));
        var cpdByPublisher = cpdInvestments.GroupBy(c => c.PublisherId).ToDictionary(g => g.Key, g => g.Sum(c => c.Cost));

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
            CpdInvestmentCost: cpdTotal,
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
                    CpdInvestmentCost: cpdByBrandAudience.GetValueOrDefault((first.BrandId, first.AudienceId), 0m),
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
                    CpdInvestmentCost: cpdByPublisher.GetValueOrDefault(g.Key, 0m),
                    Metrics: WindowMetrics(list),
                    TargetMetrics: Targets(list));
            })
            .OrderByDescending(r => r.MediaCost)
            .ThenBy(r => r.Label)
            .ToList();

        var byCategory = placements
            .GroupBy(p => CategoryOf(p.Template.Code))
            .Select(g =>
            {
                var list = g.ToList();
                return new SummaryRowDto(
                    Label: g.Key,
                    BrandSlug: null,
                    AudienceSlug: null,
                    PlacementCount: list.Count,
                    MediaCost: Costing(list).Sum(p => p.MediaCost),
                    PlannedMediaCost: PlannedSum(list),
                    CpdInvestmentCost: 0m,
                    Metrics: WindowMetrics(list),
                    TargetMetrics: Targets(list));
            })
            .OrderByDescending(r => r.MediaCost)
            .ThenBy(r => r.Label)
            .ToList();

        var byDigitalFormat = placements
            .Select(p => new { Placement = p, Format = DigitalFormatOf(p.Template.Code, p.EdmSubcategory) })
            .Where(x => x.Format != null)
            .GroupBy(x => x.Format!)
            .Select(g =>
            {
                var list = g.Select(x => x.Placement).ToList();
                return new SummaryRowDto(
                    Label: g.Key,
                    BrandSlug: null,
                    AudienceSlug: null,
                    PlacementCount: list.Count,
                    MediaCost: Costing(list).Sum(p => p.MediaCost),
                    PlannedMediaCost: PlannedSum(list),
                    CpdInvestmentCost: 0m,
                    Metrics: WindowMetrics(list),
                    TargetMetrics: Targets(list));
            })
            .OrderByDescending(r => r.MediaCost)
            .ThenBy(r => r.Label)
            .ToList();

        var brands = placements
            .Select(p => p.Brand)
            .DistinctBy(b => b.Id)
            .OrderBy(b => b.Name)
            .Select(b => new BrandRefDto(b.Slug, b.Name, b.Color))
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
                CpdInvestmentCost: 0m,
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
                    Months = (IReadOnlyList<BrandMonthlyPointDto>)g
                        .SelectMany(p => p.Actuals.Where(InWindow)
                            .Select(a => new { Actual = a, Category = CategoryOf(p.Template.Code) }))
                        .GroupBy(x => (x.Actual.Year, x.Actual.Month))
                        .OrderBy(m => PeriodWindow.Ord(m.Key.Year, m.Key.Month))
                        .Select(m =>
                        {
                            var all = new Dictionary<string, decimal>();
                            var digital = new Dictionary<string, decimal>();
                            var print = new Dictionary<string, decimal>();
                            foreach (var x in m)
                            {
                                Add(all, x.Actual.MetricKey, x.Actual.Value);
                                if (x.Category == "Digital") Add(digital, x.Actual.MetricKey, x.Actual.Value);
                                else if (x.Category == "Print") Add(print, x.Actual.MetricKey, x.Actual.Value);
                            }
                            return new BrandMonthlyPointDto(m.Key.Year, m.Key.Month, all, digital, print);
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
            ByCategory: byCategory,
            ByDigitalFormat: byDigitalFormat,
            Brands: brands,
            IsPlan: isPlan,
            Summary: summary,
            ShowBrandMonthlyChart: client.ShowBrandMonthlyChart,
            ShowPublisherChart: client.ShowPublisherChart,
            MonthlyByBrand: monthlyByBrand,
            ByAsset: byAsset);
    }

    private static string CategoryOf(MetricTemplateCode code) => code switch
    {
        MetricTemplateCode.Print => "Print",
        MetricTemplateCode.Education => "Education",
        _ => "Digital",
    };

    private static string? DigitalFormatOf(MetricTemplateCode code, EdmSubcategory? edm) => code switch
    {
        MetricTemplateCode.DigitalDisplay => "Digital Display",
        MetricTemplateCode.SponsoredContent => "Spon Con",
        MetricTemplateCode.Edm => edm switch
        {
            EdmSubcategory.Solus => "eDM Solus",
            EdmSubcategory.SponsoredContent => "eDM Spon Con",
            EdmSubcategory.Banner => "eDM Banners",
            _ => "eDM",
        },
        _ => null,
    };
}
