using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public interface ITsSavedReportService
{
    Task<IReadOnlyList<TsSavedReportDto>> ListAsync(TsViewer viewer, CancellationToken cancellationToken = default);
    Task<TsSavedReportDto> CreateAsync(TsViewer viewer, TsSavedReportWriteRequest data, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(TsViewer viewer, Guid reportId, CancellationToken cancellationToken = default);
}

public class TsSavedReportService : ITsSavedReportService
{
    private const int MaxName = 200;
    private const int MaxQuery = 2000;

    private readonly AppDbContext _context;

    public TsSavedReportService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TsSavedReportDto>> ListAsync(TsViewer viewer, CancellationToken cancellationToken = default)
        => await _context.TsSavedReports.AsNoTracking()
            .Where(r => r.PersonId == viewer.PersonId)
            .OrderBy(r => r.Name)
            .Select(r => new TsSavedReportDto(r.Id, r.Name, r.Query, r.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<TsSavedReportDto> CreateAsync(TsViewer viewer, TsSavedReportWriteRequest data, CancellationToken cancellationToken = default)
    {
        var name = data.Name.Trim();
        if (name.Length == 0 || name.Length > MaxName) throw new TsValidationException("Give the report a name");
        if (data.Query.Length > MaxQuery) throw new TsValidationException("That report has too many filters to save");

        var report = new TsSavedReport
        {
            Id = Guid.NewGuid(),
            PersonId = viewer.PersonId,
            Name = name,
            Query = data.Query,
            CreatedAt = DateTime.UtcNow
        };
        _context.TsSavedReports.Add(report);
        await _context.SaveChangesAsync(cancellationToken);
        return new TsSavedReportDto(report.Id, report.Name, report.Query, report.CreatedAt);
    }

    public async Task<bool> DeleteAsync(TsViewer viewer, Guid reportId, CancellationToken cancellationToken = default)
        => await _context.TsSavedReports
            .Where(r => r.Id == reportId && r.PersonId == viewer.PersonId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
}
