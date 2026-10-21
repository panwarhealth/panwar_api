using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public interface ITsTaskService
{
    Task<IReadOnlyList<TsTaskDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<TsTaskDto> CreateAsync(TsTaskWriteRequest data, CancellationToken cancellationToken = default);
    Task<TsTaskDto?> UpdateAsync(Guid taskId, TsTaskWriteRequest data, CancellationToken cancellationToken = default);
}

public class TsTaskService : ITsTaskService
{
    private const int MaxName = 200;

    private readonly AppDbContext _context;

    public TsTaskService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TsTaskDto>> ListAsync(CancellationToken cancellationToken = default)
        => await _context.TsTasks.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TsTaskDto(t.Id, t.Name, t.IsCommon, t.Archived))
            .ToListAsync(cancellationToken);

    public async Task<TsTaskDto> CreateAsync(TsTaskWriteRequest data, CancellationToken cancellationToken = default)
    {
        var task = new TsTask { Id = Guid.NewGuid(), Name = "", CreatedAt = DateTime.UtcNow };
        await ApplyAsync(task, data, cancellationToken);
        _context.TsTasks.Add(task);
        await _context.SaveChangesAsync(cancellationToken);
        return new TsTaskDto(task.Id, task.Name, task.IsCommon, task.Archived);
    }

    public async Task<TsTaskDto?> UpdateAsync(Guid taskId, TsTaskWriteRequest data, CancellationToken cancellationToken = default)
    {
        var task = await _context.TsTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null) return null;
        await ApplyAsync(task, data, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return new TsTaskDto(task.Id, task.Name, task.IsCommon, task.Archived);
    }

    private async Task ApplyAsync(TsTask task, TsTaskWriteRequest data, CancellationToken cancellationToken)
    {
        var name = data.Name.Trim();
        if (name.Length == 0 || name.Length > MaxName) throw new TsValidationException("Task name is required");
        if (await _context.TsTasks.AnyAsync(t => t.Id != task.Id && t.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new TsValidationException($"{name} already exists");

        task.Name = name;
        task.IsCommon = data.IsCommon && !data.Archived;
        task.Archived = data.Archived;
        task.UpdatedAt = DateTime.UtcNow;
    }
}
