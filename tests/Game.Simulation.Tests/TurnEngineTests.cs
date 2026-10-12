using Game.Contracts;
using Game.Simulation;
using Game.Simulation.Observation;

namespace Game.Simulation.Tests;

sealed class ProbeActor(CountryId self, Func<int, ActionIntent>? script = null) : IActor
{
    public readonly List<int> DecidedTurns = new();
    public readonly List<int> ReflectedTurns = new();
    public string? SwappedTo;
    public Task<ActionIntent> DecideAsync(DecisionContext ctx, CancellationToken ct)
    { DecidedTurns.Add(ctx.Turn); return Task.FromResult(script?.Invoke(ctx.Turn) ?? ActionIntent.Pass(self)); }
    public Task ReflectAsync(ReflectionContext ctx, CancellationToken ct)
    { ReflectedTurns.Add(ctx.Turn); return Task.CompletedTask; }
    public void OnGovernmentChanged(string t) => SwappedTo = t;
}

sealed class ScriptedActor(Func<DecisionContext, ActionIntent> script) : IActor
{
    public Task<ActionIntent> DecideAsync(DecisionContext ctx, CancellationToken ct) => Task.FromResult(script(ctx));
    public Task ReflectAsync(ReflectionContext ctx, CancellationToken ct) => Task.CompletedTask;
}

public class TurnEngineTests
{
    private static (TurnEngine, ProbeActor a, ProbeActor b, ProbeActor c) Build(
        ulong seed, SchedulingPolicy llmPolicy = SchedulingPolicy.OnSignificance,
        Func<int, ActionIntent>? borviaScript = null,
        SchedulingPolicy borviaPolicy = SchedulingPolicy.OnSignificance)
    {
        var a = new ProbeActor(CountryId.Alderia);
        var b = new ProbeActor(CountryId.Borvia, borviaScript);
        var c = new ProbeActor(CountryId.Cascara);
        var e = new TurnEngine(Scenario.CreateMeridianStrait(), new[]
        {
            new ActorRegistration(CountryId.Alderia, a, llmPolicy),
            new ActorRegistration(CountryId.Borvia, b, borviaPolicy),
            new ActorRegistration(CountryId.Cascara, c, llmPolicy),
        }, seed);
        return (e, a, b, c);
    }

    private static ActionIntent BorviaExportControl(int t) => t == 2
        ? new ActionIntent(CountryId.Borvia, ActionType.ExportControl, CountryId.Alderia, 0.8, "punish")
        : ActionIntent.Pass(CountryId.Borvia);

    private static TurnEngine BuildScripted(
        ulong seed,
        Func<DecisionContext, ActionIntent> alderiaScript,
        Func<DecisionContext, ActionIntent> borviaScript,
        Func<DecisionContext, ActionIntent> cascaraScript) =>
        new(Scenario.CreateMeridianStrait(), new ActorRegistration[]
        {
            new(CountryId.Alderia, new ScriptedActor(alderiaScript), SchedulingPolicy.EveryTurn),
            new(CountryId.Borvia, new ScriptedActor(borviaScript), SchedulingPolicy.EveryTurn),
            new(CountryId.Cascara, new ScriptedActor(cascaraScript), SchedulingPolicy.EveryTurn),
        }, seed);

    [Fact]
    public async Task SameSeedSameScripts_IdenticalHistory()
    {
        var (e1, _, _, _) = Build(42, borviaScript: BorviaExportControl, borviaPolicy: SchedulingPolicy.EveryTurn);
        var (e2, _, _, _) = Build(42, borviaScript: BorviaExportControl, borviaPolicy: SchedulingPolicy.EveryTurn);
        for (var i = 0; i < 10; i++) { await e1.AdvanceTurnAsync(); await e2.AdvanceTurnAsync(); }
        Assert.Equal(e1.AllEvents.Select(x => x.ToString()), e2.AllEvents.Select(x => x.ToString()));
    }

    [Fact]
    public async Task EveryTurnActors_NeverMissAScheduledTurn()
    {
        var (engine, _, b, _) = Build(1, borviaScript: BorviaExportControl,
            borviaPolicy: SchedulingPolicy.EveryTurn);
        for (var i = 0; i < 4; i++) await engine.AdvanceTurnAsync();
        Assert.Equal(new[] { 1, 2, 3, 4 }, b.DecidedTurns);
        Assert.True(engine.World.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction > 0);
    }

    [Fact]
    public async Task QuietTurns_KeepOnSignificanceActorsAsleep()
    {
        var (engine, a, _, c) = Build(1);
        await engine.AdvanceTurnAsync();                    // turn 1: everyone wakes
        var r2 = await engine.AdvanceTurnAsync();           // all passed; routine drift only
        Assert.DoesNotContain(CountryId.Alderia, r2.AwakeActors);
        Assert.DoesNotContain(CountryId.Cascara, r2.AwakeActors);
    }

    [Fact]
    public async Task MeaningfulShock_WakesTheVictimNextTurn()
    {
        var (engine, a, _, _) = Build(1, borviaScript: BorviaExportControl,
            borviaPolicy: SchedulingPolicy.EveryTurn);
        await engine.AdvanceTurnAsync();                    // T1
        await engine.AdvanceTurnAsync();                    // T2: export control fires
        var r3 = await engine.AdvanceTurnAsync();           // T3: Alderia must wake
        Assert.Contains(CountryId.Alderia, r3.AwakeActors);
        Assert.Contains(2, a.ReflectedTurns);               // and reflected on the T2 shock
    }

    [Fact]
    public async Task Heartbeat_WakesSleepersWithinFourTurns()
    {
        var (engine, _, _, c) = Build(1);
        for (var i = 0; i < 5; i++) await engine.AdvanceTurnAsync();
        Assert.True(c.DecidedTurns.Count >= 2);
    }

    [Fact]
    public async Task GovernmentChanged_SyncsActorBeforeReflection()
    {
        var recorder = new OrderRecordingActor(CountryId.Alderia);
        var engine = new TurnEngine(Scenario.CreateMeridianStrait(), new[]
        {
            new ActorRegistration(CountryId.Alderia, recorder, SchedulingPolicy.OnSignificance),
            new ActorRegistration(CountryId.Borvia, new ProbeActor(CountryId.Borvia), SchedulingPolicy.OnSignificance),
            new ActorRegistration(CountryId.Cascara, new ProbeActor(CountryId.Cascara), SchedulingPolicy.OnSignificance),
        }, 1);
        engine.World.Countries[CountryId.Alderia].ElectionCountdown = 1;
        engine.World.Countries[CountryId.Alderia].Approval = 10;
        await engine.AdvanceTurnAsync();   // election fires, GovernmentChanged is Critical → reflection runs
        Assert.True(recorder.Reflected, "a GovernmentChanged turn must trigger reflection for the country");
        Assert.True(recorder.GovernmentSyncedBeforeReflect);
    }

    [Fact]
    public async Task ActorClaimingAnotherIdentity_IsForcedToPass()
    {
        var (engine, _, b, _) = Build(1,
            borviaScript: _ => new ActionIntent(CountryId.Alderia, ActionType.Sanction, CountryId.Cascara, 1, "spoof"),
            borviaPolicy: SchedulingPolicy.EveryTurn);
        var r = await engine.AdvanceTurnAsync();
        Assert.DoesNotContain(r.Events, e => e is SanctionLevelSet);
    }

    [Fact]
    public async Task SnapshotResume_ProducesIdenticalContinuationAsUninterruptedRun()
    {
        const ulong seed = 7;

        Func<DecisionContext, ActionIntent> alderiaScript = ctx => ctx.Turn switch
        {
            5 => new ActionIntent(CountryId.Alderia, ActionType.DeployToRegion, null, 0.5, "reinforce"),
            // Context-sensitive: magnitude derived from the restored relevant-events
            // window, so a resume that fails to restore it diverges from here on.
            9 => new ActionIntent(CountryId.Alderia, ActionType.Mobilize, null,
                Math.Min(1.0, ctx.RelevantEvents.Length / 10.0), "context-sensitive"),
            _ => ActionIntent.Pass(CountryId.Alderia),
        };
        Func<DecisionContext, ActionIntent> borviaScript = ctx => ctx.Turn == 2
            ? new ActionIntent(CountryId.Borvia, ActionType.ExportControl, CountryId.Alderia, 0.8, "punish")
            : ActionIntent.Pass(CountryId.Borvia);
        Func<DecisionContext, ActionIntent> cascaraScript = _ => ActionIntent.Pass(CountryId.Cascara);

        // Original engine: 6 turns, snapshot, then 6 more turns of continuation.
        var original = BuildScripted(seed, alderiaScript, borviaScript, cascaraScript);
        for (var i = 0; i < 6; i++) await original.AdvanceTurnAsync();
        var snapshot = original.Snapshot();
        var eventsBeforeResume = original.AllEvents.Count;

        // A wholly independent engine, run with the identical seed and scripts for the
        // same 6 turns, produces a bit-identical WorldState (already proven by
        // SameSeedSameScripts_IdenticalHistory above) — this stands in for "replay from
        // the persisted event log up to the snapshot point," without touching Task 11's
        // actual persistence machinery.
        var replaySource = BuildScripted(seed, alderiaScript, borviaScript, cascaraScript);
        for (var i = 0; i < 6; i++) await replaySource.AdvanceTurnAsync();

        var resumedActors = new ActorRegistration[]
        {
            new(CountryId.Alderia, new ScriptedActor(alderiaScript), SchedulingPolicy.EveryTurn),
            new(CountryId.Borvia, new ScriptedActor(borviaScript), SchedulingPolicy.EveryTurn),
            new(CountryId.Cascara, new ScriptedActor(cascaraScript), SchedulingPolicy.EveryTurn),
        };
        var resumed = TurnEngine.Resume(replaySource.World, resumedActors, snapshot);

        for (var i = 0; i < 6; i++)
        {
            await original.AdvanceTurnAsync();
            await resumed.AdvanceTurnAsync();
        }

        var originalContinuation = original.AllEvents.Skip(eventsBeforeResume).Select(e => e.ToString());
        Assert.Equal(originalContinuation, resumed.AllEvents.Select(e => e.ToString()));
        Assert.Equal(
            original.World.Countries[CountryId.Alderia].Gdp,
            resumed.World.Countries[CountryId.Alderia].Gdp);
    }

    [Fact]
    public async Task Resume_MissingRegisteredActorInSnapshot_ThrowsClearException()
    {
        var (engine, _, _, _) = Build(3, borviaPolicy: SchedulingPolicy.EveryTurn);
        await engine.AdvanceTurnAsync();
        var snapshot = engine.Snapshot();

        var brokenKnowledge = new SortedDictionary<CountryId, ObserverKnowledgeDto>(snapshot.Knowledge);
        brokenKnowledge.Remove(CountryId.Cascara);
        var broken = snapshot with { Knowledge = brokenKnowledge };

        var actors = new ActorRegistration[]
        {
            new(CountryId.Alderia, new ScriptedActor(_ => ActionIntent.Pass(CountryId.Alderia)), SchedulingPolicy.EveryTurn),
            new(CountryId.Borvia, new ScriptedActor(_ => ActionIntent.Pass(CountryId.Borvia)), SchedulingPolicy.EveryTurn),
            new(CountryId.Cascara, new ScriptedActor(_ => ActionIntent.Pass(CountryId.Cascara)), SchedulingPolicy.EveryTurn),
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            TurnEngine.Resume(Scenario.CreateMeridianStrait(), actors, broken));
        Assert.Contains("Cascara", ex.Message);
    }
}

file sealed class OrderRecordingActor(CountryId self) : IActor
{
    private bool _synced;
    public bool Reflected { get; private set; }
    public bool GovernmentSyncedBeforeReflect { get; private set; }
    public Task<ActionIntent> DecideAsync(DecisionContext ctx, CancellationToken ct)
        => Task.FromResult(ActionIntent.Pass(self));
    public Task ReflectAsync(ReflectionContext ctx, CancellationToken ct)
    { Reflected = true; GovernmentSyncedBeforeReflect = _synced; return Task.CompletedTask; }
    public void OnGovernmentChanged(string t) => _synced = true;
}
