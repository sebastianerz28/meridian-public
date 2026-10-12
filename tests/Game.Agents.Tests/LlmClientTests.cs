using System.Net;
using System.Text.Json;
using Game.Agents;

namespace Game.Agents.Tests;

file sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest;
    public string? LastBody;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return respond(request);
    }
}

public class LlmClientTests
{
    [Fact]
    public async Task ClaudeClient_SendsCorrectShape_AndParsesText()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"content":[{"type":"text","text":"{\"ok\":1}"}]}""")
        });
        var client = new ClaudeClient(new HttpClient(handler), "key-123", "claude-haiku-4-5-20251001", 1024);
        Assert.Equal("""{"ok":1}""", await client.CompleteAsync("SYS", "USER", default));

        // Verify request shape
        Assert.Equal("https://api.anthropic.com/v1/messages", handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal("key-123", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.LastRequest!.Headers.GetValues("anthropic-version").Single());

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("claude-haiku-4-5-20251001", root.GetProperty("model").GetString());
        Assert.Equal(1024, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("SYS", root.GetProperty("system").GetString());
        Assert.Equal("USER", root.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task LlamaCppClient_UsesOpenAiChatShape()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"role":"assistant","content":"hi"}}]}""")
        });
        var client = new LlamaCppClient(new HttpClient(handler), "http://localhost:8080", 512);
        Assert.Equal("hi", await client.CompleteAsync("SYS", "USER", default));

        Assert.EndsWith("/v1/chat/completions", handler.LastRequest!.RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("SYS", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("USER", messages[1].GetProperty("content").GetString());
        Assert.Equal(512, root.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task ClaudeClient_ThrowsWithBodyOnError()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"bad model"}}""")
        });
        var client = new ClaudeClient(new HttpClient(handler), "k", "m", 10);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync("s", "u", default));
        Assert.Contains("bad model", ex.Message);
    }

    [Fact]
    public async Task LlamaCppClient_ThrowsWithBodyOnError()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid token"}""")
        });
        var client = new LlamaCppClient(new HttpClient(handler), "http://localhost:8080", 256);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync("s", "u", default));
        Assert.Contains("invalid token", ex.Message);
    }

    [Fact]
    public void LlmConfig_LoadRoundTripsAndAppliesDefaults()
    {
        var jsonContent = """{"provider":"claude","model":"custom-model","baseUrl":"http://custom:8080","maxTokens":2048}""";
        var tempFile = Path.Combine(Path.GetTempPath(), $"llm-config-{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(tempFile, jsonContent);
            var config = LlmConfig.Load(tempFile);

            Assert.Equal("claude", config.Provider);
            Assert.Equal("custom-model", config.Model);
            Assert.Equal("http://custom:8080", config.BaseUrl);
            Assert.Equal(2048, config.MaxTokens);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void LlmConfig_AppliesDefaultsWhenNotProvided()
    {
        var jsonContent = """{"provider":"llamacpp"}""";
        var tempFile = Path.Combine(Path.GetTempPath(), $"llm-config-{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(tempFile, jsonContent);
            var config = LlmConfig.Load(tempFile);

            Assert.Equal("llamacpp", config.Provider);
            Assert.Equal("claude-haiku-4-5-20251001", config.Model);
            Assert.Equal("http://localhost:8080", config.BaseUrl);
            Assert.Equal(1024, config.MaxTokens);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void LlmConfig_CreateClient_LlamaCpp_ReturnsLlamaCppClient()
    {
        var config = new LlmConfig("llamacpp", BaseUrl: "http://test:9000");
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = new HttpClient(handler);

        var client = config.CreateClient(http);

        Assert.IsType<LlamaCppClient>(client);
    }

    [Fact]
    public void LlmConfig_CreateClient_UnknownProvider_Throws()
    {
        var config = new LlmConfig("unknown_provider");
        var http = new HttpClient();

        var ex = Assert.Throws<InvalidOperationException>(() => config.CreateClient(http));
        Assert.Contains("Unknown LLM provider", ex.Message);
    }

    [Fact]
    public void LlmConfig_CreateClient_Claude_WithoutApiKey_Throws()
    {
        var config = new LlmConfig("claude");
        var http = new HttpClient();

        // Ensure env var is not set
        var originalValue = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);

            var ex = Assert.Throws<InvalidOperationException>(() => config.CreateClient(http));
            Assert.Contains("ANTHROPIC_API_KEY environment variable is not set", ex.Message);
        }
        finally
        {
            if (originalValue != null)
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalValue);
        }
    }

    [Fact]
    public void LlmConfig_CreateClient_Claude_WithApiKey_ReturnsClaudeClient()
    {
        var config = new LlmConfig("claude", Model: "claude-opus", MaxTokens: 4096);
        var http = new HttpClient();

        var originalValue = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key-12345");

            var client = config.CreateClient(http);

            Assert.IsType<ClaudeClient>(client);
        }
        finally
        {
            if (originalValue != null)
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalValue);
            else
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        }
    }
}
