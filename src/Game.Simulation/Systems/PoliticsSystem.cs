using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation.Systems;

/// <summary>
/// Approval drift and election cycles. Elections (democracies only) are judged
/// against the POST-update approval — approval plus this turn's delta, clamped —
/// computed locally, since this system is pure and cannot observe the reducer
/// applying its own ApprovalChanged event first.
///
/// Constants:
///   GrowthApprovalWeight   = 0.2  (approval gained per percentage point of annual growth)
///   InflationFreeBand      = 0.03 (inflation below this causes no approval penalty)
///   InflationPenaltyWeight = 400  (approval lost per unit of inflation above the free band)
///   WarAnxietyThreatGate   = 60   (max incoming threat that triggers war anxiety)
///   WarAnxietyTensionGate  = 50   (strait tension, combined with own forces present, that triggers war anxiety)
///   WarAnxietyPenalty      = 2    (flat approval penalty when war anxiety is triggered)
///   ElectionRetainThreshold= 45   (post-update approval at/above which the incumbent survives)
///   TrustResetFactor       = 0.3  (fraction of outgoing trust erased toward zero on a government change)
/// </summary>
public static class PoliticsSystem
{
    private const double GrowthApprovalWeight = 0.2;
    private const double InflationFreeBand = 0.03;
    private const double InflationPenaltyWeight = 400.0;
    private const double WarAnxietyThreatGate = 60.0;
    private const double WarAnxietyTensionGate = 50.0;
    private const double WarAnxietyPenalty = 2.0;
    private const double ElectionRetainThreshold = 45.0;
    private const double TrustResetFactor = 0.3;

    public static IReadOnlyList<DomainEvent> EndTurn(WorldState w)
    {
        var events = new List<DomainEvent>();

        foreach (var (id, country) in w.Countries)
        {
            var maxIncomingThreat = w.Relationships
                .Where(r => r.Target == id)
                .Select(r => r.Threat)
                .DefaultIfEmpty(0.0)
                .Max();

            var warAnxiety = (country.ForcesInStrait > 0 && w.StraitTension >= WarAnxietyTensionGate)
                || maxIncomingThreat >= WarAnxietyThreatGate
                ? WarAnxietyPenalty
                : 0.0;

            var delta = country.GrowthAnnual * 100 * GrowthApprovalWeight
                - Math.Max(0, country.Inflation - InflationFreeBand) * InflationPenaltyWeight
                - warAnxiety;

            events.Add(new ApprovalChanged(w.Turn, null, Visibility.ActorOnly(id), id, delta));

            if (country.Government != GovernmentKind.Democracy) continue;

            events.Add(new ElectionClockTicked(w.Turn, null, Visibility.Public, id));

            if (country.ElectionCountdown != 1) continue;

            var postApproval = Clamp(country.Approval + delta, 0, 100);
            var retained = postApproval >= ElectionRetainThreshold;

            events.Add(new ElectionHeld(w.Turn, null, Visibility.Public, id, retained));

            if (retained) continue;

            var successor = GovernmentTemplates.Successor(id, country.GovernmentTemplateId);
            events.Add(new GovernmentChanged(w.Turn, null, Visibility.Public, id, successor));

            foreach (var (otherId, _) in w.Countries)
            {
                if (otherId == id) continue;
                var trust = w.Rel(id, otherId).Trust;
                events.Add(new TrustChanged(w.Turn, null, Visibility.ActorOnly(id), id, otherId, -TrustResetFactor * trust));
            }
        }

        return events;
    }

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
}
