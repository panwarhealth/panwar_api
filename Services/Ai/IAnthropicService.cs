namespace Panwar.Api.Services.Ai;

public sealed class AnthropicException : Exception
{
    public AnthropicException(string message) : base(message) { }
}

public interface IAnthropicService
{
    // False when no ANTHROPIC_API_KEY is configured - callers must no-op so the
    // import still works without the AI layer.
    bool IsEnabled { get; }

    // Sends one message and returns the assistant's text. When jsonSchema is
    // supplied the response is constrained to that schema (valid JSON text).
    Task<string> CompleteAsync(string system, string userContent, object? jsonSchema, CancellationToken ct);
}
