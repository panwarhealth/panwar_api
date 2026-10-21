using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Panwar.Api.Services.Timesheets;

public interface ITrelloService
{
    Task<string?> CreateJobCardAsync(string code, string title, decimal budgetHours, CancellationToken cancellationToken = default);
}

public class TrelloService : ITrelloService
{
    private const string BaseUrl = "https://api.trello.com/1";
    private const string QuotedHoursLine = "Quoted hours";
    private const string Placeholder = "XXX";

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TrelloService> _logger;

    public TrelloService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<TrelloService> logger)
    {
        _http = httpClientFactory.CreateClient();
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> CreateJobCardAsync(string code, string title, decimal budgetHours, CancellationToken cancellationToken = default)
    {
        var key = _configuration["TRELLO_API_KEY"];
        var token = _configuration["TRELLO_TOKEN"];
        var listId = _configuration["TRELLO_LIST_ID"];
        var templateId = _configuration["TRELLO_TEMPLATE_CARD_ID"];
        if (new[] { key, token, listId, templateId }.Any(string.IsNullOrEmpty))
        {
            _logger.LogWarning("Trello is not configured, so no card was made for {Code}", code);
            return null;
        }

        var auth = $"key={Uri.EscapeDataString(key!)}&token={Uri.EscapeDataString(token!)}";
        try
        {
            var name = Uri.EscapeDataString($"{code} | {title}");
            var created = await _http.PostAsync(
                $"{BaseUrl}/cards?idList={listId}&idCardSource={templateId}&keepFromSource=all&name={name}&{auth}", null, cancellationToken);
            created.EnsureSuccessStatusCode();
            var card = await created.Content.ReadFromJsonAsync<TrelloCard>(cancellationToken) ?? throw new InvalidOperationException("Empty Trello response");

            var desc = string.Join('\n', card.Desc.Split('\n').Select(line =>
                line.Contains(QuotedHoursLine, StringComparison.OrdinalIgnoreCase) ? line.Replace(Placeholder, budgetHours.ToString("0.##")) : line));
            if (desc != card.Desc)
            {
                var updated = await _http.PutAsync(
                    $"{BaseUrl}/cards/{card.Id}?desc={Uri.EscapeDataString(desc)}&{auth}", null, cancellationToken);
                updated.EnsureSuccessStatusCode();
            }
            return card.ShortUrl;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            _logger.LogError(ex, "Trello card for {Code} could not be created", code);
            return null;
        }
    }

    private sealed class TrelloCard
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("desc")] public string Desc { get; set; } = "";
        [JsonPropertyName("shortUrl")] public string ShortUrl { get; set; } = "";
    }
}
