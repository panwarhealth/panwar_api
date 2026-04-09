using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services;

/// <summary>
/// Builds the brand × audience dashboard payload from the live database.
/// One round trip pulls every placement for the requested brand × audience along
/// with its publisher, template, KPIs and (filtered) monthly actuals; the rest
/// is in-memory aggregation. With ~60 placements per dashboard and ~12 actuals
/// each this is well under a millisecond's work.
/// </summary>
public class DashboardService : IDashboardService
{
    // Reckitt 2025 is currently the only year of data we have. When multi-year
    // becomes a real requirement this turns into a request parameter with a
    // current-year default.
    private const int Year = 2025;

    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardResponse?> GetDashboardAsync(
        Guid clientId,
        string brandSlug,
        string audienceSlug,
        CancellationToken cancellationToken)
    {
        var brand = await _context.Brands
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.ClientId == clientId && b.Slug == brandSlug,
                cancellationToken);
        if (brand is null) return null;

        var audience = await _context.Audiences
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.ClientId == clientId && a.Slug == audienceSlug,
                cancellationToken);
        if (audience is null) return null;

        var placements = await _context.Placements
            .AsNoTracking()
            .Include(p => p.Publisher)
            .Include(p => p.Template)
            .Include(p => p.Kpis)
            .Include(p => p.Actuals.Where(a => a.Year == Year))
            .Where(p => p.BrandId == brand.Id && p.AudienceId == audience.Id)
            .OrderBy(p => p.Publisher.Name)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        // ── YTD totals across every placement ──────────────────────────────
        var totalsMetrics = new Dictionary<string, decimal>();
        foreach (var actual in placements.SelectMany(p => p.Actuals))
        {
            totalsMetrics.TryGetValue(actual.MetricKey, out var current);
            totalsMetrics[actual.MetricKey] = current + actual.Value;
        }

        var totals = new DashboardTotalsDto(
            PlacementCount: placements.Count,
            MediaCost: placements.Sum(p => p.MediaCost),
            Metrics: totalsMetrics);

        // ── Per-month rollup (months 1..12 always present, zero-filled) ────
        var monthly = new List<DashboardMonthDto>(12);
        for (var m = 1; m <= 12; m++)
        {
            var monthMetrics = new Dictionary<string, decimal>();
            foreach (var actual in placements.SelectMany(p => p.Actuals))
            {
                if (actual.Month != m) continue;
                monthMetrics.TryGetValue(actual.MetricKey, out var current);
                monthMetrics[actual.MetricKey] = current + actual.Value;
            }
            monthly.Add(new DashboardMonthDto(m, monthMetrics));
        }

        // ── Per-publisher rollup ───────────────────────────────────────────
        // Group by PublisherId (not the Publisher entity) because AsNoTracking
        // disables identity resolution, so two placements pointing at the same
        // publisher row would otherwise see distinct entity instances.
        var publishers = placements
            .GroupBy(p => p.PublisherId)
            .Select(g =>
            {
                var first = g.First().Publisher;
                var pubMetrics = new Dictionary<string, decimal>();
                foreach (var actual in g.SelectMany(p => p.Actuals))
                {
                    pubMetrics.TryGetValue(actual.MetricKey, out var current);
                    pubMetrics[actual.MetricKey] = current + actual.Value;
                }
                return new DashboardPublisherDto(
                    Id: first.Id,
                    Name: first.Name,
                    Slug: first.Slug,
                    PlacementCount: g.Count(),
                    MediaCost: g.Sum(p => p.MediaCost),
                    Metrics: pubMetrics);
            })
            .OrderByDescending(p => p.MediaCost)
            .ThenBy(p => p.Name)
            .ToList();

        // ── Per-placement detail ───────────────────────────────────────────
        var placementDtos = placements
            .Select(p =>
            {
                var pTotals = new Dictionary<string, decimal>();
                foreach (var actual in p.Actuals)
                {
                    pTotals.TryGetValue(actual.MetricKey, out var current);
                    pTotals[actual.MetricKey] = current + actual.Value;
                }
                var targets = p.Kpis.ToDictionary(k => k.MetricKey, k => k.TargetValue);
                return new DashboardPlacementDto(
                    Id: p.Id,
                    Name: p.Name,
                    Objective: p.Objective.ToString().ToLowerInvariant(),
                    TemplateCode: TemplateCodeToString(p.Template.Code),
                    PublisherName: p.Publisher.Name,
                    PublisherSlug: p.Publisher.Slug,
                    IsBonus: p.IsBonus,
                    IsCpdPackage: p.IsCpdPackage,
                    MediaCost: p.MediaCost,
                    LiveMonths: p.LiveMonths,
                    Totals: pTotals,
                    Targets: targets);
            })
            .ToList();

        return new DashboardResponse(
            Brand: new DashboardBrandDto(brand.Id, brand.Name, brand.Slug),
            Audience: new DashboardAudienceDto(audience.Id, audience.Name, audience.Slug),
            Year: Year,
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
