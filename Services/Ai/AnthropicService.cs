using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Panwar.Api.Services.Ai;

// First Anthropic integration in this codebase. Mirrors GraphService: raw HttpClient
// via IHttpClientFactory, config-driven, System.Text.Json, typed exception. Runs a
// hand-rolled tool-use loop (no SDK) so the model can read workbook tabs on demand.
// Responses are STREAMED so long generations can be narrated live via OnStatus
// (the final answer for a big file takes minutes - a buffered call looks frozen).
public class AnthropicService : IAnthropicService
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    // Sonnet 5 runs adaptive thinking by default whenever "thinking" is omitted (unlike
    // Opus 4.7/4.8, where omitting it means no thinking at all) - so we make it explicit
    // and opt into a visible summary for the audit transcript. max_tokens is a hard cap on
    // thinking + response combined; the final submit_result JSON for a 21-block file plus
    // its thinking has blown 16k in practice, so give it real room.
    private const int MaxTokens = 32000;

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AnthropicService> _logger;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly bool _thinkingOff;
    private readonly string _effort;

    public AnthropicService(IConfiguration configuration, IHttpClientFactory httpFactory, ILogger<AnthropicService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _apiKey = configuration["ANTHROPIC_API_KEY"];
        _model = configuration["ANTHROPIC_MODEL"] ?? "claude-sonnet-5";
        // Config-flippable so thinking/effort A-B tests don't need a rebuild. Evidence so
        // far on this task: effort "high" with thinking overthought its way through the
        // whole 32k budget twice without finishing; "medium" completed. Sonnet 5 runs
        // adaptive thinking even when the field is omitted, so "off" must be explicit.
        _thinkingOff = string.Equals(configuration["ANTHROPIC_THINKING"], "off", StringComparison.OrdinalIgnoreCase);
        _effort = configuration["ANTHROPIC_EFFORT"] ?? "medium";
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);

    public string Model => _model;

    public async Task<AgentRunResult> RunToolLoopAsync(AgentRunRequest request, CancellationToken ct)
    {
        if (!IsEnabled) throw new AnthropicException("ANTHROPIC_API_KEY is not configured");

        var tools = new JsonArray();
        foreach (var t in request.Tools)
            tools.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = t.InputSchema.DeepClone(),
                // Without this the API buffers a tool call's JSON and delivers it in one
                // burst at the end - the long submit_result write would stream nothing,
                // so there'd be nothing to narrate for minutes.
                ["eager_input_streaming"] = true,
            });

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = request.UserContent },
        };

        int inTokens = 0, outTokens = 0, toolCalls = 0;
        JsonElement? answer = null;
        bool hitMax = false;

        var http = _httpFactory.CreateClient();
        // Headers-only timeout: the body is streamed, so a healthy long generation keeps
        // delivering deltas well past any fixed budget; overall control is the caller's ct.
        http.Timeout = TimeSpan.FromMinutes(2);

        for (int iter = 0; iter < request.MaxIterations; iter++)
        {
            var body = new JsonObject
            {
                ["model"] = _model,
                ["max_tokens"] = MaxTokens,
                ["stream"] = true,
                ["system"] = request.System,
                ["thinking"] = _thinkingOff
                    ? new JsonObject { ["type"] = "disabled" }
                    : new JsonObject { ["type"] = "adaptive", ["display"] = "summarized" },
                ["output_config"] = new JsonObject { ["effort"] = _effort },
                ["tools"] = tools.DeepClone(),
                ["messages"] = messages.DeepClone(),
            };

            var root = await SendStreamingAsync(http, body, request.TerminalToolName, request.OnStatus, ct);

            if (root["usage"] is JsonObject usage)
            {
                inTokens += usage["input_tokens"]?.GetValue<int>() ?? 0;
                outTokens += usage["output_tokens"]?.GetValue<int>() ?? 0;
            }

            var content = root["content"] as JsonArray ?? new JsonArray();
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });

            var stopReason = root["stop_reason"]?.GetValue<string>();
            var toolUses = content.OfType<JsonObject>()
                .Where(b => b["type"]?.GetValue<string>() == "tool_use")
                .ToList();

            if (toolUses.Count == 0) break; // model answered in text without the terminal tool

            var terminal = toolUses.FirstOrDefault(b => b["name"]?.GetValue<string>() == request.TerminalToolName);
            if (terminal is not null)
            {
                // A response cut off by max_tokens carries a truncated tool input - that's
                // not a real answer. Report it as a failure so the caller retries instead
                // of trusting (and caching) junk.
                if (stopReason == "max_tokens")
                {
                    _logger.LogWarning("Terminal tool {Tool} was truncated by max_tokens - discarding answer", request.TerminalToolName);
                    hitMax = true;
                    break;
                }
                answer = terminal["input"]?.Deserialize<JsonElement>();
                break;
            }

            var results = new JsonArray();
            foreach (var use in toolUses)
            {
                toolCalls++;
                var name = use["name"]?.GetValue<string>() ?? "";
                var id = use["id"]?.GetValue<string>() ?? "";
                var input = use["input"]?.Deserialize<JsonElement>() ?? default;
                string output;
                try { output = await request.Executor(name, input, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tool {Tool} threw during AI run", name);
                    output = $"error: {ex.Message}";
                }
                results.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = id,
                    ["content"] = output,
                });
            }
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });

            if (iter == request.MaxIterations - 1) hitMax = true;
        }

        return new AgentRunResult(answer, messages.ToJsonString(), inTokens, outTokens, toolCalls, hitMax);
    }

    // Streams one Messages call (SSE), narrating progress, and reassembles the events
    // into the same JSON shape a non-streaming response would have: content blocks are
    // cloned from their start events (preserving unknown fields) with the accumulated
    // text / thinking / tool-input filled in at the end.
    private async Task<JsonNode> SendStreamingAsync(
        HttpClient http, JsonObject body, string terminalToolName, Action<string>? onStatus, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Anthropic API {Status}: {Body}", (int)resp.StatusCode, errBody);
            throw new AnthropicException($"Anthropic API returned {(int)resp.StatusCode}");
        }

        var blocks = new SortedDictionary<int, JsonObject>();
        var textAcc = new Dictionary<int, StringBuilder>();
        var thinkAcc = new Dictionary<int, StringBuilder>();
        var sigAcc = new Dictionary<int, StringBuilder>();
        var jsonAcc = new Dictionary<int, StringBuilder>();

        string? stopReason = null;
        int inputTokens = 0, outputTokens = 0;

        // Narration state.
        var thinkingSince = (DateTime?)null;
        var lastReport = DateTime.MinValue;
        int terminalIdx = -1;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            if (JsonNode.Parse(line[6..]) is not JsonObject evt) continue;

            switch (evt["type"]?.GetValue<string>())
            {
                case "message_start":
                    inputTokens = evt["message"]?["usage"]?["input_tokens"]?.GetValue<int>() ?? 0;
                    break;

                case "content_block_start":
                {
                    var idx = evt["index"]!.GetValue<int>();
                    var cb = evt["content_block"] as JsonObject ?? new JsonObject();
                    blocks[idx] = (JsonObject)cb.DeepClone();
                    var btype = cb["type"]?.GetValue<string>();
                    if (btype == "tool_use")
                    {
                        jsonAcc[idx] = new StringBuilder();
                        if (cb["name"]?.GetValue<string>() == terminalToolName)
                        {
                            terminalIdx = idx;
                            onStatus?.Invoke("The AI is writing up its answer...");
                        }
                    }
                    else if (btype == "thinking")
                    {
                        thinkAcc[idx] = new StringBuilder();
                        sigAcc[idx] = new StringBuilder();
                        thinkingSince ??= DateTime.UtcNow;
                        onStatus?.Invoke("The AI is thinking...");
                        lastReport = DateTime.UtcNow;
                    }
                    else if (btype == "text")
                    {
                        textAcc[idx] = new StringBuilder();
                    }
                    break;
                }

                case "content_block_delta":
                {
                    var idx = evt["index"]!.GetValue<int>();
                    var delta = evt["delta"] as JsonObject;
                    switch (delta?["type"]?.GetValue<string>())
                    {
                        case "text_delta":
                            textAcc.GetValueOrDefault(idx)?.Append(delta["text"]?.GetValue<string>());
                            break;
                        case "thinking_delta":
                        {
                            var acc = thinkAcc.GetValueOrDefault(idx);
                            acc?.Append(delta["thinking"]?.GetValue<string>());
                            if (acc is not null && (DateTime.UtcNow - lastReport).TotalSeconds >= 3)
                            {
                                lastReport = DateTime.UtcNow;
                                // The stream gives us a live summary of the model's reasoning -
                                // show its tail so the user sees WHAT it's working on, not a timer.
                                var snippet = ThinkingSnippet(acc);
                                var secs = (int)(DateTime.UtcNow - (thinkingSince ?? DateTime.UtcNow)).TotalSeconds;
                                onStatus?.Invoke(snippet is null
                                    ? $"The AI is thinking... ({secs}s on this step)"
                                    : $"The AI is thinking: \"{snippet}\"");
                            }
                            break;
                        }
                        case "signature_delta":
                            sigAcc.GetValueOrDefault(idx)?.Append(delta["signature"]?.GetValue<string>());
                            break;
                        case "input_json_delta":
                        {
                            var acc = jsonAcc.GetValueOrDefault(idx);
                            acc?.Append(delta["partial_json"]?.GetValue<string>());
                            if (idx == terminalIdx && acc is not null && (DateTime.UtcNow - lastReport).TotalSeconds >= 3)
                            {
                                lastReport = DateTime.UtcNow;
                                onStatus?.Invoke(AnswerProgress(acc.ToString()));
                            }
                            break;
                        }
                    }
                    break;
                }

                case "message_delta":
                    stopReason ??= evt["delta"]?["stop_reason"]?.GetValue<string>();
                    outputTokens = evt["usage"]?["output_tokens"]?.GetValue<int>() ?? outputTokens;
                    break;

                case "error":
                    throw new AnthropicException($"Anthropic stream error: {evt["error"]?["message"]?.GetValue<string>()}");
            }
        }

        // Fill each block's accumulated payload back in.
        var content = new JsonArray();
        foreach (var (idx, block) in blocks)
        {
            switch (block["type"]?.GetValue<string>())
            {
                case "tool_use":
                    block["input"] = ParseOrEmptyObject(jsonAcc.GetValueOrDefault(idx)?.ToString());
                    break;
                case "thinking":
                    block["thinking"] = thinkAcc.GetValueOrDefault(idx)?.ToString() ?? "";
                    if (sigAcc.GetValueOrDefault(idx) is { Length: > 0 } sig) block["signature"] = sig.ToString();
                    break;
                case "text":
                    block["text"] = textAcc.GetValueOrDefault(idx)?.ToString() ?? "";
                    break;
            }
            content.Add(block);
        }

        return new JsonObject
        {
            ["content"] = content,
            ["stop_reason"] = stopReason,
            ["usage"] = new JsonObject { ["input_tokens"] = inputTokens, ["output_tokens"] = outputTokens },
        };
    }

    // Last readable slice of the accumulated thinking summary, one line, ~140 chars.
    private static string? ThinkingSnippet(StringBuilder acc)
    {
        var sb = new StringBuilder(acc.Length);
        var lastWasSpace = false;
        foreach (var ch in acc.ToString())
        {
            var c = char.IsWhiteSpace(ch) ? ' ' : ch;
            if (c == ' ' && lastWasSpace) continue;
            lastWasSpace = c == ' ';
            sb.Append(c);
        }
        var clean = sb.ToString().Trim();
        // The accumulated summary's live edge ends mid-sentence - cut back to the last
        // finished sentence so the status line always reads complete.
        var lastEnd = clean.LastIndexOfAny(SentenceEnders);
        if (lastEnd < 20) return null;
        clean = clean[..(lastEnd + 1)];
        if (clean.Length <= 140) return clean;
        var tail = clean[^140..];
        var firstSpace = tail.IndexOf(' ');
        if (firstSpace > 0 && firstSpace < 40) tail = tail[(firstSpace + 1)..];
        return "…" + tail;
    }

    private static readonly char[] SentenceEnders = { '.', '!', '?' };

    private static JsonNode ParseOrEmptyObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try { return JsonNode.Parse(json) ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    // Reads the growing submit_result JSON and says which send is being written right
    // now - e.g. `writing up send 7, "MSK Pain"` - so the longest stretch of a run
    // narrates real content instead of sitting on one frozen line.
    private static string AnswerProgress(string partialJson)
    {
        var topics = TopicRegex.Matches(partialJson);
        if (topics.Count == 0) return "The AI is writing up its answer...";
        var topic = topics[^1].Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        if (topic.Length > 70) topic = topic[..70] + "…";
        return $"The AI is writing up its answer - send {topics.Count}: \"{topic}\"...";
    }

    private static readonly System.Text.RegularExpressions.Regex TopicRegex =
        new("\"topic\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", System.Text.RegularExpressions.RegexOptions.Compiled);
}
