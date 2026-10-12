using Game.Contracts;
using Game.Persistence;
using Game.Simulation;

namespace Game.Simulation.Tests;

public class GameStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"meridian-{Guid.NewGuid()}.db");
    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    private GameStore NewStore() { var s = new GameStore(_path); s.InitSchema(); return s; }

    [Fact]
    public void Events_RoundTrip_PreservingVisibilityAndOrder()
    {
        using var store = NewStore();
        var events = new List<DomainEvent>
        {
            new TurnStarted(1, null, Visibility.Public, 1),
            new SpySucceeded(1, CountryId.Alderia, Visibility.ActorOnly(CountryId.Alderia), CountryId.Borvia),
            new SpyCaught(1, CountryId.Alderia, Visibility.Participants(CountryId.Alderia, CountryId.Borvia), CountryId.Borvia),
            new ExportRestrictionSet(1, CountryId.Borvia, Visibility.Public, CountryId.Borvia, CountryId.Alderia, 0.3, "s"),
        };
        store.SaveTurn(1, Scenario.CreateMeridianStrait(), events, "{}", EmptyEngine());
        var loaded = store.LoadAllEvents();
        Assert.Equal(events.Select(e => e.ToString()), loaded.Select(e => e.ToString()));
        var spy = loaded.OfType<SpySucceeded>().Single();
        Assert.False(spy.Visibility.CanObserve(CountryId.Cascara));   // secrecy survives reload
        Assert.True(spy.Visibility.CanObserve(CountryId.Alderia));
    }

    [Fact]
    public void Replay_FromPersistedStream_ReconstructsWorld()
    {
        using var store = NewStore();
        var w = Scenario.CreateMeridianStrait();
        var stream = new List<DomainEvent>
        {
            new TurnStarted(1, null, Visibility.Public, 1),
            new GdpChanged(1, null, Visibility.Public, CountryId.Alderia, -100),
            new ExportRestrictionSet(1, CountryId.Borvia, Visibility.Public, CountryId.Borvia, CountryId.Alderia, 0.3, "s"),
        };
        Game.Simulation.WorldReducer.ApplyAll(w, stream);
        store.SaveTurn(1, w, stream, "{}", EmptyEngine());

        var replayed = Game.Simulation.WorldReducer.Replay(store.LoadAllEvents());
        Assert.Equal(w.Countries[CountryId.Alderia].Gdp, replayed.Countries[CountryId.Alderia].Gdp);
        Assert.Equal(0.3, replayed.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction);
        Assert.Equal(1, replayed.Turn);
    }

    [Fact]
    public void Snapshot_RoundTripsWorldAndEngineState()
    {
        using var store = NewStore();
        var w = Scenario.CreateMeridianStrait();
        w.Turn = 7;
        w.Countries[CountryId.Alderia].Gdp = 987.65;
        w.StraitTension = 44;
        var engine = new EngineSnapshot(123456789UL, 42UL,
            new SortedDictionary<CountryId, int> { [CountryId.Cascara] = 2 },
            new SortedDictionary<CountryId, ObserverKnowledgeDto>(),
            new List<DomainEvent> { new TurnStarted(7, null, Visibility.Public, 7) },
            new SortedDictionary<CountryId, List<DomainEvent>>
            {
                [CountryId.Alderia] = new()
                {
                    new ExportRestrictionSet(5, CountryId.Borvia, Visibility.Public,
                        CountryId.Borvia, CountryId.Alderia, 0.6, "s")
                }
            });
        store.SaveTurn(7, w, new List<DomainEvent>(), "{}", engine);
        var loaded = store.LoadLatestSnapshot();
        Assert.Equal(987.65, loaded!.Value.World.Countries[CountryId.Alderia].Gdp);
        Assert.Equal(44, loaded.Value.World.StraitTension);
        Assert.Equal(123456789UL, loaded.Value.Engine.RngState);
        Assert.Equal(2, loaded.Value.Engine.AsleepStreaks[CountryId.Cascara]);
        Assert.Single(loaded.Value.Engine.LastTurnEvents);
        Assert.Single(loaded.Value.Engine.RelevantWindows[CountryId.Alderia]); // windows round-trip
    }

    [Fact]
    public void DoubleWritingATurn_FailsLoudly()
    {
        using var store = NewStore();
        var events = new List<DomainEvent> { new TurnStarted(1, null, Visibility.Public, 1) };
        store.SaveTurn(1, Scenario.CreateMeridianStrait(), events, "{}", EmptyEngine());
        Assert.ThrowsAny<Exception>(() =>
            store.SaveTurn(1, Scenario.CreateMeridianStrait(), events, "{}", EmptyEngine()));
    }

    [Fact]
    public void SaveTurn_RejectsEventsFromADifferentTurn()
    {
        using var store = NewStore();
        var mixedTurnEvents = new List<DomainEvent>
        {
            new TurnStarted(1, null, Visibility.Public, 1),
            new GdpChanged(2, null, Visibility.Public, CountryId.Alderia, -10),
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            store.SaveTurn(1, Scenario.CreateMeridianStrait(), mixedTurnEvents, "{}", EmptyEngine()));
        Assert.Contains("turn 1", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(store.LoadAllEvents());
        Assert.Null(store.LoadLatestSnapshot());
    }

    [Fact]
    public void Memories_LoadMostRecentFirst()
    {
        using var store = NewStore();
        store.SaveMemory(CountryId.Borvia, 1, "old");
        store.SaveMemory(CountryId.Borvia, 5, "new");
        Assert.Equal("new", store.LoadMemories(CountryId.Borvia, 10)[0].Text);
    }

    private static EngineSnapshot EmptyEngine() => new(1UL, 42UL,
        new SortedDictionary<CountryId, int>(),
        new SortedDictionary<CountryId, ObserverKnowledgeDto>(),
        new List<DomainEvent>(),
        new SortedDictionary<CountryId, List<DomainEvent>>());
}
