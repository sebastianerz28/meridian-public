using Game.Contracts;
using Game.Simulation;
using Game.Simulation.State;

namespace Game.Simulation.Tests;

public class SignificanceTests
{
    private static readonly WorldState W = Scenario.CreateMeridianStrait();

    [Fact]
    public void RoutinePassiveBookkeeping_IsNone_EvenForTheAffectedCountry()
    {
        // exactly what EconomySystem emits monthly for Alderia
        Assert.Equal(StrategicSignificance.None, SignificanceCalculator.ForObserver(W,
            new GdpChanged(1, null, Visibility.Public, CountryId.Alderia, 1000 * 0.02 / 12), CountryId.Alderia));
        Assert.Equal(StrategicSignificance.None, SignificanceCalculator.ForObserver(W,
            new TreasuryChanged(1, null, Visibility.ActorOnly(CountryId.Alderia), CountryId.Alderia, 1.67), CountryId.Alderia));
        Assert.Equal(StrategicSignificance.None, SignificanceCalculator.ForObserver(W,
            new InflationChanged(1, null, Visibility.Public, CountryId.Alderia, 0.001), CountryId.Alderia));
        Assert.Equal(StrategicSignificance.None, SignificanceCalculator.ForObserver(W,
            new ElectionClockTicked(1, null, Visibility.Public, CountryId.Alderia), CountryId.Alderia));
    }

    [Fact]
    public void LargeGdpShock_IsSignificantForTheVictim()
    {
        Assert.True(SignificanceCalculator.ForObserver(W,
            new GdpChanged(1, null, Visibility.Public, CountryId.Alderia, -8), CountryId.Alderia)
            >= StrategicSignificance.Significant);
    }

    [Fact]
    public void UnobservableEvent_IsNoneRegardlessOfContent()
    {
        var secret = new SpySucceeded(1, CountryId.Alderia, Visibility.ActorOnly(CountryId.Alderia), CountryId.Borvia);
        Assert.Equal(StrategicSignificance.None,
            SignificanceCalculator.ForObserver(W, secret, CountryId.Borvia));
    }

    [Fact]
    public void ExportRestriction_IsCriticalForTargetMinorForBystander()
    {
        var e = new ExportRestrictionSet(1, CountryId.Borvia, Visibility.Public,
            CountryId.Borvia, CountryId.Alderia, 0.45, "s");
        Assert.True(SignificanceCalculator.ForObserver(W, e, CountryId.Alderia) >= StrategicSignificance.Significant);
        Assert.Equal(StrategicSignificance.Minor, SignificanceCalculator.ForObserver(W, e, CountryId.Cascara));
    }

    [Fact]
    public void GovernmentChange_IsSignificantForEveryone()
    {
        var e = new GovernmentChanged(1, null, Visibility.Public, CountryId.Alderia, "alderia-prosperity-party");
        Assert.True(SignificanceCalculator.ForObserver(W, e, CountryId.Borvia) >= StrategicSignificance.Significant);
    }

    [Fact]
    public void ReadinessChanged_SystemicWithNullActor_IsNoneForAll()
    {
        // Systemic ReadinessChanged (passive, Actor=null) with Public visibility
        // should score 0 for everyone, not +1 (breaks routine-drift-stays-None invariant)
        var systemic = new ReadinessChanged(1, null, Visibility.Public, CountryId.Alderia, 2.0);
        Assert.Equal(StrategicSignificance.None, SignificanceCalculator.ForObserver(W, systemic, CountryId.Alderia));
        Assert.Equal(StrategicSignificance.None, SignificanceCalculator.ForObserver(W, systemic, CountryId.Borvia));
        Assert.Equal(StrategicSignificance.None, SignificanceCalculator.ForObserver(W, systemic, CountryId.Cascara));
    }

    [Fact]
    public void ReadinessChanged_ActionWithActor_IsMinorForOthers()
    {
        // Action-origin ReadinessChanged (Actor: Borvia) with Public visibility
        // should score +1 for observers != Borvia
        var action = new ReadinessChanged(1, CountryId.Borvia, Visibility.Public, CountryId.Borvia, 2.0);
        Assert.Equal(StrategicSignificance.None, SignificanceCalculator.ForObserver(W, action, CountryId.Borvia));
        Assert.Equal(StrategicSignificance.Minor, SignificanceCalculator.ForObserver(W, action, CountryId.Alderia));
    }
}
