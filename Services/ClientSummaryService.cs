using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.CloudflareR2;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services;

public class ClientSummaryService : IClientSummaryService
{
    private const int FallbackYear = 2025;

    private readonly AppDbContext _context;
    private readonly ICloudflareR2Service _r2;

    public ClientSummaryService(AppDbContext context, ICloudflareR2Service r2)
    {
        _context = context;
        _r2 = r2;
    }

    public async Task<ClientSummaryResponse?> GetSummaryAsync(
        Guid clientId,
        string? from,
        string? to,
        string? brandSlug,
        string? audienceSlug,
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
            .Include(p => p.Template).ThenInclude(t => t.Fields)
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
        if (!string.IsNullOrWhiteSpace(audienceSlug))
            placements = placements.Where(p => p.Audience.Slug == audienceSlug).ToList();

        int fromYear = fromOrd / 12, toYear = toOrd / 12;
        var cpdInvestments = await _context.CpdInvestments.AsNoTracking()
            .Where(c => c.Brand.ClientId == clientId && c.Year >= fromYear && c.Year <= toYear)
            .Where(c => string.IsNullOrWhiteSpace(brandSlug) || c.Brand.Slug == brandSlug)
            .Where(c => string.IsNullOrWhiteSpace(audienceSlug) || c.Audience.Slug == audienceSlug)
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

        var audienceRank = placements
            .GroupBy(p => p.AudienceId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.First().Audience.Name)
            .Select((g, i) => (g.Key, i))
            .ToDictionary(x => x.Key, x => x.i);

        var byPublisher = placements
            .GroupBy(p => p.PublisherId)
            .Select(g =>
            {
                var list = g.ToList();
                var dominantAudience = list
                    .GroupBy(p => p.AudienceId)
                    .OrderByDescending(a => a.Count())
                    .ThenBy(a => audienceRank[a.Key])
                    .First().Key;
                return new
                {
                    AudienceOrder = audienceRank[dominantAudience],
                    Row = new SummaryRowDto(
                        Label: g.First().Publisher.Name,
                        BrandSlug: null,
                        AudienceSlug: null,
                        PlacementCount: list.Count,
                        MediaCost: Costing(list).Sum(p => p.MediaCost),
                        PlannedMediaCost: PlannedSum(list),
                        CpdInvestmentCost: cpdByPublisher.GetValueOrDefault(g.Key, 0m),
                        Metrics: WindowMetrics(list),
                        TargetMetrics: Targets(list)),
                };
            })
            .OrderBy(x => x.AudienceOrder)
            .ThenBy(x => x.Row.Label)
            .Select(x => x.Row)
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
            .OrderBy(r => CategoryRank(r.Label))
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
            .OrderBy(r => DigitalFormatRank(r.Label))
            .ThenBy(r => r.Label)
            .ToList();

        var brands = placements
            .Select(p => p.Brand)
            .DistinctBy(b => b.Id)
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Name)
            .Select(b => new BrandRefDto(b.Slug, b.Name, b.Color))
            .ToList();

        var byAsset = placements
            .Select(p => new AssetRowDto(
                Name: p.Name,
                BrandName: p.Brand.Name,
                BrandSlug: p.Brand.Slug,
                AudienceName: p.Audience.Name,
                AudienceSlug: p.Audience.Slug,
                PublisherName: p.Publisher.Name,
                PublisherSlug: p.Publisher.Slug,
                Objective: p.Objective.ToString(),
                TemplateCode: PlacementEnumNames.ToName(p.Template.Code),
                MediaType: MediaTypeOf(p.Template.Code, p.EdmSubcategory),
                OsCode: p.OsCode,
                LiveMonths: p.LiveMonths,
                StartDate: p.StartDate?.ToString("yyyy-MM-dd"),
                EndDate: p.EndDate?.ToString("yyyy-MM-dd"),
                SendDates: (p.SendDates.Length > 0 ? p.SendDates : (p.StartDate is { } sd ? new[] { sd } : Array.Empty<DateOnly>()))
                    .Where(d => PeriodWindow.Ord(d) >= fromOrd && PeriodWindow.Ord(d) <= toOrd)
                    .OrderBy(d => d)
                    .Select(d => d.ToString("yyyy-MM-dd"))
                    .ToList(),
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

        var placementDtos = new List<DashboardPlacementDto>();
        if (!string.IsNullOrWhiteSpace(brandSlug))
        {
            async Task<DashboardPlacementDto> BuildCard(Placement rep, List<Placement> members)
            {
                var cardTotals = new Dictionary<string, decimal>();
                foreach (var a in members.SelectMany(m => m.Actuals).Where(InWindow)) Add(cardTotals, a.MetricKey, a.Value);
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

                var sendDates = members
                    .SelectMany(m => m.SendDates.Length > 0
                        ? m.SendDates
                        : (m.StartDate is { } s ? new[] { s } : Array.Empty<DateOnly>()))
                    .Where(d => PeriodWindow.Ord(d) >= fromOrd && PeriodWindow.Ord(d) <= toOrd)
                    .Distinct()
                    .OrderBy(d => d)
                    .Select(d => d.ToString("yyyy-MM-dd"))
                    .ToList();

                var months = new List<PlacementMonthDto>();
                for (var ord = fromOrd; ord <= toOrd; ord++)
                {
                    var year = ord / 12;
                    var month = ord % 12 + 1;
                    var metrics = new Dictionary<string, decimal>();
                    foreach (var a in members.SelectMany(m => m.Actuals).Where(a => a.Year == year && a.Month == month))
                        Add(metrics, a.MetricKey, a.Value);
                    var targets = new Dictionary<string, decimal>();
                    foreach (var m in members)
                    {
                        var fraction = PeriodWindow.TargetFraction(m, ord, ord);
                        if (fraction <= 0) continue;
                        foreach (var k in m.Kpis) Add(targets, k.MetricKey, k.TargetValue * fraction);
                    }
                    if (metrics.Count == 0 && targets.Count == 0) continue;
                    months.Add(new PlacementMonthDto(year, month, metrics, targets));
                }

                return new DashboardPlacementDto(
                    Id: rep.GroupId ?? rep.Id,
                    Name: rep.Name,
                    Objective: rep.Objective.ToString().ToLowerInvariant(),
                    TemplateCode: PlacementEnumNames.ToName(rep.Template.Code),
                    MediaType: MediaTypeOf(rep.Template.Code, rep.EdmSubcategory),
                    PublisherName: rep.Publisher.Name,
                    PublisherSlug: rep.Publisher.Slug,
                    AudienceName: rep.Audience.Name,
                    AudienceSlug: rep.Audience.Slug,
                    OsCode: rep.OsCode,
                    IsBonus: rep.IsBonus,
                    MediaCost: Costing(members).Sum(m => m.MediaCost),
                    PlannedMediaCost: PlannedSum(members),
                    ArtworkViewUrl: artworkViewUrl,
                    LiveMonths: rep.LiveMonths,
                    MetricKeys: metricKeys,
                    Totals: cardTotals,
                    Targets: Targets(members),
                    StartDate: rep.StartDate?.ToString("yyyy-MM-dd"),
                    EndDate: rep.EndDate?.ToString("yyyy-MM-dd"),
                    Subcategory: subcategory,
                    SendDates: sendDates,
                    Comments: rep.Comments,
                    Months: months);
            }

            var ordered = placements
                .OrderBy(p => p.Audience.Name)
                .ThenByDescending(p => p.MediaCost)
                .ThenBy(p => p.Name)
                .ToList();
            var mergedGroups = new HashSet<Guid>();
            foreach (var p in ordered)
            {
                if (p.GroupId.HasValue && p.Template.Code == MetricTemplateCode.Edm)
                {
                    var key = p.GroupId.Value;
                    if (!mergedGroups.Add(key)) continue;
                    var members = ordered
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
            ByAsset: byAsset,
            Placements: placementDtos);
    }

    private static string CategoryOf(MetricTemplateCode code) => code switch
    {
        MetricTemplateCode.Print => "Print",
        MetricTemplateCode.Education => "Education",
        _ => "Digital",
    };

    private static string MediaTypeOf(MetricTemplateCode code, EdmSubcategory? edm) =>
        DigitalFormatOf(code, edm) ?? CategoryOf(code);

    private static readonly string[] DigitalFormatOrder =
        { "Digital Display", "Solus eDM", "Sponsored eDM Tile", "Sponsored Article" };

    private static int DigitalFormatRank(string label)
    {
        var i = Array.IndexOf(DigitalFormatOrder, label);
        return i < 0 ? DigitalFormatOrder.Length : i;
    }

    private static int CategoryRank(string label) => label switch
    {
        "Print" => 0,
        "Digital" => 1,
        _ => 2,
    };

    private static string? DigitalFormatOf(MetricTemplateCode code, EdmSubcategory? edm) => code switch
    {
        MetricTemplateCode.DigitalDisplay => "Digital Display",
        MetricTemplateCode.SponsoredContent => "Sponsored Article",
        MetricTemplateCode.Edm => edm switch
        {
            EdmSubcategory.Solus => "Solus eDM",
            EdmSubcategory.SponsoredContent => "Sponsored eDM Tile",
            EdmSubcategory.Banner => "eDM Banner",
            _ => "eDM",
        },
        _ => null,
    };
}
