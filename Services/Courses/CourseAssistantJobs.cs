using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Panwar.Api.Services.Courses;

public record CourseAssistantJobState(string Status, string? Message, JsonNode? Reply, string? Error);

public interface ICourseAssistantJobs
{
    Guid Start(Func<Action<string>, Task<JsonNode>> work);
    CourseAssistantJobState? Get(Guid jobId);
}

public class CourseAssistantJobs : ICourseAssistantJobs
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private sealed record Entry(CourseAssistantJobState State, DateTime At);

    private readonly ConcurrentDictionary<Guid, Entry> _jobs = new();

    public Guid Start(Func<Action<string>, Task<JsonNode>> work)
    {
        Prune();
        var jobId = Guid.NewGuid();
        Set(jobId, new CourseAssistantJobState("running", null, null, null));
        _ = Task.Run(async () =>
        {
            try
            {
                var reply = await work(message => Set(jobId, new CourseAssistantJobState("running", message, null, null)));
                Set(jobId, new CourseAssistantJobState("done", null, reply, null));
            }
            catch (Exception ex)
            {
                Set(jobId, new CourseAssistantJobState("failed", null, null, ex.Message));
            }
        });
        return jobId;
    }

    public CourseAssistantJobState? Get(Guid jobId) => _jobs.TryGetValue(jobId, out var e) ? e.State : null;

    private void Set(Guid jobId, CourseAssistantJobState state) => _jobs[jobId] = new Entry(state, DateTime.UtcNow);

    private void Prune()
    {
        foreach (var stale in _jobs.Where(kv => kv.Value.At < DateTime.UtcNow - MaxAge).Select(kv => kv.Key).ToList())
            _jobs.TryRemove(stale, out _);
    }
}
