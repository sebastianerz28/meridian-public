using Game.Contracts;
using Game.Simulation;
using Game.Simulation.State;

namespace Game.Simulation.Tests;

public class ScenarioTests
{
    [Fact]
    public void Scenario_HasThreeCountriesSixRelationshipsTwoTradeLinks()
    {
        var world = Scenario.CreateMeridianStrait();
        Assert.Equal(3, world.Countries.Count);
        Assert.Equal(6, world.Relationships.Count);
        Assert.Equal(2, world.TradeLinks.Count);
        Assert.Equal(0, world.Turn);
        Assert.Equal(20, world.StraitTension);
    }

    [Fact]
    public void Relationships_AreDirectional()
    {
        var world = Scenario.CreateMeridianStrait();
        Assert.Equal(60, world.Rel(CountryId.Alderia, CountryId.Cascara).Trust);
        Assert.Equal(65, world.Rel(CountryId.Cascara, CountryId.Alderia).Trust);
    }

    [Fact]
    public void TradeLinks_AreDirectedAndStartUnrestricted()
    {
        var world = Scenario.CreateMeridianStrait();
        var link = world.Link(CountryId.Borvia, CountryId.Alderia);
        Assert.NotNull(link);
        Assert.Equal(60, link!.BaseFlow);
        Assert.Equal(0, link.ExportRestriction);
        Assert.Null(world.Link(CountryId.Alderia, CountryId.Borvia));
    }

    [Fact]
    public void AlderiaAndCascara_StartAllied()
    {
        var world = Scenario.CreateMeridianStrait();
        Assert.True(world.HasPact(CountryId.Alderia, CountryId.Cascara));
        Assert.False(world.HasPact(CountryId.Alderia, CountryId.Borvia));
    }

    [Fact]
    public void Rng_IsDeterministicAndResumableFromExposedState()
    {
        var a = new DeterministicRng(42);
        var b = new DeterministicRng(42);
        for (var i = 0; i < 50; i++) Assert.Equal(a.NextDouble(), b.NextDouble());

        var mid = a.State;
        var resumed = new DeterministicRng(mid);
        for (var i = 0; i < 50; i++) Assert.Equal(a.NextDouble(), resumed.NextDouble());
    }
}
