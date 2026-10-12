using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation.Resolution;

/// <summary>
/// Pure per-action resolvers: turn an AcceptedCommand into the list of DomainEvents
/// it produces. Resolvers never mutate WorldState — they only read it. Persistent
/// policy actions (ExportControl, Sanction) use SET semantics on the actor's own
/// directed/owned state; relationship deltas scale with the change in level, not
/// the raw magnitude. Immediate economic damage (GDP, price, shortages) is NOT
/// emitted here — it emerges from the passive systems over later turns.
///
/// All relationship events default to visibility ActorOnly(Source) unless the
/// action table below states otherwise.
/// </summary>
public static class Resolvers
{
    public static IReadOnlyList<DomainEvent> Resolve(WorldState w, AcceptedCommand cmd, DeterministicRng rng)
    {
        var intent = cmd.Intent;
        var a = intent.Actor;
        var m = cmd.EffectiveMagnitude;
        var turn = w.Turn;
        var stmt = intent.Statement;

        return intent.Type switch
        {
            ActionType.Pass => Array.Empty<DomainEvent>(),
            ActionType.Trade => ResolveTrade(w, a, intent.Target!.Value, m, turn, stmt),
            ActionType.Sanction => ResolveSanction(w, a, intent.Target!.Value, m, turn, stmt),
            ActionType.ExportControl => ResolveExportControl(w, a, intent.Target!.Value, m, turn, stmt),
            ActionType.Subsidize => ResolveSubsidize(a, m, turn, stmt),
            ActionType.Reassure => ResolveReassure(a, intent.Target!.Value, m, turn, stmt),
            ActionType.Threaten => ResolveThreaten(a, intent.Target!.Value, m, turn, stmt),
            ActionType.SignPact => ResolveSignPact(a, intent.Target!.Value, turn, stmt),
            ActionType.BreakPact => ResolveBreakPact(a, intent.Target!.Value, turn, stmt),
            ActionType.Mobilize => ResolveMobilize(a, m, turn),
            ActionType.DeployToRegion => ResolveDeployToRegion(a, m, turn, stmt),
            ActionType.Withdraw => ResolveWithdraw(w, a, m, turn),
            ActionType.Spy => ResolveSpy(a, intent.Target!.Value, m, turn, rng),
            _ => Array.Empty<DomainEvent>(),
        };
    }

    private static IReadOnlyList<DomainEvent> ResolveTrade(
        WorldState w, CountryId a, CountryId t, double m, int turn, string stmt)
    {
        var events = new List<DomainEvent>();
        var link = w.Link(a, t)!; // validation guarantees this exists

        events.Add(new TradeFlowChanged(turn, a, Visibility.Public, a, t, 10 * m, stmt));

        if (link.ExportRestriction > 0)
        {
            var newLevel = Math.Max(0, link.ExportRestriction - 0.5 * m);
            events.Add(new ExportRestrictionSet(turn, a, Visibility.Public, a, t, newLevel, stmt));
        }

        events.Add(new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, 5 * m));
        events.Add(new TrustChanged(turn, a, Visibility.ActorOnly(a), a, t, 3 * m));

        return events;
    }

    private static IReadOnlyList<DomainEvent> ResolveSanction(
        WorldState w, CountryId a, CountryId t, double m, int turn, string stmt)
    {
        var events = new List<DomainEvent>();
        var old = w.SanctionLevel(a, t);
        var newLevel = m;

        events.Add(new SanctionLevelSet(turn, a, Visibility.Public, a, t, newLevel, stmt));

        if (newLevel > old)
        {
            events.Add(new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, -10 * (newLevel - old)));
            events.Add(new ThreatChanged(turn, a, Visibility.ActorOnly(t), t, a, 5 * (newLevel - old)));
        }
        else if (newLevel < old)
        {
            events.Add(new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, 5 * (old - newLevel)));
        }

        return events;
    }

    private static IReadOnlyList<DomainEvent> ResolveExportControl(
        WorldState w, CountryId a, CountryId t, double m, int turn, string stmt)
    {
        var events = new List<DomainEvent>();
        var link = w.Link(a, t)!; // validation guarantees this exists
        var old = link.ExportRestriction;
        var newLevel = m;

        events.Add(new ExportRestrictionSet(turn, a, Visibility.Public, a, t, newLevel, stmt));

        if (newLevel > old)
        {
            events.Add(new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, -8 * (newLevel - old)));
            events.Add(new ApprovalChanged(turn, a, Visibility.ActorOnly(a), a, 1 * (newLevel - old))); // rally
        }
        else if (newLevel < old)
        {
            events.Add(new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, 4 * (old - newLevel)));
        }

        return events;
    }

    private static IReadOnlyList<DomainEvent> ResolveSubsidize(CountryId a, double m, int turn, string stmt) =>
        new List<DomainEvent>
        {
            new SubsidyEnacted(turn, a, Visibility.Public, m, stmt),
            new TreasuryChanged(turn, a, Visibility.ActorOnly(a), a, -10 * m),
            new InflationChanged(turn, a, Visibility.Public, a, 0.002 * m),
            new DependenceChanged(turn, a, Visibility.ActorOnly(a), a, -0.02 * m),
        };

    private static IReadOnlyList<DomainEvent> ResolveReassure(CountryId a, CountryId t, double m, int turn, string stmt) =>
        new List<DomainEvent>
        {
            new ReassuranceGiven(turn, a, Visibility.Public, t, stmt),
            new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, 8 * m),
            new ThreatChanged(turn, a, Visibility.ActorOnly(t), t, a, -5 * m),
        };

    private static IReadOnlyList<DomainEvent> ResolveThreaten(CountryId a, CountryId t, double m, int turn, string stmt) =>
        new List<DomainEvent>
        {
            new ThreatIssued(turn, a, Visibility.Public, t, stmt),
            new ThreatChanged(turn, a, Visibility.ActorOnly(t), t, a, 12 * m),
            new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, -6 * m),
        };

    private static IReadOnlyList<DomainEvent> ResolveSignPact(CountryId a, CountryId t, int turn, string stmt) =>
        new List<DomainEvent>
        {
            new PactSigned(turn, a, Visibility.Public, a, t, stmt),
            new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, 10),
            new TrustChanged(turn, a, Visibility.ActorOnly(a), a, t, 10),
        };

    private static IReadOnlyList<DomainEvent> ResolveBreakPact(CountryId a, CountryId t, int turn, string stmt) =>
        new List<DomainEvent>
        {
            new PactBroken(turn, a, Visibility.Public, a, t, stmt),
            new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, -25),
            new ThreatChanged(turn, a, Visibility.ActorOnly(t), t, a, 10),
        };

    private static IReadOnlyList<DomainEvent> ResolveMobilize(CountryId a, double m, int turn)
    {
        var events = new List<DomainEvent>
        {
            new ReadinessChanged(turn, a, Visibility.Public, a, 15 * m),
            new TreasuryChanged(turn, a, Visibility.ActorOnly(a), a, -5 * m),
        };

        foreach (var x in Enum.GetValues<CountryId>())
        {
            if (x == a) continue;
            events.Add(new ThreatChanged(turn, a, Visibility.ActorOnly(x), x, a, 8 * m));
        }

        return events;
    }

    private static IReadOnlyList<DomainEvent> ResolveDeployToRegion(CountryId a, double m, int turn, string stmt)
    {
        var claimant = Claimant(a);
        return new List<DomainEvent>
        {
            new ForcesInStraitChanged(turn, a, Visibility.Public, a, 20 * m),
            new ForcesDeployedNotice(turn, a, Visibility.Public, claimant, stmt),
            new ThreatChanged(turn, a, Visibility.ActorOnly(claimant), claimant, a, 15 * m),
            new TrustChanged(turn, a, Visibility.ActorOnly(claimant), claimant, a, -10 * m),
        };
    }

    private static IReadOnlyList<DomainEvent> ResolveWithdraw(WorldState w, CountryId a, double m, int turn)
    {
        var claimant = Claimant(a);
        var forces = w.Countries[a].ForcesInStrait;
        var delta = -Math.Min(forces, 20 * m);

        return new List<DomainEvent>
        {
            new ForcesInStraitChanged(turn, a, Visibility.Public, a, delta),
            new TrustChanged(turn, a, Visibility.ActorOnly(claimant), claimant, a, 5 * m),
        };
    }

    // Strait claimant: Borvia by default, or Cascara when Borvia itself is the actor.
    private static CountryId Claimant(CountryId actor) =>
        actor == CountryId.Borvia ? CountryId.Cascara : CountryId.Borvia;

    private static IReadOnlyList<DomainEvent> ResolveSpy(CountryId a, CountryId t, double m, int turn, DeterministicRng rng)
    {
        var events = new List<DomainEvent>();

        // Exactly two rolls in fixed order, always consumed regardless of outcome.
        var success = rng.NextDouble() < 0.5 + 0.3 * m;
        var caught = rng.NextDouble() < 0.1 + 0.25 * m;

        if (success)
        {
            events.Add(new SpySucceeded(turn, a, Visibility.ActorOnly(a), t));
        }

        if (caught)
        {
            events.Add(new SpyCaught(turn, a, Visibility.Participants(a, t), t));
            events.Add(new TrustChanged(turn, a, Visibility.ActorOnly(t), t, a, -15));
        }

        return events;
    }
}
