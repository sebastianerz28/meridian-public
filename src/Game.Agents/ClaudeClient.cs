using System.Text;
using System.Text.Json;

namespace Game.Agents;

public sealed class ClaudeClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxTokens;
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    public ClaudeClient(HttpClient http, string apiKey, string model, int maxTokens)
    {
        _http = http;
        _apiKey = apiKey;
        _model = model;
        _maxTokens = maxTokens;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = _maxTokens,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        var response = await _http.SendAsync(request, ct);
        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Claude API returned {response.StatusCode}: {responseContent}", null, response.StatusCode);
        }

        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;
        var contentArray = root.GetProperty("content");

        var textParts = new StringBuilder();
        foreach (var item in contentArray.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeElem) && typeElem.GetString() == "text")
            {
                if (item.TryGetProperty("text", out var textElem))
                {
                    textParts.Append(textElem.GetString());
                }
            }
        }

        return textParts.ToString();
    }
}
