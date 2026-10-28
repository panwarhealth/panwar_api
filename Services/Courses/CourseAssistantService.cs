using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services.Ai;

namespace Panwar.Api.Services.Courses;

public interface ICourseAssistantService
{
    Task<Guid?> StartTurnAsync(Guid courseId, CourseAssistantRequest request, CancellationToken cancellationToken = default);
    CourseAssistantJobState? GetTurn(Guid jobId);
}

public class CourseAssistantService : ICourseAssistantService
{
    private static readonly Dictionary<string, (decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite)> PricePerMillion = new()
    {
        ["claude-fable-5-1"] = (10m, 50m, 0.25m, 12.50m),
        ["claude-opus-5-5"] = (4m, 20m, 0.20m, 5m),
        ["claude-sonnet-5-5"] = (2m, 10m, 0.20m, 2.50m),
    };

    private readonly AppDbContext _context;
    private readonly IAnthropicService _anthropic;
    private readonly ICourseAssistantJobs _jobs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CourseAssistantService> _logger;
    private readonly string _model;
    private readonly string _effort;
    private readonly string? _logDir;

    public CourseAssistantService(
        AppDbContext context,
        IAnthropicService anthropic,
        ICourseAssistantJobs jobs,
        IServiceScopeFactory scopeFactory,
        ILogger<CourseAssistantService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _anthropic = anthropic;
        _jobs = jobs;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _model = configuration["COURSE_AI_MODEL"] ?? "claude-opus-5-5";
        _effort = configuration["COURSE_AI_EFFORT"] ?? "high";
        _logDir = configuration["COURSE_AI_LOG_DIR"];
    }

    public async Task<Guid?> StartTurnAsync(Guid courseId, CourseAssistantRequest request, CancellationToken cancellationToken = default)
    {
        if (!_anthropic.IsEnabled) throw new CourseValidationException("The AI assistant isn't set up on this server");
        if (!await _context.Courses.AnyAsync(c => c.Id == courseId, cancellationToken)) return null;

        if (request.System.ValueKind is not (JsonValueKind.String or JsonValueKind.Array))
            throw new CourseValidationException("system is required");
        if (request.Tools.ValueKind != JsonValueKind.Array) throw new CourseValidationException("tools must be a list");
        if (request.Messages.ValueKind != JsonValueKind.Array || request.Messages.GetArrayLength() == 0)
            throw new CourseValidationException("messages are required");

        var turn = new MessageTurnRequest(
            _model,
            _effort,
            JsonNode.Parse(request.System.GetRawText())!,
            JsonNode.Parse(request.Tools.GetRawText())!.AsArray(),
            JsonNode.Parse(request.Messages.GetRawText())!.AsArray());

        return _jobs.Start(async report =>
        {
            using var scope = _scopeFactory.CreateScope();
            var anthropic = scope.ServiceProvider.GetRequiredService<IAnthropicService>();
            try
            {
                var reply = await anthropic.CreateMessageAsync(turn, report, CancellationToken.None);
                if (reply["usage"] is JsonObject usage) AddCost(courseId, usage);
                await WriteLogAsync(courseId, turn.Messages, reply, null);
                return reply;
            }
            catch (Exception ex)
            {
                await WriteLogAsync(courseId, turn.Messages, null, ex.Message);
                throw;
            }
        });
    }

    private void AddCost(Guid courseId, JsonObject usage)
    {
        int Tokens(string key) => usage[key]?.GetValue<int>() ?? 0;
        var input = Tokens("input_tokens");
        var output = Tokens("output_tokens");
        var cacheRead = Tokens("cache_read_input_tokens");
        var cacheWrite = Tokens("cache_creation_input_tokens");
        decimal? cost = PricePerMillion.TryGetValue(_model, out var p)
            ? (input * p.Input + output * p.Output + cacheRead * p.CacheRead + cacheWrite * p.CacheWrite) / 1_000_000m
            : null;
        usage["cost_usd"] = cost;
        _logger.LogInformation(
            "Course assistant turn for {CourseId} on {Model}: {Input} in, {CacheRead} cache read, {CacheWrite} cache write, {Output} out, ${Cost}",
            courseId, _model, input, cacheRead, cacheWrite, output, cost);
    }

    private async Task WriteLogAsync(Guid courseId, JsonArray messages, JsonNode? reply, string? error)
    {
        if (string.IsNullOrWhiteSpace(_logDir)) return;
        var entry = new JsonObject
        {
            ["at"] = DateTime.UtcNow.ToString("O"),
            ["model"] = _model,
            ["effort"] = _effort,
            ["messages"] = messages.Count,
            ["sent"] = messages[^1]?.DeepClone(),
            ["stop_reason"] = reply?["stop_reason"]?.DeepClone(),
            ["usage"] = reply?["usage"]?.DeepClone(),
            ["reply"] = reply?["content"]?.DeepClone(),
            ["error"] = error,
        };
        try
        {
            Directory.CreateDirectory(_logDir);
            await File.AppendAllTextAsync(Path.Combine(_logDir, $"{courseId}.jsonl"), entry.ToJsonString() + "\n");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Couldn't write the course assistant log for {CourseId}", courseId);
        }
    }

    public CourseAssistantJobState? GetTurn(Guid jobId) => _jobs.Get(jobId);
}
