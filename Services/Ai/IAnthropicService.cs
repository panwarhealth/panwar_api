using System.Text.Json;
using System.Text.Json.Nodes;

namespace Panwar.Api.Services.Ai;

public sealed class AnthropicException : Exception
{
    public AnthropicException(string message) : base(message) { }
}

// A tool the model may call during a run. InputSchema is a JSON Schema object.
public sealed record AgentTool(string Name, string Description, JsonNode InputSchema);

// Executes a tool call and returns the text fed back to the model as the result.
public delegate Task<string> ToolExecutor(string toolName, JsonElement input, CancellationToken ct);

public sealed record AgentRunRequest(
    string System,
    string UserContent,
    IReadOnlyList<AgentTool> Tools,
    string TerminalToolName,   // when the model calls this tool the loop ends and its input is the answer
    ToolExecutor Executor,
    int MaxIterations = 8,
    Action<string>? OnStatus = null);  // live human-readable narration of what the model is doing

public sealed record AgentRunResult(
    JsonElement? Answer,       // the terminal tool's input, or null if the model never called it
    string TranscriptJson,     // full ordered messages incl. every tool_use + tool_result
    int InputTokens,
    int OutputTokens,
    int ToolCallCount,
    bool HitMaxIterations);

public interface IAnthropicService
{
    // False when no ANTHROPIC_API_KEY is configured - callers must no-op so the
    // import still works without the AI layer.
    bool IsEnabled { get; }

    string Model { get; }

    // Runs a tool-use agentic loop: the model may call the supplied tools (executed
    // via the caller's executor) and ends by calling the terminal tool. Returns the
    // terminal answer plus the full transcript and usage for auditing.
    Task<AgentRunResult> RunToolLoopAsync(AgentRunRequest request, CancellationToken ct);
}
