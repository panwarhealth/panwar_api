namespace Panwar.Api.Models;

/// <summary>
/// One row per real AI reconciliation run. Records the full tool-call transcript,
/// every cell the AI was served, the grounding of every value it cited, the
/// deterministic verification result, token cost, and (filled at commit) a
/// proposed-vs-actual outcome - so we can prove after the fact that it did exactly
/// what we required and pulled only real data.
/// </summary>
public class ImportAiLog
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public required string FileName { get; set; }
    public required string ContentHash { get; set; }
    public required string Model { get; set; }
    public Guid? RequestedByUserId { get; set; }

    public required string SystemPrompt { get; set; }
    public required string TranscriptJson { get; set; }      // jsonb - full ordered messages
    public string? AnswerJson { get; set; }                  // jsonb - the terminal tool input (raw answer)
    public required string VerificationJson { get; set; }    // jsonb - per-suggestion pass/fail + reason
    public required string CellsReadJson { get; set; }       // jsonb - every coordinate+value served to the AI
    public required string GroundingJson { get; set; }       // jsonb - per cited value: claimed vs actual, verdict
    public string? OutcomeJson { get; set; }                 // jsonb - proposed-vs-actual, filled at commit

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ToolCallCount { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}
