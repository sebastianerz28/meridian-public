using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Agents;

public record LlmConfig(
    string Provider,
    string Model = "claude-haiku-4-5-20251001",
    string BaseUrl = "http://localhost:8080",
    int MaxTokens = 1024
)
{
    public static LlmConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<LlmConfig>(json, options);

        if (config is null)
        {
            throw new InvalidOperationException($"Failed to deserialize config from {path}");
        }

        return config;
    }

    public ILlmClient CreateClient(HttpClient http)
    {
        return Provider.ToLowerInvariant() switch
        {
            "claude" => CreateClaudeClient(http),
            "llamacpp" => new LlamaCppClient(http, BaseUrl, MaxTokens),
            _ => throw new InvalidOperationException($"Unknown LLM provider: {Provider}")
        };
    }

    private ClaudeClient CreateClaudeClient(HttpClient http)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "ANTHROPIC_API_KEY environment variable is not set. " +
                "Please set the ANTHROPIC_API_KEY environment variable before using the Claude client."
            );
        }

        return new ClaudeClient(http, apiKey, Model, MaxTokens);
    }
}
