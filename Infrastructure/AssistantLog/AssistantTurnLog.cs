using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Panwar.Api.Infrastructure.AssistantLog;

public interface IAssistantTurnLog
{
    Task WriteAsync(Guid courseId, JsonObject entry);
}

public class FileAssistantTurnLog : IAssistantTurnLog
{
    private readonly string? _directory;
    private readonly ILogger<FileAssistantTurnLog> _logger;

    public FileAssistantTurnLog(IConfiguration configuration, ILogger<FileAssistantTurnLog> logger)
    {
        _directory = configuration["COURSE_AI_LOG_DIR"];
        _logger = logger;
    }

    public async Task WriteAsync(Guid courseId, JsonObject entry)
    {
        if (string.IsNullOrWhiteSpace(_directory)) return;
        try
        {
            Directory.CreateDirectory(_directory);
            await File.AppendAllTextAsync(Path.Combine(_directory, $"{courseId}.jsonl"), entry.ToJsonString() + "\n");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Couldn't write the course assistant log for {CourseId}", courseId);
        }
    }
}
