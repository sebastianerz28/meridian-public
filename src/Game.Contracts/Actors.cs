using System.Collections.Immutable;

namespace Game.Contracts;

public enum SchedulingPolicy { EveryTurn, OnSignificance }

public sealed record DecisionContext(
    CountryId Self,
    int Turn,
    ActorView View,                              // typed, immutable — no object escape hatch
    ImmutableArray<DomainEvent> RelevantEvents,  // visible, Minor+ significance, newest first, ≤15
    StrategicSignificance LastTurnSignificance);

public sealed record ReflectionContext(
    CountryId Self,
    int Turn,
    ImmutableArray<DomainEvent> VisibleTurnEvents,
    StrategicSignificance Significance);
// ImmutableArray throughout: everything an actor receives is immutable by type,
// not by interface convention (`using System.Collections.Immutable;`).

public interface IActor
{
    Task<ActionIntent> DecideAsync(DecisionContext ctx, CancellationToken ct);
    Task ReflectAsync(ReflectionContext ctx, CancellationToken ct);
    void OnGovernmentChanged(string newTemplateId) { }   // default no-op
}

public sealed record ActorRegistration(CountryId Country, IActor Actor, SchedulingPolicy Policy);
