using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

public class EducationService : IEducationService
{
    private readonly AppDbContext _context;

    public EducationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<EducationPagesResponse> GetPagesAsync(
        Guid clientId, string? from, string? to, CancellationToken cancellationToken)
    {
        var hasFrom = PeriodWindow.TryParse(from, out var fromOrd);
        var hasTo = PeriodWindow.TryParse(to, out var toOrd);
        var windowed = hasFrom && hasTo;
        if (windowed && toOrd < fromOrd) (fromOrd, toOrd) = (toOrd, fromOrd);

        var pages = await _context.EducationPages
            .AsNoTracking()
            .Where(p => p.ClientId == clientId)
            .Where(p => !windowed
                || p.Assets.SelectMany(a => a.Values)
                    .Any(v => v.Year * 12 + v.Month - 1 >= fromOrd && v.Year * 12 + v.Month - 1 <= toOrd))
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => new EducationPageSummaryDto(
                p.Id, p.Name, p.Slug, p.SortOrder,
                p.Charts.Count,
                p.Assets.Count,
                p.Assets.SelectMany(a => a.Values)
                    .Where(v => v.Status.ToLower().Contains("complet")
                        && (!windowed
                            || (v.Year * 12 + v.Month - 1 >= fromOrd && v.Year * 12 + v.Month - 1 <= toOrd)))
                    .Sum(v => (decimal?)v.Value) ?? 0))
            .ToListAsync(cancellationToken);

        return new EducationPagesResponse(pages);
    }

    public async Task<EducationPageResponse?> GetPageAsync(
        Guid clientId, string pageSlug, string? from, string? to, CancellationToken cancellationToken)
    {
        var page = await _context.EducationPages
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Charts).ThenInclude(c => c.Annotations)
            .Include(p => p.Assets).ThenInclude(a => a.Values)
            .FirstOrDefaultAsync(p => p.ClientId == clientId && p.Slug == pageSlug, cancellationToken);
        if (page is null) return null;

        var brands = await _context.Brands.AsNoTracking()
            .Where(b => b.ClientId == clientId)
            .ToListAsync(cancellationToken);
        return EducationMapper.Build(page, brands, from, to, defaultLatestYear: true);
    }
}
