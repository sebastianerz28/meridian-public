using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation.Systems;

/// <summary>
/// Strait tension from force presence, plus a chance of an incident once tension
/// runs hot enough. The rng roll is ALWAYS consumed whenever the post-update
/// tension (computed locally, since this system is pure) would be >= 80 — this
/// keeps the rng sequence deterministic and independent of whether an incident
/// actually fires.
///
/// Constants:
///   ForcePresenceThreshold = 20   (baseline force count that doesn't add tension)
///   TensionGainPerForce    = 2    (tension target moves 2 points per force unit above baseline)
///   TensionAdjustSpeed     = 0.5  (fraction of the gap to target closed per turn)
///   TensionDecayBias       = 1    (constant tension bleed-off applied every turn)
///   IncidentRollThreshold  = 80   (post-update tension at/above which a roll is made)
///   IncidentChance         = 0.15 (probability of an incident given a roll)
///   IncidentApprovalHit    = 4    (approval lost by every country with forces in the strait)
///   IncidentTrustHit       = 10   (trust lost between every ordered pair of present countries)
///   IncidentThreatGain     = 10   (threat gained between every ordered pair of present countries)
/// </summary>
public static class StraitSystem
{
    private const double ForcePresenceThreshold = 20.0;
    private const double TensionGainPerForce = 2.0;
    private const double TensionAdjustSpeed = 0.5;
    private const double TensionDecayBias = 1.0;
    private const double IncidentRollThreshold = 80.0;
    private const double IncidentChance = 0.15;
    private const double IncidentApprovalHit = 4.0;
    private const double IncidentTrustHit = 10.0;
    private const double IncidentThreatGain = 10.0;

    public static IReadOnlyList<DomainEvent> EndTurn(WorldState w, DeterministicRng rng)
    {
        var events = new List<DomainEvent>();

        var total = w.Countries.Values.Sum(c => c.ForcesInStrait);
        var target = Clamp((total - ForcePresenceThreshold) * TensionGainPerForce, 0, 100);
        var tensionDelta = TensionAdjustSpeed * (target - w.StraitTension) - TensionDecayBias;

        events.Add(new StraitTensionChanged(w.Turn, null, Visibility.Public, tensionDelta));

        var postTension = Clamp(w.StraitTension + tensionDelta, 0, 100);
        if (postTension >= IncidentRollThreshold)
        {
            var roll = rng.NextDouble();
            if (roll < IncidentChance)
            {
                events.Add(new StraitIncident(w.Turn, null, Visibility.Public));

                var present = w.Countries
                    .Where(kv => kv.Value.ForcesInStrait > 0)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var c in present)
                {
                    events.Add(new ApprovalChanged(w.Turn, null, Visibility.ActorOnly(c), c, -IncidentApprovalHit));
                }

                foreach (var a in present)
                {
                    foreach (var b in present)
                    {
                        if (a == b) continue;
                        events.Add(new TrustChanged(w.Turn, null, Visibility.ActorOnly(a), a, b, -IncidentTrustHit));
                        events.Add(new ThreatChanged(w.Turn, null, Visibility.ActorOnly(a), a, b, IncidentThreatGain));
                    }
                }
            }
        }

        return events;
    }

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
}
