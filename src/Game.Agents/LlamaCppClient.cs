using System.Text;
using System.Text.Json;

namespace Game.Agents;

public sealed class LlamaCppClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly int _maxTokens;

    public LlamaCppClient(HttpClient http, string baseUrl, int maxTokens)
    {
        _http = http;
        _baseUrl = baseUrl;
        _maxTokens = maxTokens;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var requestBody = new
        {
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = _maxTokens
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var url = $"{_baseUrl.TrimEnd('/')}/v1/chat/completions";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        var response = await _http.SendAsync(request, ct);
        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"llama.cpp API returned {response.StatusCode}: {responseContent}", null, response.StatusCode);
        }

        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;
        var content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? string.Empty;
    }
}
