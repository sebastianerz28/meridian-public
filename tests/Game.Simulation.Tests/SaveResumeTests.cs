using Game.Contracts;
using Game.Persistence;
using Game.Simulation;
using Game.Simulation.State;

namespace Game.Simulation.Tests;

// CONTEXT-SENSITIVE: post-resume decisions depend on DecisionContext content
// (relevant-event count and view estimates), so a resume that fails to restore
// windows or knowledge produces a DIFFERENT event stream and fails the test.
file sealed class StoryActor(CountryId self) : IActor
{
    private static double CtxMagnitude(DecisionContext ctx) =>
        Math.Clamp(0.2 + 0.05 * ctx.RelevantEvents.Length, 0, 1);

    public Task<ActionIntent> DecideAsync(DecisionContext ctx, CancellationToken ct) =>
        Task.FromResult((self, ctx.Turn) switch
        {
            (CountryId.Borvia, 2) => new ActionIntent(self, ActionType.ExportControl, CountryId.Alderia, 0.8, "a"),
            (CountryId.Alderia, 3) => new ActionIntent(self, ActionType.Mobilize, null, 0.5, "b"),
            (CountryId.Alderia, 5) => new ActionIntent(self, ActionType.DeployToRegion, null, 0.8, "c"),
            (CountryId.Borvia, 6) => new ActionIntent(self, ActionType.Spy, CountryId.Alderia, 0.5, "d"),
            (CountryId.Cascara, 7) => new ActionIntent(self, ActionType.Reassure, CountryId.Alderia, 0.6, "e"),
            // post-resume turns whose magnitude depends on the decision context:
            (CountryId.Cascara, 10) => new ActionIntent(self, ActionType.Reassure, CountryId.Alderia,
                CtxMagnitude(ctx), "f"),
            (CountryId.Alderia, 12) => new ActionIntent(self, ActionType.Subsidize, null,
                Math.Clamp(ctx.View.ForeignEstimates.Count * 0.02, 0, 1), "g"),
            _ => ActionIntent.Pass(self),
        });
    public Task ReflectAsync(ReflectionContext ctx, CancellationToken ct) => Task.CompletedTask;
}

public class SaveResumeTests
{
    private static IReadOnlyList<ActorRegistration> Actors() => new[]
    {
        new ActorRegistration(CountryId.Alderia, new StoryActor(CountryId.Alderia), SchedulingPolicy.EveryTurn),
        new ActorRegistration(CountryId.Borvia, new StoryActor(CountryId.Borvia), SchedulingPolicy.EveryTurn),
        new ActorRegistration(CountryId.Cascara, new StoryActor(CountryId.Cascara), SchedulingPolicy.EveryTurn),
    };

    [Fact]
    public async Task Turns1To20_EqualsTurns1To8_SaveResume_9To20()
    {
        // uninterrupted reference run
        var straight = new TurnEngine(Scenario.CreateMeridianStrait(), Actors(), 7);
        var straightEvents = new List<DomainEvent>();
        for (var t = 0; t < 20; t++) straightEvents.AddRange((await straight.AdvanceTurnAsync()).Events);

        // interrupted run
        var path = Path.Combine(Path.GetTempPath(), $"meridian-resume-{Guid.NewGuid()}.db");
        try
        {
            var first = new TurnEngine(Scenario.CreateMeridianStrait(), Actors(), 7);
            var firstEvents = new List<DomainEvent>();
            using (var store = new GameStore(path))
            {
                store.InitSchema();
                for (var t = 0; t < 8; t++)
                {
                    var result = await first.AdvanceTurnAsync();
                    firstEvents.AddRange(result.Events);
                    store.SaveTurn(result.Turn, first.World, result.Events, "{}", first.Snapshot());
                }

                Assert.Equal(
                    firstEvents.Select(e => e.ToString()),
                    store.LoadAllEvents().Select(e => e.ToString()));
            }

            List<DomainEvent> resumedEvents;
            WorldState resumedWorld;
            using (var store = new GameStore(path))
            {
                var loaded = store.LoadLatestSnapshot()!.Value;
                var resumed = TurnEngine.Resume(loaded.World, Actors(), loaded.Engine);
                resumedEvents = new List<DomainEvent>();
                for (var t = 0; t < 12; t++)
                {
                    var result = await resumed.AdvanceTurnAsync();
                    resumedEvents.AddRange(result.Events);
                    store.SaveTurn(result.Turn, resumed.World, result.Events, "{}", resumed.Snapshot());
                }
                resumedWorld = resumed.World;

                Assert.Equal(
                    straightEvents.Select(e => e.ToString()),
                    store.LoadAllEvents().Select(e => e.ToString()));
            }

            var straightTail = straightEvents.Where(e => e.Turn > 8).Select(e => e.ToString());
            Assert.Equal(straightTail, resumedEvents.Select(e => e.ToString()));
            Assert.Equal(straight.World.Countries[CountryId.Alderia].Gdp,
                resumedWorld.Countries[CountryId.Alderia].Gdp, 9);
            Assert.Equal(straight.World.StraitTension, resumedWorld.StraitTension, 9);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
