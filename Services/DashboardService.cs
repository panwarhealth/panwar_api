using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.CloudflareR2;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services;

public class DashboardService : IDashboardService
{
    private const int FallbackYear = 2025;

    private readonly AppDbContext _context;
    private readonly ICloudflareR2Service _r2;

    public DashboardService(AppDbContext context, ICloudflareR2Service r2)
    {
        _context = context;
        _r2 = r2;
    }

    public async Task<DashboardResponse?> GetDashboardAsync(
        Guid clientId,
        string brandSlug,
        string audienceSlug,
        string? from,
        string? to,
        CancellationToken cancellationToken)
    {
        var brand = await _context.Brands.AsNoTracking()
            .FirstOrDefaultAsync(b => b.ClientId == clientId && b.Slug == brandSlug, cancellationToken);
        if (brand is null) return null;

        var audience = await _context.Audiences.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ClientId == clientId && a.Slug == audienceSlug, cancellationToken);
        if (audience is null) return null;

        var placements = await _context.Placements
            .AsNoTracking()
            .Include(p => p.Publisher)
            .Include(p => p.Template).ThenInclude(t => t.Fields)
            .Include(p => p.Kpis)
            .Include(p => p.Actuals)
            .Where(p => p.BrandId == brand.Id && p.AudienceId == audience.Id)
            .OrderBy(p => p.Publisher.Name)
            .ThenBy(p => p.Name)
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
        int latestYear = (actualToOrd ?? availToOrd).HasValue ? (actualToOrd ?? availToOrd)!.Value / 12 : FallbackYear;

        var fromOrd = PeriodWindow.TryParse(from, out var f) ? f : PeriodWindow.Ord(latestYear, 1);
        var toOrd = PeriodWindow.TryParse(to, out var t) ? t : PeriodWindow.Ord(latestYear, 12);
        if (toOrd < fromOrd) (fromOrd, toOrd) = (toOrd, fromOrd);

        placements = placements.Where(p => PeriodWindow.AppearsInWindow(p, fromOrd, toOrd)).ToList();
        var allActuals = placements.SelectMany(p => p.Actuals).ToList();

        int fromYear = fromOrd / 12, toYear = toOrd / 12;
        var cpdInvestments = await _context.CpdInvestments.AsNoTracking()
            .Where(c => c.BrandId == brand.Id && c.AudienceId == audience.Id && c.Year >= fromYear && c.Year <= toYear)
            .ToListAsync(cancellationToken);
        var cpdTotal = cpdInvestments.Sum(c => c.Cost);
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

        Dictionary<string, decimal> WindowTargets(IEnumerable<Placement> ps)
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

        var totalsMetrics = new Dictionary<string, decimal>();
        foreach (var a in allActuals.Where(InWindow)) Add(totalsMetrics, a.MetricKey, a.Value);
        var targetMetrics = WindowTargets(placements);

        var totals = new DashboardTotalsDto(
            PlacementCount: placements.Count,
            MediaCost: Costing(placements).Sum(p => p.MediaCost),
            PlannedMediaCost: PlannedSum(placements),
            CpdInvestmentCost: cpdTotal,
            Metrics: totalsMetrics,
            TargetMetrics: targetMetrics);

        var monthly = new List<DashboardMonthDto>();
        for (var o = fromOrd; o <= toOrd; o++)
        {
            int year = o / 12, month = o % 12 + 1;
            var mm = new Dictionary<string, decimal>();
            foreach (var a in allActuals)
                if (a.Year == year && a.Month == month) Add(mm, a.MetricKey, a.Value);
            monthly.Add(new DashboardMonthDto(year, month, mm));
        }

        var publishers = placements
            .GroupBy(p => p.PublisherId)
            .Select(g =>
            {
                var first = g.First().Publisher;
                var pm = new Dictionary<string, decimal>();
                foreach (var a in g.SelectMany(p => p.Actuals).Where(InWindow)) Add(pm, a.MetricKey, a.Value);
                var tm = WindowTargets(g);
                return new DashboardPublisherDto(
                    Id: first.Id,
                    Name: first.Name,
                    Slug: first.Slug,
                    PlacementCount: g.Count(),
                    MediaCost: Costing(g).Sum(p => p.MediaCost),
                    PlannedMediaCost: PlannedSum(g),
                    CpdInvestmentCost: cpdByPublisher.GetValueOrDefault(first.Id, 0m),
                    Metrics: pm,
                    TargetMetrics: tm);
            })
            .OrderByDescending(p => p.MediaCost)
            .ThenBy(p => p.Name)
            .ToList();

        // eDM sends sharing a GroupId merge into one card; other templates render one card each.
        async Task<DashboardPlacementDto> BuildCard(Placement rep, List<Placement> members)
        {
            var cardTotals = new Dictionary<string, decimal>();
            foreach (var a in members.SelectMany(m => m.Actuals).Where(InWindow)) Add(cardTotals, a.MetricKey, a.Value);
            var targets = WindowTargets(members);
            string? artworkViewUrl = string.IsNullOrWhiteSpace(rep.ArtworkUrl)
                ? null
                : await _r2.GenerateDownloadUrlAsync(rep.ArtworkUrl, cancellationToken);

            var metricKeys = rep.Template.Fields
                .Where(f => !f.IsCalculated)
                .OrderBy(f => f.SortOrder)
                .Select(f => f.Key)
                .ToArray();

            string? subcategory = rep.Template.Code switch
            {
                MetricTemplateCode.Edm when rep.EdmSubcategory is { } e => PlacementEnumNames.ToName(e),
                MetricTemplateCode.Education when rep.EducationSubcategory is { } ed => PlacementEnumNames.ToName(ed),
                _ => null,
            };

            var sendDates = members.Count > 1
                ? members
                    .Where(m => m.StartDate is { } s && PeriodWindow.Ord(s) >= fromOrd && PeriodWindow.Ord(s) <= toOrd)
                    .Select(m => m.StartDate!.Value.ToString("yyyy-MM-dd"))
                    .OrderBy(s => s)
                    .ToList()
                : new List<string>();

            return new DashboardPlacementDto(
                Id: rep.GroupId ?? rep.Id,
                Name: rep.Name,
                Objective: rep.Objective.ToString().ToLowerInvariant(),
                TemplateCode: PlacementEnumNames.ToName(rep.Template.Code),
                PublisherName: rep.Publisher.Name,
                PublisherSlug: rep.Publisher.Slug,
                IsBonus: rep.IsBonus,
                MediaCost: Costing(members).Sum(m => m.MediaCost),
                PlannedMediaCost: PlannedSum(members),
                ArtworkViewUrl: artworkViewUrl,
                LiveMonths: rep.LiveMonths,
                MetricKeys: metricKeys,
                Totals: cardTotals,
                Targets: targets,
                StartDate: rep.StartDate?.ToString("yyyy-MM-dd"),
                EndDate: rep.EndDate?.ToString("yyyy-MM-dd"),
                Subcategory: subcategory,
                SendDates: sendDates,
                Comments: rep.Comments);
        }

        var placementDtos = new List<DashboardPlacementDto>(placements.Count);
        var mergedGroups = new HashSet<Guid>();
        foreach (var p in placements)
        {
            if (p.GroupId.HasValue && p.Template.Code == MetricTemplateCode.Edm)
            {
                var key = p.GroupId.Value;
                if (!mergedGroups.Add(key)) continue; // group already emitted
                var members = placements
                    .Where(m => m.GroupId == key && m.Template.Code == MetricTemplateCode.Edm)
                    .OrderBy(m => m.StartDate ?? DateOnly.MaxValue)
                    .ToList();
                placementDtos.Add(await BuildCard(members[0], members));
            }
            else
            {
                placementDtos.Add(await BuildCard(p, new List<Placement> { p }));
            }
        }

        var period = new DashboardPeriodDto(
            From: PeriodWindow.ToYm(fromOrd),
            To: PeriodWindow.ToYm(toOrd),
            AvailableFrom: availFromOrd.HasValue ? PeriodWindow.ToYm(availFromOrd.Value) : null,
            AvailableTo: availToOrd.HasValue ? PeriodWindow.ToYm(availToOrd.Value) : null);

        return new DashboardResponse(
            Brand: new DashboardBrandDto(brand.Id, brand.Name, brand.Slug),
            Audience: new DashboardAudienceDto(audience.Id, audience.Name, audience.Slug),
            Period: period,
            Totals: totals,
            Monthly: monthly,
            Publishers: publishers,
            Placements: placementDtos,
            IsPlan: totals.Metrics.Count == 0);
    }

}
