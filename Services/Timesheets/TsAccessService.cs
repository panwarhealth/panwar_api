using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;

namespace Panwar.Api.Services.Timesheets;

public interface ITsAccessService
{
    Task<bool> CanSeePersonAsync(TsViewer viewer, Guid personId, CancellationToken cancellationToken = default);
    Task<bool> CanWriteAsync(TsViewer viewer, Guid personId, Guid jobId, CancellationToken cancellationToken = default);
    IQueryable<TsEntry> Visible(TsViewer viewer, IQueryable<TsEntry> entries);
    IQueryable<TsPerson> VisiblePeople(TsViewer viewer, IQueryable<TsPerson> people);
}

public class TsAccessService : ITsAccessService
{
    private readonly AppDbContext _context;

    public TsAccessService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> CanSeePersonAsync(TsViewer viewer, Guid personId, CancellationToken cancellationToken = default)
        => personId == viewer.PersonId || viewer.IsAdmin
            || (viewer.IsManager && await ManagedPeople(viewer).AnyAsync(id => id == personId, cancellationToken));

    public async Task<bool> CanWriteAsync(TsViewer viewer, Guid personId, Guid jobId, CancellationToken cancellationToken = default)
        => await CanSeePersonAsync(viewer, personId, cancellationToken)
            || (viewer.IsManager && await ManagedJobs(viewer).AnyAsync(id => id == jobId, cancellationToken));

    public IQueryable<TsEntry> Visible(TsViewer viewer, IQueryable<TsEntry> entries)
    {
        if (viewer.IsAdmin) return entries;
        if (!viewer.IsManager) return entries.Where(e => e.PersonId == viewer.PersonId);
        var people = ManagedPeople(viewer);
        var jobs = ManagedJobs(viewer);
        return entries.Where(e => e.PersonId == viewer.PersonId || people.Contains(e.PersonId) || jobs.Contains(e.JobId));
    }

    public IQueryable<TsPerson> VisiblePeople(TsViewer viewer, IQueryable<TsPerson> people)
    {
        if (viewer.IsAdmin) return people;
        var managed = ManagedPeople(viewer);
        return people.Where(p => p.Id == viewer.PersonId || (viewer.IsManager && managed.Contains(p.Id)));
    }

    private IQueryable<Guid> ManagedPeople(TsViewer viewer)
        => _context.TsPersonManagers.Where(m => m.ManagerId == viewer.PersonId).Select(m => m.PersonId);

    private IQueryable<Guid> ManagedJobs(TsViewer viewer)
        => _context.TsJobPeople.Where(p => p.PersonId == viewer.PersonId && p.IsManager).Select(p => p.JobId);
}
