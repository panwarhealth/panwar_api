using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panwar.Api.Data;
using Panwar.Api.Models;

namespace Panwar.Api.Services.Courses;

public record CourseAssistantJobState(string Status, string? Message, JsonNode? Reply, string? Error);

public interface ICourseAssistantJobs
{
    Task<Guid> StartAsync(Func<Action<string>, Task<JsonNode>> work, CancellationToken cancellationToken = default);
    Task<CourseAssistantJobState?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);
}

public class CourseAssistantJobs : ICourseAssistantJobs
{
    private const string Running = "running";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    private static readonly TimeSpan Stalled = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;

    public CourseAssistantJobs(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<Guid> StartAsync(Func<Action<string>, Task<JsonNode>> work, CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.CourseAssistantJobs.Where(j => j.CreatedAt < now - MaxAge).ExecuteDeleteAsync(cancellationToken);
            db.CourseAssistantJobs.Add(new CourseAssistantJob { Id = jobId, Status = Running, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(cancellationToken);
        }

        _ = Task.Run(async () =>
        {
            var lastProgress = DateTime.MinValue;
            try
            {
                var reply = await work(message =>
                {
                    if (DateTime.UtcNow - lastProgress < ProgressEvery) return;
                    lastProgress = DateTime.UtcNow;
                    _ = FinishAsync(jobId, Running, message, null, null);
                });
                await FinishAsync(jobId, "done", null, reply.ToJsonString(), null);
            }
            catch (Exception ex)
            {
                await FinishAsync(jobId, "failed", null, null, ex.Message);
            }
        });
        return jobId;
    }

    public async Task<CourseAssistantJobState?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.CourseAssistantJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null) return null;
        if (job.Status == Running && job.UpdatedAt < DateTime.UtcNow - Stalled)
            return new CourseAssistantJobState("failed", null, null, "The server stopped while working on this. Try again.");
        return new CourseAssistantJobState(job.Status, job.Message, job.Reply is null ? null : JsonNode.Parse(job.Reply), job.Error);
    }

    private async Task FinishAsync(Guid jobId, string status, string? message, string? reply, string? error)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.CourseAssistantJobs
            .Where(j => j.Id == jobId && j.Status == Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, status)
                .SetProperty(j => j.Message, message)
                .SetProperty(j => j.Reply, reply)
                .SetProperty(j => j.Error, error)
                .SetProperty(j => j.UpdatedAt, DateTime.UtcNow));
    }
}
