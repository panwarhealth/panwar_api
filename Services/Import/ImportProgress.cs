using System.Collections.Concurrent;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Import;

public record ImportJobState(string Status, string? Message, ImportPreviewDto? Preview, string? Error);

public interface IImportProgress
{
    void Report(Guid jobId, string message);
    void Complete(Guid jobId, ImportPreviewDto preview);
    void Fail(Guid jobId, string error);
    ImportJobState? Get(Guid jobId);
}

// In-memory state for background preview jobs: the live status line while running,
// then the finished preview (or error). Polled by the frontend. Instance-local by
// design: on a scaled-out host a poll can miss and the UI just keeps waiting -
// acceptable for an internal admin tool, and it avoids a table for ephemeral state.
public class ImportProgress : IImportProgress
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    private sealed record Entry(string Status, string? Message, ImportPreviewDto? Preview, string? Error, DateTime At);

    private readonly ConcurrentDictionary<Guid, Entry> _jobs = new();

    public void Report(Guid jobId, string message)
    {
        if (jobId == Guid.Empty) return;
        _jobs[jobId] = new Entry("running", message, null, null, DateTime.UtcNow);
        Prune();
    }

    public void Complete(Guid jobId, ImportPreviewDto preview)
    {
        if (jobId == Guid.Empty) return;
        _jobs[jobId] = new Entry("done", null, preview, null, DateTime.UtcNow);
    }

    public void Fail(Guid jobId, string error)
    {
        if (jobId == Guid.Empty) return;
        _jobs[jobId] = new Entry("failed", null, null, error, DateTime.UtcNow);
    }

    public ImportJobState? Get(Guid jobId)
        => _jobs.TryGetValue(jobId, out var e) ? new ImportJobState(e.Status, e.Message, e.Preview, e.Error) : null;

    private void Prune()
    {
        foreach (var stale in _jobs.Where(kv => kv.Value.At < DateTime.UtcNow - MaxAge).Select(kv => kv.Key).ToList())
            _jobs.TryRemove(stale, out _);
    }
}
