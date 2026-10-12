using Game.Contracts;
using Game.Simulation;
using Game.Simulation.State;

namespace Game.Persistence;

public sealed record CountryStateDto(
    CountryId Id,
    double Gdp,
    double GrowthAnnual,
    double Inflation,
    double Treasury,
    double ResourceProduction,
    double ResourceStockpile,
    double ResourceDependence,
    double MilitaryStrength,
    double Readiness,
    double ForcesInStrait,
    double Approval,
    GovernmentKind Government,
    int ElectionCountdown,
    string GovernmentTemplateId,
    bool InShortage)
{
    public static CountryStateDto FromState(CountryId id, CountryState s) => new(
        id, s.Gdp, s.GrowthAnnual, s.Inflation, s.Treasury,
        s.ResourceProduction, s.ResourceStockpile, s.ResourceDependence,
        s.MilitaryStrength, s.Readiness, s.ForcesInStrait, s.Approval,
        s.Government, s.ElectionCountdown, s.GovernmentTemplateId, s.InShortage);

    public CountryState ToState() => new()
    {
        Gdp = Gdp,
        GrowthAnnual = GrowthAnnual,
        Inflation = Inflation,
        Treasury = Treasury,
        ResourceProduction = ResourceProduction,
        ResourceStockpile = ResourceStockpile,
        ResourceDependence = ResourceDependence,
        MilitaryStrength = MilitaryStrength,
        Readiness = Readiness,
        ForcesInStrait = ForcesInStrait,
        Approval = Approval,
        Government = Government,
        ElectionCountdown = ElectionCountdown,
        GovernmentTemplateId = GovernmentTemplateId,
        InShortage = InShortage,
    };
}

public sealed record RelationshipDto(CountryId Source, CountryId Target, double Trust, double Threat)
{
    public static RelationshipDto FromState(RelationshipState r) => new(r.Source, r.Target, r.Trust, r.Threat);
    public RelationshipState ToState() => new() { Source = Source, Target = Target, Trust = Trust, Threat = Threat };
}

public sealed record TradeLinkDto(CountryId Exporter, CountryId Importer, double BaseFlow, double ExportRestriction)
{
    public static TradeLinkDto FromState(TradeLink t) => new(t.Exporter, t.Importer, t.BaseFlow, t.ExportRestriction);
    public TradeLink ToState() => new() { Exporter = Exporter, Importer = Importer, BaseFlow = BaseFlow, ExportRestriction = ExportRestriction };
}

public sealed record SanctionDto(CountryId Source, CountryId Target, double Level)
{
    public static SanctionDto FromState(SanctionState s) => new(s.Source, s.Target, s.Level);
    public SanctionState ToState() => new() { Source = Source, Target = Target, Level = Level };
}

public sealed record PactDto(CountryId A, CountryId B);

/// <summary>Bidirectional, complete persistence shape for <see cref="WorldState"/>.</summary>
public sealed record WorldStateDto(
    int Turn,
    List<CountryStateDto> Countries,
    List<RelationshipDto> Relationships,
    List<TradeLinkDto> TradeLinks,
    List<SanctionDto> Sanctions,
    List<PactDto> Pacts,
    double RareEarthPrice,
    double StraitTension)
{
    public static WorldStateDto FromWorld(WorldState w) => new(
        w.Turn,
        w.Countries.Select(kv => CountryStateDto.FromState(kv.Key, kv.Value)).ToList(),
        w.Relationships.Select(RelationshipDto.FromState).ToList(),
        w.TradeLinks.Select(TradeLinkDto.FromState).ToList(),
        w.Sanctions.Select(SanctionDto.FromState).ToList(),
        // w.Pacts is a HashSet: unordered enumeration would let the persisted list order
        // depend on internal slot reuse (e.g. break-then-sign churn), diverging from a
        // straight run's order after reload. Sort explicitly for save/resume equivalence.
        w.Pacts.OrderBy(p => p.Item1).ThenBy(p => p.Item2).Select(p => new PactDto(p.Item1, p.Item2)).ToList(),
        w.RareEarthPrice,
        w.StraitTension);

    public WorldState ToWorld()
    {
        var w = new WorldState
        {
            Turn = Turn,
            RareEarthPrice = RareEarthPrice,
            StraitTension = StraitTension,
        };
        foreach (var c in Countries) w.Countries[c.Id] = c.ToState();
        foreach (var r in Relationships) w.Relationships.Add(r.ToState());
        foreach (var t in TradeLinks) w.TradeLinks.Add(t.ToState());
        foreach (var s in Sanctions) w.Sanctions.Add(s.ToState());
        foreach (var p in Pacts) w.Pacts.Add(WorldState.PactKey(p.A, p.B));
        return w;
    }
}

/// <summary>
/// One serialized domain event: a (type, payload) pair produced by <see cref="EventSerializer"/>.
/// Used inside <see cref="EngineSnapshotDto"/> wherever an <see cref="EngineSnapshot"/> carries
/// raw <c>DomainEvent</c> lists (last-turn events, per-observer relevant windows) — those are
/// polymorphic over the closed event-record hierarchy, so they cannot round-trip through plain
/// System.Text.Json reflection and instead go through the same type-discriminated serializer
/// used for the events table.
/// </summary>
public sealed record EventEnvelope(string Type, string Payload)
{
    public static EventEnvelope From(DomainEvent e) => new(EventSerializer.TypeName(e), EventSerializer.Serialize(e));
    public DomainEvent ToEvent() => EventSerializer.Deserialize(Type, Payload);
}

/// <summary>
/// Bidirectional persistence shape for <see cref="EngineSnapshot"/>. The rng state and game
/// seed are string-encoded ulongs: System.Text.Json (like all JSON) represents numbers as
/// IEEE-754 doubles when the target isn't explicitly typed through the full 64-bit range at
/// the token level, and round-tripping a raw JSON number through some tooling paths can lose
/// precision above 2^53 — so both are carried as decimal strings and parsed back with
/// <see cref="ulong.Parse(string)"/> to guarantee exact rng continuation across resume.
/// </summary>
public sealed record EngineSnapshotDto(
    string RngState,
    string GameSeed,
    Dictionary<CountryId, int> AsleepStreaks,
    Dictionary<CountryId, ObserverKnowledgeDto> Knowledge,
    List<EventEnvelope> LastTurnEvents,
    Dictionary<CountryId, List<EventEnvelope>> RelevantWindows)
{
    public static EngineSnapshotDto FromSnapshot(EngineSnapshot s) => new(
        s.RngState.ToString(),
        s.GameSeed.ToString(),
        new Dictionary<CountryId, int>(s.AsleepStreaks),
        new Dictionary<CountryId, ObserverKnowledgeDto>(s.Knowledge),
        s.LastTurnEvents.Select(EventEnvelope.From).ToList(),
        s.RelevantWindows.ToDictionary(kv => kv.Key, kv => kv.Value.Select(EventEnvelope.From).ToList()));

    public EngineSnapshot ToSnapshot() => new(
        ulong.Parse(RngState),
        ulong.Parse(GameSeed),
        new SortedDictionary<CountryId, int>(AsleepStreaks),
        new SortedDictionary<CountryId, ObserverKnowledgeDto>(Knowledge),
        LastTurnEvents.Select(e => e.ToEvent()).ToList(),
        new SortedDictionary<CountryId, List<DomainEvent>>(
            RelevantWindows.ToDictionary(kv => kv.Key, kv => kv.Value.Select(e => e.ToEvent()).ToList())));
}
