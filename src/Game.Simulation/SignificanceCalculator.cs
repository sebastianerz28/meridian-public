using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation;

public static class SignificanceCalculator
{
    /// <summary>
    /// Calculate strategic significance of a single event for a given observer.
    /// </summary>
    public static StrategicSignificance ForObserver(WorldState world, DomainEvent evt, CountryId observer)
    {
        // 1. Visibility gate first: unobservable -> None, always
        if (!evt.Visibility.CanObserve(observer))
            return StrategicSignificance.None;

        // Calculate points based on event type and rules
        int points = evt switch
        {
            // 2. Bookkeeping type-gate (score 0 by type)
            TurnStarted => 0,
            ElectionClockTicked => 0,
            TreasuryChanged => 0,
            StockpileChanged => 0,

            // Thresholded bookkeeping (below threshold -> 0)
            GdpChanged gdp => ScoreGdpChanged(world, gdp, observer),
            InflationChanged inflation => ScoreInflationChanged(world, inflation, observer),
            ApprovalChanged approval => ScoreApprovalChanged(world, approval, observer),

            // Deliberate acts
            ExportRestrictionSet export => ScoreExportRestriction(export, observer),
            SanctionLevelSet sanction => ScoreSanctionLevel(sanction, observer),
            ThreatIssued threat => ScoreThreatIssued(threat, observer),
            ReassuranceGiven reassurance => ScoreReassuranceGiven(reassurance, observer),
            TradeFlowChanged trade => ScoreTradeFlowChanged(trade, observer),

            // Relationship changes
            TrustChanged trust => ScoreTrustChanged(trust, observer),
            ThreatChanged threat => ScoreThreatChanged(threat, observer),

            // Military/readiness
            ForcesInStraitChanged => 2,  // +2 for everyone (the Strait is everyone's flashpoint)
            ReadinessChanged readiness => ScoreReadinessChanged(readiness, observer),
            StraitTensionChanged => 0,  // deliberate: tension is ambient; StraitIncident and the heartbeat carry the wake signal

            // Major events (+3 for every observer, with special cases)
            ElectionHeld => 3,
            GovernmentChanged => 3,
            PactSigned => 3,
            PactBroken => 3,
            StraitIncident => 3,
            ShortageStarted shortage => ScoreShortageStarted(shortage, observer),
            ShortageEnded shortage => ScoreShortageEnded(shortage, observer),

            // Other strategic events
            IntentRejected intent => ScoreIntentRejected(intent, observer),
            SpyCaught spy => ScoreSpyCaught(spy, observer),
            SpySucceeded spy => ScoreSpySucceeded(spy, observer),

            // Default for any unhandled events
            _ => 0,
        };

        // Map points to significance
        return PointsToSignificance(points);
    }

    /// <summary>
    /// Calculate maximum strategic significance for observer across all events.
    /// </summary>
    public static StrategicSignificance MaxForObserver(WorldState world, IReadOnlyList<DomainEvent> events, CountryId observer)
    {
        StrategicSignificance max = StrategicSignificance.None;

        foreach (var evt in events)
        {
            var sig = ForObserver(world, evt, observer);
            if (sig > max)
                max = sig;
        }

        return max;
    }

    private static int ScoreGdpChanged(WorldState world, GdpChanged evt, CountryId observer)
    {
        var threshold = world.Countries[evt.Country].Gdp * 0.005;
        if (Math.Abs(evt.Delta) < threshold)
            return 0;

        // Above threshold: +2 if observer is affected country, +1 otherwise
        return observer == evt.Country ? 2 : 1;
    }

    private static int ScoreInflationChanged(WorldState world, InflationChanged evt, CountryId observer)
    {
        const double threshold = 0.005;
        if (Math.Abs(evt.Delta) < threshold)
            return 0;

        // Above threshold: +2 if observer is affected country, +1 otherwise
        return observer == evt.Country ? 2 : 1;
    }

    private static int ScoreApprovalChanged(WorldState world, ApprovalChanged evt, CountryId observer)
    {
        const double threshold = 5;
        if (Math.Abs(evt.Delta) < threshold)
            return 0;

        // Above threshold: +2 if observer is affected country, +1 otherwise
        return observer == evt.Country ? 2 : 1;
    }

    private static int ScoreExportRestriction(ExportRestrictionSet evt, CountryId observer)
    {
        // target/importer +3, others +1
        if (observer == evt.Importer)
            return 3;
        return 1;
    }

    private static int ScoreSanctionLevel(SanctionLevelSet evt, CountryId observer)
    {
        // target +3, others +1
        if (observer == evt.Target)
            return 3;
        return 1;
    }

    private static int ScoreThreatIssued(ThreatIssued evt, CountryId observer)
    {
        // target +2
        return observer == evt.Target ? 2 : 0;
    }

    private static int ScoreReassuranceGiven(ReassuranceGiven evt, CountryId observer)
    {
        // target +2
        return observer == evt.Target ? 2 : 0;
    }

    private static int ScoreTradeFlowChanged(TradeFlowChanged evt, CountryId observer)
    {
        // parties +1
        if (observer == evt.Exporter || observer == evt.Importer)
            return 1;
        return 0;
    }

    private static int ScoreTrustChanged(TrustChanged evt, CountryId observer)
    {
        // |Delta| >= 10 where observer is Source or Target: +2 (+1 if involving observer with smaller delta)
        if (Math.Abs(evt.Delta) >= 10 && (observer == evt.Source || observer == evt.Target))
            return 2;
        if ((observer == evt.Source || observer == evt.Target) && Math.Abs(evt.Delta) > 0)
            return 1;
        return 0;
    }

    private static int ScoreThreatChanged(ThreatChanged evt, CountryId observer)
    {
        // |Delta| >= 10 where observer is Source or Target: +2 (+1 if involving observer with smaller delta)
        if (Math.Abs(evt.Delta) >= 10 && (observer == evt.Source || observer == evt.Target))
            return 2;
        if ((observer == evt.Source || observer == evt.Target) && Math.Abs(evt.Delta) > 0)
            return 1;
        return 0;
    }

    private static int ScoreReadinessChanged(ReadinessChanged evt, CountryId observer)
    {
        // From actions (Public): observer != actor +1
        // Guard against null Actor (systemic events should score 0, not break invariant)
        if (evt.Visibility.Kind == VisibilityKind.Public && evt.Actor.HasValue && observer != evt.Actor.Value)
            return 1;
        return 0;
    }

    private static int ScoreShortageStarted(ShortageStarted evt, CountryId observer)
    {
        // +3 affected country, +1 others
        return observer == evt.Country ? 3 : 1;
    }

    private static int ScoreShortageEnded(ShortageEnded evt, CountryId observer)
    {
        // +3 affected country, +1 others
        return observer == evt.Country ? 3 : 1;
    }

    private static int ScoreIntentRejected(IntentRejected evt, CountryId observer)
    {
        // observer is the intent actor: +2
        return observer == evt.IntentActor ? 2 : 0;
    }

    private static int ScoreSpyCaught(SpyCaught evt, CountryId observer)
    {
        // participant: +3
        if (observer == evt.Actor || observer == evt.Target)
            return 3;
        return 0;
    }

    private static int ScoreSpySucceeded(SpySucceeded evt, CountryId observer)
    {
        // actor: +1
        return observer == evt.Actor ? 1 : 0;
    }

    private static StrategicSignificance PointsToSignificance(int points)
    {
        return points switch
        {
            0 => StrategicSignificance.None,
            1 => StrategicSignificance.Minor,
            2 or 3 => StrategicSignificance.Significant,
            _ when points >= 4 => StrategicSignificance.Critical,
            _ => StrategicSignificance.None,
        };
    }
}
