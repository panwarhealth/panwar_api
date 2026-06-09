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
            .Include(p => p.Template)
            .Include(p => p.Kpis)
            .Include(p => p.Actuals)
            .Where(p => p.BrandId == brand.Id && p.AudienceId == audience.Id)
            .OrderBy(p => p.Publisher.Name)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        // ── Resolve the month window ───────────────────────────────────────
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

        // ── Totals (windowed actuals; period-level KPI targets) ────────────
        var totalsMetrics = new Dictionary<string, decimal>();
        foreach (var a in allActuals.Where(InWindow)) Add(totalsMetrics, a.MetricKey, a.Value);
        var targetMetrics = new Dictionary<string, decimal>();
        foreach (var k in placements.SelectMany(p => p.Kpis)) Add(targetMetrics, k.MetricKey, k.TargetValue);

        var totals = new DashboardTotalsDto(
            PlacementCount: placements.Count,
            MediaCost: placements.Sum(p => p.MediaCost),
            PlannedMediaCost: placements.Any(p => p.PlannedMediaCost.HasValue) ? placements.Sum(p => p.PlannedMediaCost ?? 0) : null,
            CpdInvestmentCost: placements.Sum(p => p.CpdInvestmentCost ?? 0),
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
                var tm = new Dictionary<string, decimal>();
                foreach (var k in g.SelectMany(p => p.Kpis)) Add(tm, k.MetricKey, k.TargetValue);
                return new DashboardPublisherDto(
                    Id: first.Id,
                    Name: first.Name,
                    Slug: first.Slug,
                    PlacementCount: g.Count(),
                    MediaCost: g.Sum(p => p.MediaCost),
                    PlannedMediaCost: g.Any(p => p.PlannedMediaCost.HasValue) ? g.Sum(p => p.PlannedMediaCost ?? 0) : null,
                    CpdInvestmentCost: g.Sum(p => p.CpdInvestmentCost ?? 0),
                    Metrics: pm,
                    TargetMetrics: tm);
            })
            .OrderByDescending(p => p.MediaCost)
            .ThenBy(p => p.Name)
            .ToList();

        // ── Per-placement detail (with presigned artwork) ──────────────────
        var placementDtos = new List<DashboardPlacementDto>(placements.Count);
        foreach (var p in placements)
        {
            var pTotals = new Dictionary<string, decimal>();
            foreach (var a in p.Actuals.Where(InWindow)) Add(pTotals, a.MetricKey, a.Value);
            var targets = p.Kpis.ToDictionary(k => k.MetricKey, k => k.TargetValue);
            string? artworkViewUrl = string.IsNullOrWhiteSpace(p.ArtworkUrl)
                ? null
                : await _r2.GenerateDownloadUrlAsync(p.ArtworkUrl, cancellationToken);

            placementDtos.Add(new DashboardPlacementDto(
                Id: p.Id,
                Name: p.Name,
                Objective: p.Objective.ToString().ToLowerInvariant(),
                TemplateCode: TemplateCodeToString(p.Template.Code),
                PublisherName: p.Publisher.Name,
                PublisherSlug: p.Publisher.Slug,
                IsBonus: p.IsBonus,
                IsCpdPackage: p.IsCpdPackage,
                MediaCost: p.MediaCost,
                PlannedMediaCost: p.PlannedMediaCost,
                CpdInvestmentCost: p.CpdInvestmentCost,
                ArtworkViewUrl: artworkViewUrl,
                LiveMonths: p.LiveMonths,
                Totals: pTotals,
                Targets: targets));
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
            Placements: placementDtos);
    }

    private static string TemplateCodeToString(MetricTemplateCode code) => code switch
    {
        MetricTemplateCode.DigitalDisplay => "digital_display",
        MetricTemplateCode.Edm => "edm",
        MetricTemplateCode.Print => "print",
        MetricTemplateCode.SponsoredContent => "sponsored_content",
        MetricTemplateCode.EducationVideo => "education_video",
        MetricTemplateCode.EducationCourse => "education_course",
        _ => code.ToString().ToLowerInvariant(),
    };
}
