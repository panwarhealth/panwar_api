using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.CloudflareR2;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services;

/// <summary>
/// Builds the brand × audience dashboard payload from the live database, scoped
/// to a month window (the global date filter). One round trip pulls every
/// placement for the brand × audience with its publisher, template, KPIs and
/// monthly actuals; the rest is in-memory aggregation. Artwork view URLs are
/// minted per placement (cheap local presigning).
/// </summary>
public class DashboardService : IDashboardService
{
    // Fallback window only when a brand × audience has no actuals at all.
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

        // ── Resolve the month window ───────────────────────────────────────
        // Available span covers actuals AND placement live periods so a planned
        // future year is selectable before any results land. The default window
        // stays the latest year with ACTUALS (results, not plan), falling back to
        // the latest planned year.
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

        // Presence set: placements whose metrics fall in the window (education
        // ranges overlap; eDM sends land on a month; others by reporting year).
        placements = placements.Where(p => PeriodWindow.AppearsInWindow(p, fromOrd, toOrd)).ToList();
        var allActuals = placements.SelectMany(p => p.Actuals).ToList();

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

        // Annual KPI targets are pro-rated to the window per the placement's date
        // shape, so partial windows compare like-for-like with windowed actuals.
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

        // Cost belongs to the booking year, so only cost-counting members
        // contribute spend even when their metrics show across years.
        IEnumerable<Placement> Costing(IEnumerable<Placement> ps) =>
            ps.Where(p => PeriodWindow.CostsCountInWindow(p, fromOrd, toOrd));
        decimal? PlannedSum(IEnumerable<Placement> ps)
        {
            var costing = Costing(ps).ToList();
            return costing.Any(p => p.PlannedMediaCost.HasValue) ? costing.Sum(p => p.PlannedMediaCost ?? 0) : null;
        }

        // ── Totals (windowed actuals + pro-rated KPI targets) ──────────────
        var totalsMetrics = new Dictionary<string, decimal>();
        foreach (var a in allActuals.Where(InWindow)) Add(totalsMetrics, a.MetricKey, a.Value);
        var targetMetrics = WindowTargets(placements);

        var totals = new DashboardTotalsDto(
            PlacementCount: placements.Count,
            MediaCost: Costing(placements).Sum(p => p.MediaCost),
            PlannedMediaCost: PlannedSum(placements),
            CpdInvestmentCost: Costing(placements).Sum(p => p.CpdInvestmentCost ?? 0),
            Metrics: totalsMetrics,
            TargetMetrics: targetMetrics);

        // ── Per-month rollup (only months inside the window) ───────────────
        var monthly = new List<DashboardMonthDto>();
        for (var o = fromOrd; o <= toOrd; o++)
        {
            int year = o / 12, month = o % 12 + 1;
            var mm = new Dictionary<string, decimal>();
            foreach (var a in allActuals)
                if (a.Year == year && a.Month == month) Add(mm, a.MetricKey, a.Value);
            monthly.Add(new DashboardMonthDto(year, month, mm));
        }

        // ── Per-publisher rollup ───────────────────────────────────────────
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
                    CpdInvestmentCost: Costing(g).Sum(p => p.CpdInvestmentCost ?? 0),
                    Metrics: pm,
                    TargetMetrics: tm);
            })
            .OrderByDescending(p => p.MediaCost)
            .ThenBy(p => p.Name)
            .ToList();

        // ── Per-placement detail (with presigned artwork) ──────────────────
        // Duplicated eDM sends (same GroupId) merge into one card: summed
        // actuals/targets/costs and the list of in-window send dates. Other
        // templates and singleton groups render one card each.
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
                TemplateCode: TemplateCodeToString(rep.Template.Code),
                PublisherName: rep.Publisher.Name,
                PublisherSlug: rep.Publisher.Slug,
                IsBonus: rep.IsBonus,
                IsCpdPackage: rep.IsCpdPackage,
                MediaCost: Costing(members).Sum(m => m.MediaCost),
                PlannedMediaCost: PlannedSum(members),
                CpdInvestmentCost: Costing(members).Sum(m => m.CpdInvestmentCost ?? 0) is var cpd && cpd > 0 ? cpd : (decimal?)null,
                ArtworkViewUrl: artworkViewUrl,
                LiveMonths: rep.LiveMonths,
                MetricKeys: metricKeys,
                Totals: cardTotals,
                Targets: targets,
                StartDate: rep.StartDate?.ToString("yyyy-MM-dd"),
                EndDate: rep.EndDate?.ToString("yyyy-MM-dd"),
                Subcategory: subcategory,
                SendDates: sendDates);
        }

        var placementDtos = new List<DashboardPlacementDto>(placements.Count);
        var mergedGroups = new HashSet<Guid>();
        foreach (var p in placements)
        {
            // Only eDM duplicates merge; everything else renders one card each.
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

    private static string TemplateCodeToString(MetricTemplateCode code) => code switch
    {
        MetricTemplateCode.DigitalDisplay => "digital_display",
        MetricTemplateCode.Edm => "edm",
        MetricTemplateCode.Print => "print",
        MetricTemplateCode.SponsoredContent => "sponsored_content",
        MetricTemplateCode.Education => "education",
        _ => code.ToString().ToLowerInvariant(),
    };
}
