using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Panwar.Api.Services.Ai;

// First Anthropic integration in this codebase. Mirrors GraphService: raw HttpClient
// via IHttpClientFactory, config key with throwing-or-disabled resolution,
// System.Text.Json, typed exception. Default model claude-sonnet-4-6.
public class AnthropicService : IAnthropicService
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AnthropicService> _logger;
    private readonly string? _apiKey;
    private readonly string _model;

    public AnthropicService(IConfiguration configuration, IHttpClientFactory httpFactory, ILogger<AnthropicService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _apiKey = configuration["ANTHROPIC_API_KEY"];
        _model = configuration["ANTHROPIC_MODEL"] ?? "claude-sonnet-4-6";
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string> CompleteAsync(string system, string userContent, object? jsonSchema, CancellationToken ct)
    {
        if (!IsEnabled) throw new AnthropicException("ANTHROPIC_API_KEY is not configured");

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["max_tokens"] = 8192,
            ["system"] = system,
            ["messages"] = new[] { new { role = "user", content = userContent } },
        };
        if (jsonSchema is not null)
            body["output_config"] = new { format = new { type = "json_schema", schema = jsonSchema } };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);

        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(2);

        using var resp = await http.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic API {Status}: {Body}", (int)resp.StatusCode, respBody);
            throw new AnthropicException($"Anthropic API returned {(int)resp.StatusCode}");
        }

        var parsed = JsonSerializer.Deserialize<MessageResponse>(respBody, Json);
        var text = parsed?.Content?.FirstOrDefault(b => b.Type == "text")?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Anthropic response had no text block (stop_reason={Stop})", parsed?.StopReason);
            return "";
        }
        return text;
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
        [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}
