namespace Game.Contracts;

// Engine
public sealed record TurnStarted(int Turn, CountryId? Actor, Visibility Visibility, int NewTurn)
    : DomainEvent(Turn, Actor, Visibility);

// Stat deltas
public sealed record GdpChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record InflationChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record TreasuryChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ApprovalChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record DependenceChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record StockpileChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ReadinessChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ForcesInStraitChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record RareEarthPriceChanged(int Turn, CountryId? Actor, Visibility Visibility, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record StraitTensionChanged(int Turn, CountryId? Actor, Visibility Visibility, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ElectionClockTicked(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country)
    : DomainEvent(Turn, Actor, Visibility);

// Relationships
public sealed record TrustChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Source, CountryId Target, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ThreatChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Source, CountryId Target, double Delta)
    : DomainEvent(Turn, Actor, Visibility);

// Persistent policy (SET semantics)
public sealed record ExportRestrictionSet(int Turn, CountryId? Actor, Visibility Visibility, CountryId Exporter, CountryId Importer, double Level, string Statement)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record SanctionLevelSet(int Turn, CountryId? Actor, Visibility Visibility, CountryId Source, CountryId Target, double Level, string Statement)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record TradeFlowChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Exporter, CountryId Importer, double Delta, string Statement)
    : DomainEvent(Turn, Actor, Visibility);

// Pacts
public sealed record PactSigned(int Turn, CountryId? Actor, Visibility Visibility, CountryId A, CountryId B, string Statement)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record PactBroken(int Turn, CountryId? Actor, Visibility Visibility, CountryId A, CountryId B, string Statement)
    : DomainEvent(Turn, Actor, Visibility);

// Markers (news/context only, no state change)
public sealed record SubsidyEnacted(int Turn, CountryId? Actor, Visibility Visibility, double Effective, string Statement)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ThreatIssued(int Turn, CountryId? Actor, Visibility Visibility, CountryId Target, string Statement)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ReassuranceGiven(int Turn, CountryId? Actor, Visibility Visibility, CountryId Target, string Statement)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ForcesDeployedNotice(int Turn, CountryId? Actor, Visibility Visibility, CountryId Target, string Statement)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ShortageStarted(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, double Severity)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record ShortageEnded(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record StraitIncident(int Turn, CountryId? Actor, Visibility Visibility)
    : DomainEvent(Turn, Actor, Visibility);

// Covert
public sealed record SpySucceeded(int Turn, CountryId? Actor, Visibility Visibility, CountryId Target)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record SpyCaught(int Turn, CountryId? Actor, Visibility Visibility, CountryId Target)
    : DomainEvent(Turn, Actor, Visibility);

// Politics
public sealed record ElectionHeld(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, bool IncumbentRetained)
    : DomainEvent(Turn, Actor, Visibility);

public sealed record GovernmentChanged(int Turn, CountryId? Actor, Visibility Visibility, CountryId Country, string NewTemplateId)
    : DomainEvent(Turn, Actor, Visibility);

// Rejection
public sealed record IntentRejected(int Turn, CountryId? Actor, Visibility Visibility, CountryId IntentActor, ActionType Type, string Reason)
    : DomainEvent(Turn, Actor, Visibility);
