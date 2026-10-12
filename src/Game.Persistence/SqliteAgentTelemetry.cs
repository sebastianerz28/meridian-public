using Game.Agents;
using Game.Contracts;

namespace Game.Persistence;

/// <summary>
/// Adapts <see cref="IAgentTelemetry"/> (Game.Agents, DB-agnostic) onto <see cref="GameStore"/>
/// (Game.Persistence). This is the one place the dependency direction bends the "natural" way:
/// Game.Persistence references Game.Agents so this adapter can exist, while Game.Agents itself
/// stays free of any persistence coupling (it only knows the interface it depends on).
/// </summary>
/// <remarks>
/// Each call here writes directly to its own table (llm_calls, memories) rather than
/// participating in the per-turn SaveTurn transaction. Game state and event persistence are
/// crash-safe under that transaction, but telemetry is not: a crash mid-turn followed by
/// resume from the last saved turn can leave duplicate llm_calls/memories rows for the
/// partially-replayed turn. This does not affect simulation behavior or save/resume
/// equivalence — it only adds noise to these inspection/debugging tables.
/// </remarks>
public sealed class SqliteAgentTelemetry(GameStore store) : IAgentTelemetry
{
    public void LlmAttempt(CountryId country, int turn, string kind, string prompt, string response, bool valid, long ms) =>
        store.LogLlmCall(turn, country, kind, valid, prompt, response, error: null, ms);

    public void LlmFailure(CountryId country, int turn, string kind, string error) =>
        store.LogLlmCall(turn, country, kind, valid: false, prompt: string.Empty, response: string.Empty, error, ms: 0);

    public void MemoryWritten(CountryId country, int turn, string text) =>
        store.SaveMemory(country, turn, text);
}
