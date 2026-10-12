using Game.Contracts;
using Game.Simulation;
using Game.Simulation.Resolution;
using Game.Simulation.State;

namespace Game.Simulation.Tests;

public class ResolverTests
{
    private static AcceptedCommand Cmd(CountryId a, ActionType t, CountryId? tgt, double m) =>
        new(1, new ActionIntent(a, t, tgt, m, "stmt"), m);

    public static IEnumerable<object[]> AllActionCases()
    {
        // actor/target chosen so each action is valid against the scenario
        yield return new object[] { ActionType.Pass, CountryId.Alderia, null!, typeof(DomainEvent), 0 };
        yield return new object[] { ActionType.Trade, CountryId.Borvia, CountryId.Alderia, typeof(TradeFlowChanged), 1 };
        yield return new object[] { ActionType.Sanction, CountryId.Alderia, CountryId.Borvia, typeof(SanctionLevelSet), 1 };
        yield return new object[] { ActionType.ExportControl, CountryId.Borvia, CountryId.Alderia, typeof(ExportRestrictionSet), 1 };
        yield return new object[] { ActionType.Subsidize, CountryId.Alderia, null!, typeof(SubsidyEnacted), 1 };
        yield return new object[] { ActionType.Reassure, CountryId.Cascara, CountryId.Alderia, typeof(ReassuranceGiven), 1 };
        yield return new object[] { ActionType.Threaten, CountryId.Borvia, CountryId.Cascara, typeof(ThreatIssued), 1 };
        yield return new object[] { ActionType.SignPact, CountryId.Alderia, CountryId.Cascara, typeof(PactSigned), 1 };
        yield return new object[] { ActionType.BreakPact, CountryId.Alderia, CountryId.Cascara, typeof(PactBroken), 1 };
        yield return new object[] { ActionType.Mobilize, CountryId.Borvia, null!, typeof(ReadinessChanged), 1 };
        yield return new object[] { ActionType.DeployToRegion, CountryId.Alderia, null!, typeof(ForcesInStraitChanged), 1 };
        yield return new object[] { ActionType.Withdraw, CountryId.Borvia, null!, typeof(ForcesInStraitChanged), 1 };
        // Spy covered by its own deterministic test below
    }

    [Theory]
    [MemberData(nameof(AllActionCases))]
    public void EveryAction_IsPure_EmitsExpectedMarker_AndStampsTurn(
        ActionType type, CountryId actor, CountryId? target, Type expectedEvent, int minCount)
    {
        var w = Scenario.CreateMeridianStrait();
        w.Turn = 5;
        var snapshotGdp = w.Countries.Values.Select(c => c.Gdp).ToArray();
        var snapshotTrust = w.Relationships.Select(r => r.Trust).ToArray();

        var events = Resolvers.Resolve(w, Cmd(actor, type, target, 0.5), new DeterministicRng(1));

        Assert.Equal(snapshotGdp, w.Countries.Values.Select(c => c.Gdp).ToArray()); // pure
        Assert.Equal(snapshotTrust, w.Relationships.Select(r => r.Trust).ToArray());
        Assert.True(events.Count(e => expectedEvent.IsInstanceOfType(e)) >= minCount,
            $"{type} must emit {expectedEvent.Name}");
        Assert.All(events, e => Assert.Equal(5, e.Turn));
    }

    [Fact]
    public void ExportControl_SetsPersistentPolicy_NotImmediateDamage()
    {
        var w = Scenario.CreateMeridianStrait();
        var events = Resolvers.Resolve(w,
            Cmd(CountryId.Borvia, ActionType.ExportControl, CountryId.Alderia, 0.45),
            new DeterministicRng(1));
        var set = events.OfType<ExportRestrictionSet>().Single();
        Assert.Equal(0.45, set.Level, 5);
        Assert.DoesNotContain(events, e => e is GdpChanged); // damage comes from the market system later
    }

    [Fact]
    public void PolicyActions_SetActorOwnedLevels_LoweringIsDeescalation()
    {
        var w = Scenario.CreateMeridianStrait();
        w.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction = 0.6;

        // Borvia re-submits at lower level → set to the lower value, trust recovers, no rally
        var easing = Resolvers.Resolve(w,
            Cmd(CountryId.Borvia, ActionType.ExportControl, CountryId.Alderia, 0.2),
            new DeterministicRng(1));
        Assert.Equal(0.2, easing.OfType<ExportRestrictionSet>().Single().Level, 5);
        Assert.Contains(easing, e => e is TrustChanged
            { Source: CountryId.Alderia, Target: CountryId.Borvia, Delta: > 0 });
        Assert.DoesNotContain(easing, e => e is ApprovalChanged { Delta: > 0 });
    }

    [Fact]
    public void Trade_TouchesOnlyTheActorsOwnOutgoingLink()
    {
        // importer-side Trade is already rejected in validation (Task 5);
        // here: the exporter's Trade changes its own link's flow/restriction and nothing else
        var w = Scenario.CreateMeridianStrait();
        w.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction = 0.6;
        var events = Resolvers.Resolve(w,
            Cmd(CountryId.Borvia, ActionType.Trade, CountryId.Alderia, 1.0),
            new DeterministicRng(1));
        var flow = events.OfType<TradeFlowChanged>().Single();
        Assert.Equal(CountryId.Borvia, flow.Exporter);
        Assert.Equal(CountryId.Alderia, flow.Importer);
        var eased = events.OfType<ExportRestrictionSet>().Single();
        Assert.Equal(CountryId.Borvia, eased.Exporter);   // easing its OWN restriction
        Assert.Equal(0.1, eased.Level, 5);                // 0.6 − 0.5×1.0
    }

    [Fact]
    public void Sanction_IsDirectedAndSettable()
    {
        var w = Scenario.CreateMeridianStrait();
        w.Sanctions.Add(new SanctionState
            { Source = CountryId.Alderia, Target = CountryId.Borvia, Level = 0.8 });
        var easing = Resolvers.Resolve(w,
            Cmd(CountryId.Alderia, ActionType.Sanction, CountryId.Borvia, 0.3),
            new DeterministicRng(1));
        var set = easing.OfType<SanctionLevelSet>().Single();
        Assert.Equal(0.3, set.Level, 5);
        Assert.Contains(easing, e => e is TrustChanged
            { Source: CountryId.Borvia, Target: CountryId.Alderia, Delta: > 0 }); // easing recovers trust
    }

    [Fact]
    public void Spy_OutcomeMatchesTheDocumentedRollContract_Unconditionally()
    {
        // contract: roll1 = success (< 0.5 + 0.3m), roll2 = caught (< 0.1 + 0.25m)
        foreach (var seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })
        {
            var probe = new DeterministicRng(seed);
            var expectSuccess = probe.NextDouble() < 0.5 + 0.3 * 0.5;
            var expectCaught = probe.NextDouble() < 0.1 + 0.25 * 0.5;

            var events = Resolvers.Resolve(Scenario.CreateMeridianStrait(),
                Cmd(CountryId.Alderia, ActionType.Spy, CountryId.Borvia, 0.5),
                new DeterministicRng(seed));

            Assert.Equal(expectSuccess, events.Any(e => e is SpySucceeded));
            Assert.Equal(expectCaught, events.Any(e => e is SpyCaught));
            if (expectSuccess)
            {
                var s = events.OfType<SpySucceeded>().Single();
                Assert.True(s.Visibility.CanObserve(CountryId.Alderia));
                Assert.False(s.Visibility.CanObserve(CountryId.Cascara));
            }
            if (expectCaught)
            {
                var c = events.OfType<SpyCaught>().Single();
                Assert.True(c.Visibility.CanObserve(CountryId.Borvia));
                Assert.False(c.Visibility.CanObserve(CountryId.Cascara));
            }
        }
    }
}
