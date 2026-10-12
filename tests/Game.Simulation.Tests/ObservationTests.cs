using Game.Contracts;
using Game.Simulation;
using Game.Simulation.Observation;
using Game.Simulation.State;

namespace Game.Simulation.Tests;

public class ObservationTests
{
    private const ulong Seed = 42;

    [Fact]
    public void Initialize_CoversTheFullStrategicPropertySet_AboutOthersOnly()
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, Seed);
        var k = all[CountryId.Alderia];
        foreach (var p in new[]
        {
            ObservationProperties.Gdp(CountryId.Borvia),
            ObservationProperties.Inflation(CountryId.Borvia),
            ObservationProperties.Production(CountryId.Borvia),
            ObservationProperties.Dependence(CountryId.Borvia),
            ObservationProperties.Strength(CountryId.Borvia),
            ObservationProperties.Readiness(CountryId.Borvia),
            ObservationProperties.ForcesInStrait(CountryId.Borvia),
            ObservationProperties.Approval(CountryId.Borvia),
            ObservationProperties.Treasury(CountryId.Borvia),
            ObservationProperties.Trust(CountryId.Borvia, CountryId.Alderia),
        })
            Assert.True(k.Estimates.ContainsKey(p), $"missing {p}");
        Assert.False(k.Estimates.ContainsKey(ObservationProperties.Readiness(CountryId.Alderia)));
    }

    [Fact]
    public void Estimates_AreOffCenter_ButAlwaysBracketTruth()
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, Seed);
        var offCenterCount = 0;
        foreach (var (observer, k) in all)
        foreach (var (prop, est) in k.Estimates)
        {
            if (est.Confidence == Confidence.Unknown) continue;
            var truth = ObservationTestHelper.TruthFor(w, prop); // helper reads WorldState by property name
            Assert.InRange(truth, est.Low, est.High);
            if (Math.Abs((est.Low + est.High) / 2 - truth) > 1e-9) offCenterCount++;
        }
        Assert.True(offCenterCount > 10, "midpoints must not systematically equal truth");
    }

    [Fact]
    public void Noise_IsDeterministic()
    {
        var a = ObservationNoise.Offset(Seed, CountryId.Alderia, "X", 3, 10);
        var b = ObservationNoise.Offset(Seed, CountryId.Alderia, "X", 3, 10);
        var c = ObservationNoise.Offset(Seed, CountryId.Borvia, "X", 3, 10);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.InRange(a, -6, 6);
    }

    [Fact]
    public void SpySuccess_RefreshesToHighConfidence_WithoutLeakingToThirdParties()
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, Seed);
        w.Turn = 3;
        var spy = new SpySucceeded(3, CountryId.Alderia, Visibility.ActorOnly(CountryId.Alderia), CountryId.Borvia);
        var cascaraBefore = all[CountryId.Cascara].Estimates[ObservationProperties.Treasury(CountryId.Borvia)];
        ObservationSystem.UpdateFromEvents(w, all, new List<DomainEvent> { spy }, Seed);
        Assert.Equal(cascaraBefore, all[CountryId.Cascara].Estimates[ObservationProperties.Treasury(CountryId.Borvia)]);
        var spied = all[CountryId.Alderia].Estimates[ObservationProperties.Treasury(CountryId.Borvia)];
        Assert.Equal(Confidence.High, spied.Confidence);
        Assert.Equal(EstimateSource.Espionage, spied.Source);
        Assert.InRange(w.Countries[CountryId.Borvia].Treasury, spied.Low, spied.High);
    }

    [Fact]
    public void Decay_FiresAtIntervals_NotEveryTurnAfterThreshold()
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, Seed);
        var k = all[CountryId.Alderia];
        var key = ObservationProperties.Readiness(CountryId.Borvia);
        k.Estimates[key] = new Estimate(key, 50, 60, Confidence.High, 0, EstimateSource.Espionage);

        w.Turn = 4; ObservationSystem.Decay(w, k);
        var afterFirst = k.Estimates[key];
        Assert.Equal(Confidence.Medium, afterFirst.Confidence);
        Assert.True(afterFirst.Low < 50 && afterFirst.High > 60);

        w.Turn = 5; ObservationSystem.Decay(w, k);
        Assert.Equal(afterFirst, k.Estimates[key]); // no re-decay between intervals

        w.Turn = 8; ObservationSystem.Decay(w, k);
        Assert.Equal(Confidence.Low, k.Estimates[key].Confidence); // next interval fires
    }

    [Fact]
    public void ActorView_UsesImmutableCollections_NotCastableWrappers()
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, Seed);
        var view = ObservationSystem.Freeze(w, all[CountryId.Alderia]);
        Assert.IsType<System.Collections.Immutable.ImmutableSortedDictionary<string, Estimate>>(
            view.ForeignEstimates);
        Assert.False(view.OutgoingRelations.IsDefault);   // ImmutableArray struct, materialized
        Assert.False(view.Public.Pacts.IsDefault);        // likewise — a struct, not a castable wrapper
        // A cast-to-Dictionary assertion here is unnecessary (and would not compile):
        // ForeignEstimates' compile-time type is the concrete, sealed
        // ImmutableSortedDictionary<string, Estimate>, which has no reference
        // conversion to Dictionary<string, Estimate> at all — the compiler already
        // forbids reaching a mutable collection, so there is nothing to probe at runtime.
    }

    [Fact]
    public void RefreshedEstimate_RestartsItsDecayClock()
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, Seed);
        var k = all[CountryId.Alderia];
        var key = ObservationProperties.Readiness(CountryId.Borvia);
        k.Estimates[key] = new Estimate(key, 50, 60, Confidence.High, 0, EstimateSource.Espionage);
        w.Turn = 4; ObservationSystem.Decay(w, k);                        // first decay fires
        w.Turn = 5;
        var refresh = new ReadinessChanged(5, CountryId.Borvia, Visibility.Public, CountryId.Borvia, 10);
        Game.Simulation.WorldReducer.Apply(w, refresh);
        ObservationSystem.UpdateFromEvents(w, all, new List<DomainEvent> { refresh }, Seed);
        w.Turn = 8; ObservationSystem.Decay(w, k);                        // age 3 from refresh → no decay
        Assert.Equal(Confidence.Medium, k.Estimates[key].Confidence);     // PublicEvent refresh level intact
        w.Turn = 9; ObservationSystem.Decay(w, k);                        // age 4 from refresh → decays now
        Assert.Equal(Confidence.Low, k.Estimates[key].Confidence);
    }

    [Fact]
    public void Freeze_OrdersPacts_DeterministicallyAfterBreakAndSignChurn()
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, Seed);

        // Scenario starts with a single Alderia-Cascara pact. Break it, then sign
        // two new ones. w.Pacts is a HashSet<(CountryId,CountryId)>: after a
        // Remove+Add churn like this, its enumeration order is not guaranteed to
        // match insertion order (slot reuse), which could make ActorView.Public.Pacts
        // — and the prompt text built from it — diverge between a straight run and a
        // resumed run that rebuilds the set via different Add/Remove history. Freeze
        // must always emit pacts sorted by (A, B) regardless of the set's internal order.
        w.Pacts.Remove(WorldState.PactKey(CountryId.Alderia, CountryId.Cascara));
        w.Pacts.Add(WorldState.PactKey(CountryId.Borvia, CountryId.Cascara));
        w.Pacts.Add(WorldState.PactKey(CountryId.Alderia, CountryId.Borvia));

        var view = ObservationSystem.Freeze(w, all[CountryId.Alderia]);
        var pacts = view.Public.Pacts.Select(p => (p.A, p.B)).ToList();

        Assert.Equal(new[]
        {
            (CountryId.Alderia, CountryId.Borvia),
            (CountryId.Borvia, CountryId.Cascara),
        }, pacts);
    }

    [Fact]
    public void FrozenActorView_IsASnapshot_WorldMutationDoesNotAlterIt()
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, Seed);
        var view = ObservationSystem.Freeze(w, all[CountryId.Alderia]);
        var gdpInView = view.Own.Gdp;
        var trustInView = view.OutgoingRelations.First(r => r.Target == CountryId.Borvia).Trust;

        w.Countries[CountryId.Alderia].Gdp = 1;
        w.Rel(CountryId.Alderia, CountryId.Borvia).Trust = 99;

        Assert.Equal(gdpInView, view.Own.Gdp);
        Assert.Equal(trustInView, view.OutgoingRelations.First(r => r.Target == CountryId.Borvia).Trust);
    }
}
