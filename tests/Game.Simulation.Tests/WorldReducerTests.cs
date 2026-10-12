using Game.Contracts;
using Game.Simulation;
using Game.Simulation.State;

namespace Game.Simulation.Tests;

public class WorldReducerTests
{
    private static WorldState World() => Scenario.CreateMeridianStrait();

    [Fact]
    public void TurnStarted_IsTheOnlyWayTurnAdvances()
    {
        var w = World();
        WorldReducer.Apply(w, new TurnStarted(1, null, Visibility.Public, 1));
        Assert.Equal(1, w.Turn);
    }

    [Fact]
    public void GdpChanged_MutatesOnlyThatCountry()
    {
        var w = World();
        var before = w.Countries[CountryId.Borvia].Gdp;
        WorldReducer.Apply(w, new GdpChanged(1, CountryId.Alderia, Visibility.Public, CountryId.Alderia, -50));
        Assert.Equal(950, w.Countries[CountryId.Alderia].Gdp);
        Assert.Equal(before, w.Countries[CountryId.Borvia].Gdp);
    }

    [Fact]
    public void TrustChanged_IsDirectionalAndClamped()
    {
        var w = World();
        WorldReducer.Apply(w, new TrustChanged(1, null, Visibility.ActorOnly(CountryId.Borvia),
            CountryId.Borvia, CountryId.Alderia, -200));
        Assert.Equal(-100, w.Rel(CountryId.Borvia, CountryId.Alderia).Trust);
        Assert.Equal(-30, w.Rel(CountryId.Alderia, CountryId.Borvia).Trust);
    }

    [Fact]
    public void ExportRestrictionSet_IsPersistentPolicyNotADelta()
    {
        var w = World();
        WorldReducer.Apply(w, new ExportRestrictionSet(1, CountryId.Borvia, Visibility.Public,
            CountryId.Borvia, CountryId.Alderia, 0.45, "punishment"));
        Assert.Equal(0.45, w.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction);
        WorldReducer.Apply(w, new ExportRestrictionSet(2, CountryId.Borvia, Visibility.Public,
            CountryId.Borvia, CountryId.Alderia, 0.45, "again"));
        Assert.Equal(0.45, w.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction); // set, not accumulated
        WorldReducer.Apply(w, new ExportRestrictionSet(3, CountryId.Borvia, Visibility.Public,
            CountryId.Borvia, CountryId.Alderia, 0.1, "easing"));
        Assert.Equal(0.1, w.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction); // lowering works
    }

    [Fact]
    public void SanctionLevelSet_IsDirectedOwnedAndSettable()
    {
        var w = World();
        WorldReducer.Apply(w, new SanctionLevelSet(1, CountryId.Alderia, Visibility.Public,
            CountryId.Alderia, CountryId.Borvia, 0.6, "s"));
        Assert.Equal(0.6, w.SanctionLevel(CountryId.Alderia, CountryId.Borvia));
        Assert.Equal(0, w.SanctionLevel(CountryId.Borvia, CountryId.Alderia)); // directed
        WorldReducer.Apply(w, new SanctionLevelSet(2, CountryId.Alderia, Visibility.Public,
            CountryId.Alderia, CountryId.Borvia, 0.2, "eased"));
        Assert.Equal(0.2, w.SanctionLevel(CountryId.Alderia, CountryId.Borvia)); // set, not accumulated
        WorldReducer.Apply(w, new SanctionLevelSet(3, CountryId.Alderia, Visibility.Public,
            CountryId.Alderia, CountryId.Borvia, 0, "lifted"));
        Assert.Empty(w.Sanctions); // zero removes the record
    }

    [Fact]
    public void SanctionLevelSet_NegativeValueClampedToZeroRemovesRecord()
    {
        var w = World();
        // Create a sanction first
        WorldReducer.Apply(w, new SanctionLevelSet(1, CountryId.Alderia, Visibility.Public,
            CountryId.Alderia, CountryId.Borvia, 0.5, "initial"));
        Assert.Single(w.Sanctions);

        // Apply negative level; should clamp to 0 and remove the record
        WorldReducer.Apply(w, new SanctionLevelSet(2, CountryId.Alderia, Visibility.Public,
            CountryId.Alderia, CountryId.Borvia, -0.3, "removed"));
        Assert.Empty(w.Sanctions); // Negative clamped to 0 removes the record
    }

    [Fact]
    public void GovernmentChanged_SwapsTemplateAndResetsCountdown()
    {
        var w = World();
        WorldReducer.Apply(w, new GovernmentChanged(1, null, Visibility.Public,
            CountryId.Alderia, "alderia-prosperity-party"));
        Assert.Equal("alderia-prosperity-party", w.Countries[CountryId.Alderia].GovernmentTemplateId);
        Assert.Equal(48, w.Countries[CountryId.Alderia].ElectionCountdown);
    }

    [Fact]
    public void MarkerEvents_DoNotMutateState()
    {
        var w = World();
        var gdp = w.Countries[CountryId.Alderia].Gdp;
        WorldReducer.Apply(w, new ShortageStarted(1, null, Visibility.Public, CountryId.Alderia, 0.3));
        Assert.Equal(gdp, w.Countries[CountryId.Alderia].Gdp);
    }

    [Fact]
    public void Replay_ScenarioPlusStream_ReconstructsState()
    {
        var stream = new List<DomainEvent>
        {
            new TurnStarted(1, null, Visibility.Public, 1),
            new GdpChanged(1, null, Visibility.Public, CountryId.Alderia, -100),
            new ExportRestrictionSet(1, CountryId.Borvia, Visibility.Public,
                CountryId.Borvia, CountryId.Alderia, 0.3, "s"),
        };
        var w = WorldReducer.Replay(stream);
        Assert.Equal(1, w.Turn);
        Assert.Equal(900, w.Countries[CountryId.Alderia].Gdp);
        Assert.Equal(0.3, w.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction);
    }
}
