using System.Collections.Immutable;
using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation.Observation;

public static class ObservationSystem
{
    private enum PropertyClass
    {
        Gdp, Inflation, Treasury, Production, Dependence,
        Strength, Readiness, ForcesInStrait, Approval, Relation
    }

    // property class -> half-width by confidence. Values are either absolute or a
    // fraction of the observed truth (Gdp/Production scale with the underlying value).
    private static double HalfWidth(string property, Confidence confidence, double truth)
    {
        var cls = Classify(property);
        return (cls, confidence) switch
        {
            (PropertyClass.Gdp, Confidence.Low) => 0.20 * truth,
            (PropertyClass.Gdp, Confidence.Medium) => 0.10 * truth,
            (PropertyClass.Gdp, Confidence.High) => 0.04 * truth,

            (PropertyClass.Inflation, Confidence.Low) => 0.02,
            (PropertyClass.Inflation, Confidence.Medium) => 0.01,
            (PropertyClass.Inflation, Confidence.High) => 0.004,

            (PropertyClass.Production, Confidence.Low) => 0.4 * truth,
            (PropertyClass.Production, Confidence.Medium) => 0.2 * truth,
            (PropertyClass.Production, Confidence.High) => 0.08 * truth,

            (PropertyClass.Dependence, Confidence.Low) => 0.3,
            (PropertyClass.Dependence, Confidence.Medium) => 0.15,
            (PropertyClass.Dependence, Confidence.High) => 0.06,

            (PropertyClass.Strength, Confidence.Low) => 25,
            (PropertyClass.Strength, Confidence.Medium) => 15,
            (PropertyClass.Strength, Confidence.High) => 6,

            (PropertyClass.Readiness, Confidence.Low) => 22,
            (PropertyClass.Readiness, Confidence.Medium) => 14,
            (PropertyClass.Readiness, Confidence.High) => 5,

            (PropertyClass.ForcesInStrait, Confidence.Low) => 15,
            (PropertyClass.ForcesInStrait, Confidence.Medium) => 8,
            (PropertyClass.ForcesInStrait, Confidence.High) => 3,

            (PropertyClass.Approval, Confidence.Low) => 20,
            (PropertyClass.Approval, Confidence.Medium) => 10,
            (PropertyClass.Approval, Confidence.High) => 4,

            (PropertyClass.Treasury, Confidence.Low) => 60,
            (PropertyClass.Treasury, Confidence.Medium) => 35,
            (PropertyClass.Treasury, Confidence.High) => 12,

            (PropertyClass.Relation, Confidence.Low) => 30,
            (PropertyClass.Relation, Confidence.Medium) => 18,
            (PropertyClass.Relation, Confidence.High) => 7,

            _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, $"No half-width for {cls}/{confidence}"),
        };
    }

    private static PropertyClass Classify(string property)
    {
        if (property.Contains("->", StringComparison.Ordinal)) return PropertyClass.Relation;
        if (property.EndsWith(".Economy.Gdp", StringComparison.Ordinal)) return PropertyClass.Gdp;
        if (property.EndsWith(".Economy.Inflation", StringComparison.Ordinal)) return PropertyClass.Inflation;
        if (property.EndsWith(".Economy.Treasury", StringComparison.Ordinal)) return PropertyClass.Treasury;
        if (property.EndsWith(".Resource.Production", StringComparison.Ordinal)) return PropertyClass.Production;
        if (property.EndsWith(".Resource.Dependence", StringComparison.Ordinal)) return PropertyClass.Dependence;
        if (property.EndsWith(".Military.Strength", StringComparison.Ordinal)) return PropertyClass.Strength;
        if (property.EndsWith(".Military.Readiness", StringComparison.Ordinal)) return PropertyClass.Readiness;
        if (property.EndsWith(".Military.ForcesInStrait", StringComparison.Ordinal)) return PropertyClass.ForcesInStrait;
        if (property.EndsWith(".Politics.Approval", StringComparison.Ordinal)) return PropertyClass.Approval;
        throw new ArgumentException($"Unrecognized observation property '{property}'", nameof(property));
    }

    /// <summary>
    /// Builds an Estimate = [center - hw, center + hw] where center = truth + deterministic
    /// offset. Truth is always inside the range, but never systematically at the midpoint.
    /// Unknown confidence yields a fixed, un-noised range (no information yet).
    /// </summary>
    private static Estimate MakeEstimate(
        ulong gameSeed, CountryId observer, string property, double truth,
        Confidence confidence, int observedTurn, EstimateSource source)
    {
        if (confidence == Confidence.Unknown)
            return new Estimate(property, 0, 200, Confidence.Unknown, observedTurn, source);

        var hw = HalfWidth(property, confidence, truth);
        var offset = ObservationNoise.Offset(gameSeed, observer, property, observedTurn, hw);
        var center = truth + offset;
        return new Estimate(property, center - hw, center + hw, confidence, observedTurn, source);
    }

    // Writes/overwrites an estimate and restarts its decay clock — every refresh
    // (initial seed or later event/espionage update) clears any prior decay marker.
    private static void SetEstimate(
        ObserverKnowledge k, ulong gameSeed, CountryId observer, string property, double truth,
        Confidence confidence, int observedTurn, EstimateSource source)
    {
        k.Estimates[property] = MakeEstimate(gameSeed, observer, property, truth, confidence, observedTurn, source);
        k.LastDecayAppliedAge.Remove(property);
    }

    public static SortedDictionary<CountryId, ObserverKnowledge> Initialize(WorldState w, ulong gameSeed)
    {
        var result = new SortedDictionary<CountryId, ObserverKnowledge>();
        var countries = w.Countries.Keys.ToList(); // SortedDictionary keys => enum order

        foreach (var observer in countries)
        {
            var k = new ObserverKnowledge { Observer = observer };

            foreach (var other in countries)
            {
                if (other == observer) continue;
                var cs = w.Countries[other];

                SetEstimate(k, gameSeed, observer, ObservationProperties.Gdp(other), cs.Gdp, Confidence.Medium, 0, EstimateSource.Initial);
                SetEstimate(k, gameSeed, observer, ObservationProperties.Inflation(other), cs.Inflation, Confidence.Medium, 0, EstimateSource.Initial);
                SetEstimate(k, gameSeed, observer, ObservationProperties.Production(other), cs.ResourceProduction, Confidence.Medium, 0, EstimateSource.Initial);
                SetEstimate(k, gameSeed, observer, ObservationProperties.Dependence(other), cs.ResourceDependence, Confidence.Medium, 0, EstimateSource.Initial);
                SetEstimate(k, gameSeed, observer, ObservationProperties.Strength(other), cs.MilitaryStrength, Confidence.Low, 0, EstimateSource.Initial);
                SetEstimate(k, gameSeed, observer, ObservationProperties.Readiness(other), cs.Readiness, Confidence.Low, 0, EstimateSource.Initial);
                SetEstimate(k, gameSeed, observer, ObservationProperties.ForcesInStrait(other), cs.ForcesInStrait, Confidence.Medium, 0, EstimateSource.Initial);

                var approvalConfidence = cs.Government == GovernmentKind.Democracy ? Confidence.Medium : Confidence.Low;
                SetEstimate(k, gameSeed, observer, ObservationProperties.Approval(other), cs.Approval, approvalConfidence, 0, EstimateSource.Initial);

                // Treasury starts unknown: fixed 0-200 range, no noise needed while Unknown.
                SetEstimate(k, gameSeed, observer, ObservationProperties.Treasury(other), cs.Treasury, Confidence.Unknown, 0, EstimateSource.Initial);

                // The other country's attitude toward the observer.
                var relToObserver = w.Rel(other, observer);
                SetEstimate(k, gameSeed, observer, ObservationProperties.Trust(other, observer), relToObserver.Trust, Confidence.Low, 0, EstimateSource.Initial);
                SetEstimate(k, gameSeed, observer, ObservationProperties.Threat(other, observer), relToObserver.Threat, Confidence.Low, 0, EstimateSource.Initial);
            }

            // Attitudes between the two non-observer countries, both directions.
            foreach (var a in countries)
            {
                if (a == observer) continue;
                foreach (var b in countries)
                {
                    if (b == observer || b == a) continue;
                    var rel = w.Rel(a, b);
                    SetEstimate(k, gameSeed, observer, ObservationProperties.Trust(a, b), rel.Trust, Confidence.Low, 0, EstimateSource.Initial);
                    SetEstimate(k, gameSeed, observer, ObservationProperties.Threat(a, b), rel.Threat, Confidence.Low, 0, EstimateSource.Initial);
                }
            }

            result[observer] = k;
        }

        return result;
    }

    /// <summary>
    /// Widens estimates whose age has crossed a new 4-turn interval since they were last
    /// observed, dropping confidence one level (floored at Low; Unknown is untouched).
    /// Fires once per interval via LastDecayAppliedAge, not every turn past the threshold.
    /// </summary>
    public static void Decay(WorldState w, ObserverKnowledge k)
    {
        foreach (var property in k.Estimates.Keys.ToList())
        {
            var est = k.Estimates[property];
            var age = w.Turn - est.ObservedTurn;
            if (age < 4) continue;

            var interval = age / 4;
            if (interval <= k.LastDecayAppliedAge.GetValueOrDefault(property, 0)) continue;

            if (est.Confidence == Confidence.Unknown)
            {
                k.LastDecayAppliedAge[property] = interval;
                continue;
            }

            var width = est.High - est.Low;
            var widen = width * 0.25;
            var newConfidence = est.Confidence switch
            {
                Confidence.High => Confidence.Medium,
                Confidence.Medium => Confidence.Low,
                Confidence.Low => Confidence.Low,
                _ => est.Confidence,
            };

            k.Estimates[property] = est with
            {
                Low = est.Low - widen,
                High = est.High + widen,
                Confidence = newConfidence,
            };
            k.LastDecayAppliedAge[property] = interval;
        }
    }

    // Events that carry information "about" the observer's own country are skipped —
    // the observer already knows its own stats/relations with certainty (ground truth,
    // surfaced via ActorView.Own / OutgoingRelations, not as an Estimate).
    private static bool IsAboutOwnCountry(DomainEvent ev, CountryId observer) => ev switch
    {
        GdpChanged e => e.Country == observer,
        InflationChanged e => e.Country == observer,
        ApprovalChanged e => e.Country == observer,
        ReadinessChanged e => e.Country == observer,
        ForcesInStraitChanged e => e.Country == observer,
        TradeFlowChanged e => e.Exporter == observer,
        ExportRestrictionSet e => e.Exporter == observer,
        TrustChanged e => e.Source == observer,
        ThreatChanged e => e.Source == observer,
        _ => false,
    };

    public static void UpdateFromEvents(
        WorldState w, SortedDictionary<CountryId, ObserverKnowledge> all,
        IReadOnlyList<DomainEvent> events, ulong gameSeed)
    {
        foreach (var observer in all.Keys) // SortedDictionary => enum order
        {
            var k = all[observer];

            foreach (var ev in events)
            {
                if (!ev.Visibility.CanObserve(observer)) continue;
                if (IsAboutOwnCountry(ev, observer)) continue;

                switch (ev)
                {
                    case ReadinessChanged e:
                        SetEstimate(k, gameSeed, observer, ObservationProperties.Readiness(e.Country),
                            w.Countries[e.Country].Readiness, Confidence.Medium, w.Turn, EstimateSource.PublicEvent);
                        break;

                    case ForcesInStraitChanged e:
                        SetEstimate(k, gameSeed, observer, ObservationProperties.ForcesInStrait(e.Country),
                            w.Countries[e.Country].ForcesInStrait, Confidence.Medium, w.Turn, EstimateSource.PublicEvent);
                        break;

                    case ApprovalChanged e:
                        SetEstimate(k, gameSeed, observer, ObservationProperties.Approval(e.Country),
                            w.Countries[e.Country].Approval, Confidence.Medium, w.Turn, EstimateSource.PublicEvent);
                        break;

                    // EconomySystem emits GdpChanged/InflationChanged as Public events every
                    // month, and every visible occurrence refreshes the estimate here at
                    // ObservedTurn = now (via SetEstimate, which also clears the decay clock).
                    // Consequence: unlike Military/Approval/Treasury estimates, Gdp/Inflation
                    // estimates for any actively-observed country effectively never reach the
                    // Decay() age-4 threshold and so never widen/downgrade confidence. This is
                    // deliberate v1 behavior, not a bug — decay is meant to model stale
                    // intelligence, and GDP/inflation are treated as continuously reported.
                    case GdpChanged e:
                        SetEstimate(k, gameSeed, observer, ObservationProperties.Gdp(e.Country),
                            w.Countries[e.Country].Gdp, Confidence.Medium, w.Turn, EstimateSource.PublicEvent);
                        break;

                    case InflationChanged e:
                        SetEstimate(k, gameSeed, observer, ObservationProperties.Inflation(e.Country),
                            w.Countries[e.Country].Inflation, Confidence.Medium, w.Turn, EstimateSource.PublicEvent);
                        break;

                    case TradeFlowChanged e:
                        SetEstimate(k, gameSeed, observer, ObservationProperties.Production(e.Exporter),
                            w.Countries[e.Exporter].ResourceProduction, Confidence.Medium, w.Turn, EstimateSource.TradeIntelligence);
                        break;

                    case ExportRestrictionSet e:
                        SetEstimate(k, gameSeed, observer, ObservationProperties.Production(e.Exporter),
                            w.Countries[e.Exporter].ResourceProduction, Confidence.Medium, w.Turn, EstimateSource.TradeIntelligence);
                        break;

                    case SpySucceeded e when e.Actor == observer:
                        {
                            var t = e.Target;
                            var cs = w.Countries[t];
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Gdp(t), cs.Gdp, Confidence.High, w.Turn, EstimateSource.Espionage);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Inflation(t), cs.Inflation, Confidence.High, w.Turn, EstimateSource.Espionage);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Treasury(t), cs.Treasury, Confidence.High, w.Turn, EstimateSource.Espionage);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Production(t), cs.ResourceProduction, Confidence.High, w.Turn, EstimateSource.Espionage);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Dependence(t), cs.ResourceDependence, Confidence.High, w.Turn, EstimateSource.Espionage);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Strength(t), cs.MilitaryStrength, Confidence.High, w.Turn, EstimateSource.Espionage);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Readiness(t), cs.Readiness, Confidence.High, w.Turn, EstimateSource.Espionage);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.ForcesInStrait(t), cs.ForcesInStrait, Confidence.High, w.Turn, EstimateSource.Espionage);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Approval(t), cs.Approval, Confidence.High, w.Turn, EstimateSource.Espionage);

                            var rel = w.Rel(t, observer);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Trust(t, observer), rel.Trust, Confidence.High, w.Turn, EstimateSource.Espionage);
                            SetEstimate(k, gameSeed, observer, ObservationProperties.Threat(t, observer), rel.Threat, Confidence.High, w.Turn, EstimateSource.Espionage);
                            break;
                        }

                    case TrustChanged e:
                        SetEstimate(k, gameSeed, observer, ObservationProperties.Trust(e.Source, e.Target),
                            w.Rel(e.Source, e.Target).Trust, Confidence.Medium, w.Turn, EstimateSource.PublicEvent);
                        break;

                    case ThreatChanged e:
                        SetEstimate(k, gameSeed, observer, ObservationProperties.Threat(e.Source, e.Target),
                            w.Rel(e.Source, e.Target).Threat, Confidence.Medium, w.Turn, EstimateSource.PublicEvent);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Produces an immutable point-in-time view for the observer. Shares no mutable
    /// object with WorldState — all fields are value-copied or converted into
    /// immutable collections.
    /// </summary>
    public static ActorView Freeze(WorldState w, ObserverKnowledge k)
    {
        var self = k.Observer;
        var cs = w.Countries[self];

        var own = new CountrySnapshot(
            cs.Gdp, cs.GrowthAnnual, cs.Inflation, cs.Treasury,
            cs.ResourceProduction, cs.ResourceStockpile, cs.ResourceDependence,
            cs.MilitaryStrength, cs.Readiness, cs.ForcesInStrait,
            cs.Approval, cs.ElectionCountdown, cs.GovernmentTemplateId, cs.InShortage);

        var outgoing = w.Countries.Keys
            .Where(c => c != self)
            .Select(target =>
            {
                var rel = w.Rel(self, target);
                return new OutgoingRelation(target, rel.Trust, rel.Threat);
            })
            .ToImmutableArray();

        var estimates = k.Estimates.ToImmutableSortedDictionary();

        // w.Pacts is a HashSet: enumeration order is not guaranteed and can differ between
        // a straight run and a resumed run (slot reuse after Remove vs. DTO list append) once
        // a pact is broken and a new one signed. Sort explicitly so ActorView.Public.Pacts —
        // and the prompt text built from it — is identical regardless of history.
        var pacts = w.Pacts
            .OrderBy(p => p.Item1).ThenBy(p => p.Item2)
            .Select(p => new PactInfo(p.Item1, p.Item2))
            .ToImmutableArray();

        var pub = new PublicFacts(w.RareEarthPrice, w.StraitTension, pacts);

        return new ActorView(self, w.Turn, own, outgoing, estimates, pub);
    }
}
