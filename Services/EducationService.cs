using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

/// <inheritdoc />
public class EducationService : IEducationService
{
    private readonly AppDbContext _context;

    public EducationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<EducationPagesResponse> GetPagesAsync(Guid clientId, CancellationToken cancellationToken)
    {
        var pages = await _context.EducationPages
            .AsNoTracking()
            .Where(p => p.ClientId == clientId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => new EducationPageSummaryDto(p.Id, p.Name, p.Slug, p.SortOrder, p.Charts.Count))
            .ToListAsync(cancellationToken);

        return new EducationPagesResponse(pages);
    }

    public async Task<EducationPageResponse?> GetPageAsync(
        Guid clientId, string pageSlug, string? from, string? to, CancellationToken cancellationToken)
    {
        var page = await _context.EducationPages
            .AsNoTracking()
            .Include(p => p.Charts).ThenInclude(c => c.Series).ThenInclude(s => s.DataPoints)
            .Include(p => p.Charts).ThenInclude(c => c.Annotations)
            .FirstOrDefaultAsync(p => p.ClientId == clientId && p.Slug == pageSlug, cancellationToken);
        if (page is null) return null;

        return EducationMapper.Build(page, from, to);
    }
}
