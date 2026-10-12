using System.Collections.Immutable;
using Game.Agents;
using Game.Contracts;
using Game.Simulation;
using Game.Simulation.Observation;

namespace Game.Agents.Tests;

file sealed class RecordingTelemetry : IAgentTelemetry
{
    public readonly List<(string Kind, bool Valid)> Attempts = new();
    public readonly List<string> Failures = new();
    public readonly List<string> Memories = new();
    public void LlmAttempt(CountryId c, int t, string kind, string p, string r, bool valid, long ms)
        => Attempts.Add((kind, valid));
    public void LlmFailure(CountryId c, int t, string kind, string error) => Failures.Add(error);
    public void MemoryWritten(CountryId c, int t, string text) => Memories.Add(text);
}

file sealed class ThrowingClient : ILlmClient
{
    public Task<string> CompleteAsync(string s, string u, CancellationToken ct)
        => throw new HttpRequestException("provider down");
}

public class LlmActorTests
{
    private static DecisionContext Ctx(CountryId self)
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, 42);
        return new DecisionContext(self, 1, ObservationSystem.Freeze(w, all[self]),
            ImmutableArray<DomainEvent>.Empty, StrategicSignificance.None);
    }

    private static LlmActor Actor(ILlmClient client, IAgentTelemetry? t = null) =>
        new(AgentState.FromTemplate(CountryId.Borvia, "borvia-national-front"),
            client, t ?? new NullTelemetry());

    [Fact]
    public async Task ValidResponse_BecomesIntentAndUpdatesAssessmentAndBeliefs()
    {
        var actor = Actor(new ScriptedClient(new Queue<string>(new[]
        {
            """{"assessment":"Hold.","beliefs_update":["Alderia will fold"],"action":{"type":"Mobilize","magnitude":0.4},"public_statement":"Routine."}"""
        })));
        var intent = await actor.DecideAsync(Ctx(CountryId.Borvia), default);
        Assert.Equal(ActionType.Mobilize, intent.Type);
        Assert.Equal("Hold.", actor.State.LatestAssessment);
        Assert.Contains("Alderia will fold", actor.State.PoliticalBeliefs);
        Assert.Equal("Routine.", intent.Statement);
    }

    [Fact]
    public async Task InvalidThenValid_RetriesOnce_LoggingBothAttempts()
    {
        var telemetry = new RecordingTelemetry();
        var actor = Actor(new ScriptedClient(new Queue<string>(new[]
        {
            "we should sanction them",
            """{"assessment":"ok","action":{"type":"Sanction","target":"Alderia","magnitude":0.5},"public_statement":"x"}"""
        })), telemetry);
        var intent = await actor.DecideAsync(Ctx(CountryId.Borvia), default);
        Assert.Equal(ActionType.Sanction, intent.Type);
        Assert.Equal(new[] { false, true }, telemetry.Attempts.Select(a => a.Valid));
    }

    [Fact]
    public async Task TwoInvalid_FallsBackToPass()
    {
        var actor = Actor(new ScriptedClient(new Queue<string>(new[] { "garbage", "more garbage" })));
        Assert.Equal(ActionType.Pass, (await actor.DecideAsync(Ctx(CountryId.Borvia), default)).Type);
    }

    [Fact]
    public async Task ProviderFailure_DegradesToPass_AndIsRecorded()
    {
        var telemetry = new RecordingTelemetry();
        var actor = Actor(new ThrowingClient(), telemetry);
        var intent = await actor.DecideAsync(Ctx(CountryId.Borvia), default);
        Assert.Equal(ActionType.Pass, intent.Type);
        Assert.Contains("provider down", telemetry.Failures[0]);
    }

    [Fact]
    public async Task Reflect_StoresBoundedMemoryAndReportsIt()
    {
        var telemetry = new RecordingTelemetry();
        var actor = Actor(new ScriptedClient(new Queue<string>(new[] { new string('m', 2000) })), telemetry);
        await actor.ReflectAsync(new ReflectionContext(CountryId.Borvia, 3,
            ImmutableArray<DomainEvent>.Empty, StrategicSignificance.Critical), default);
        Assert.Single(actor.State.Memories);
        Assert.True(actor.State.Memories[0].Length <= Bounds.MaxMemory);
        Assert.Single(telemetry.Memories);
    }

    [Fact]
    public void OnGovernmentChanged_SwapsGovernmentBlock()
    {
        var actor = Actor(new ScriptedClient(new Queue<string>()));
        // Borvia is autocratic; use Alderia for swap semantics
        var state = AgentState.FromTemplate(CountryId.Alderia, "alderia-atlantic-coalition");
        var a = new LlmActor(state, new ScriptedClient(new Queue<string>()), new NullTelemetry());
        state.Memories.Add("keep me");
        a.OnGovernmentChanged("alderia-prosperity-party");
        Assert.Equal("alderia-prosperity-party", state.GovernmentTemplateId);
        Assert.Contains("keep me", state.Memories);
    }

    [Fact]
    public async Task FiveTurnGame_WithScriptedLlm_RunsCleanly()
    {
        ActorRegistration Reg(CountryId c, string template) => new(c,
            new LlmActor(AgentState.FromTemplate(c, template),
                new ScriptedClient(new Queue<string>(Enumerable.Repeat(
                    """{"assessment":"steady","action":{"type":"Pass"},"public_statement":""}""", 30))),
                new NullTelemetry()),
            SchedulingPolicy.OnSignificance);
        var engine = new TurnEngine(Scenario.CreateMeridianStrait(), new[]
        {
            Reg(CountryId.Alderia, "alderia-atlantic-coalition"),
            Reg(CountryId.Borvia, "borvia-national-front"),
            Reg(CountryId.Cascara, "cascara-maritime-league"),
        }, 42);
        for (var i = 0; i < 5; i++) await engine.AdvanceTurnAsync();
        Assert.Equal(5, engine.World.Turn);
    }
}
