using System.Text.Json;
using Game.Contracts;

namespace Game.Persistence;

/// <summary>
/// Type-discriminated System.Text.Json serialization over the CLOSED set of ~31 event
/// records declared in <c>Game.Contracts/Events.cs</c>. Deliberately enumerated by an
/// explicit switch on the runtime type / type-name string rather than discovered via
/// reflection scanning: the event set is closed and
/// known at compile time, so a switch is both faster and fails loudly (a compiler/test
/// error, not a silent runtime gap) the moment a new event type is added to Contracts
/// without a matching case here.
/// </summary>
public static class EventSerializer
{
    private static readonly JsonSerializerOptions Options = new();

    public static string TypeName(DomainEvent e) => e.GetType().Name;

    public static string Serialize(DomainEvent e) => e switch
    {
        TurnStarted x => JsonSerializer.Serialize(x, Options),
        GdpChanged x => JsonSerializer.Serialize(x, Options),
        InflationChanged x => JsonSerializer.Serialize(x, Options),
        TreasuryChanged x => JsonSerializer.Serialize(x, Options),
        ApprovalChanged x => JsonSerializer.Serialize(x, Options),
        DependenceChanged x => JsonSerializer.Serialize(x, Options),
        StockpileChanged x => JsonSerializer.Serialize(x, Options),
        ReadinessChanged x => JsonSerializer.Serialize(x, Options),
        ForcesInStraitChanged x => JsonSerializer.Serialize(x, Options),
        RareEarthPriceChanged x => JsonSerializer.Serialize(x, Options),
        StraitTensionChanged x => JsonSerializer.Serialize(x, Options),
        ElectionClockTicked x => JsonSerializer.Serialize(x, Options),
        TrustChanged x => JsonSerializer.Serialize(x, Options),
        ThreatChanged x => JsonSerializer.Serialize(x, Options),
        ExportRestrictionSet x => JsonSerializer.Serialize(x, Options),
        SanctionLevelSet x => JsonSerializer.Serialize(x, Options),
        TradeFlowChanged x => JsonSerializer.Serialize(x, Options),
        PactSigned x => JsonSerializer.Serialize(x, Options),
        PactBroken x => JsonSerializer.Serialize(x, Options),
        SubsidyEnacted x => JsonSerializer.Serialize(x, Options),
        ThreatIssued x => JsonSerializer.Serialize(x, Options),
        ReassuranceGiven x => JsonSerializer.Serialize(x, Options),
        ForcesDeployedNotice x => JsonSerializer.Serialize(x, Options),
        ShortageStarted x => JsonSerializer.Serialize(x, Options),
        ShortageEnded x => JsonSerializer.Serialize(x, Options),
        StraitIncident x => JsonSerializer.Serialize(x, Options),
        SpySucceeded x => JsonSerializer.Serialize(x, Options),
        SpyCaught x => JsonSerializer.Serialize(x, Options),
        ElectionHeld x => JsonSerializer.Serialize(x, Options),
        GovernmentChanged x => JsonSerializer.Serialize(x, Options),
        IntentRejected x => JsonSerializer.Serialize(x, Options),
        _ => throw new NotSupportedException($"EventSerializer.Serialize: unhandled event type '{e.GetType().Name}'."),
    };

    public static DomainEvent Deserialize(string typeName, string json) => typeName switch
    {
        nameof(TurnStarted) => Read<TurnStarted>(json),
        nameof(GdpChanged) => Read<GdpChanged>(json),
        nameof(InflationChanged) => Read<InflationChanged>(json),
        nameof(TreasuryChanged) => Read<TreasuryChanged>(json),
        nameof(ApprovalChanged) => Read<ApprovalChanged>(json),
        nameof(DependenceChanged) => Read<DependenceChanged>(json),
        nameof(StockpileChanged) => Read<StockpileChanged>(json),
        nameof(ReadinessChanged) => Read<ReadinessChanged>(json),
        nameof(ForcesInStraitChanged) => Read<ForcesInStraitChanged>(json),
        nameof(RareEarthPriceChanged) => Read<RareEarthPriceChanged>(json),
        nameof(StraitTensionChanged) => Read<StraitTensionChanged>(json),
        nameof(ElectionClockTicked) => Read<ElectionClockTicked>(json),
        nameof(TrustChanged) => Read<TrustChanged>(json),
        nameof(ThreatChanged) => Read<ThreatChanged>(json),
        nameof(ExportRestrictionSet) => Read<ExportRestrictionSet>(json),
        nameof(SanctionLevelSet) => Read<SanctionLevelSet>(json),
        nameof(TradeFlowChanged) => Read<TradeFlowChanged>(json),
        nameof(PactSigned) => Read<PactSigned>(json),
        nameof(PactBroken) => Read<PactBroken>(json),
        nameof(SubsidyEnacted) => Read<SubsidyEnacted>(json),
        nameof(ThreatIssued) => Read<ThreatIssued>(json),
        nameof(ReassuranceGiven) => Read<ReassuranceGiven>(json),
        nameof(ForcesDeployedNotice) => Read<ForcesDeployedNotice>(json),
        nameof(ShortageStarted) => Read<ShortageStarted>(json),
        nameof(ShortageEnded) => Read<ShortageEnded>(json),
        nameof(StraitIncident) => Read<StraitIncident>(json),
        nameof(SpySucceeded) => Read<SpySucceeded>(json),
        nameof(SpyCaught) => Read<SpyCaught>(json),
        nameof(ElectionHeld) => Read<ElectionHeld>(json),
        nameof(GovernmentChanged) => Read<GovernmentChanged>(json),
        nameof(IntentRejected) => Read<IntentRejected>(json),
        _ => throw new NotSupportedException($"EventSerializer.Deserialize: unhandled event type '{typeName}'."),
    };

    private static T Read<T>(string json) where T : DomainEvent =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"EventSerializer.Deserialize: payload deserialized to null for '{typeof(T).Name}'.");
}
