using Game.Contracts;

namespace Game.Agents;

public interface IAgentTelemetry
{
    void LlmAttempt(CountryId country, int turn, string kind, string prompt, string response, bool valid, long ms);
    void LlmFailure(CountryId country, int turn, string kind, string error);
    void MemoryWritten(CountryId country, int turn, string text);
}

public sealed class NullTelemetry : IAgentTelemetry
{
    public void LlmAttempt(CountryId country, int turn, string kind, string prompt, string response, bool valid, long ms) { }
    public void LlmFailure(CountryId country, int turn, string kind, string error) { }
    public void MemoryWritten(CountryId country, int turn, string text) { }
}
