using System.Collections.Immutable;

namespace Game.Contracts;

public enum VisibilityKind { Public, Participants, ActorOnly, Restricted }

// ImmutableArray, not IReadOnlyList: events reach actors, and a read-only
// interface over a mutable array could be cast back and mutated.
public sealed record Visibility(VisibilityKind Kind, ImmutableArray<CountryId> Countries)
{
    public static Visibility Public { get; } = new(VisibilityKind.Public, ImmutableArray<CountryId>.Empty);
    public static Visibility ActorOnly(CountryId actor) => new(VisibilityKind.ActorOnly, ImmutableArray.Create(actor));
    public static Visibility Participants(params CountryId[] countries) => new(VisibilityKind.Participants, countries.ToImmutableArray());
    public static Visibility Restricted(params CountryId[] countries) => new(VisibilityKind.Restricted, countries.ToImmutableArray());

    public bool CanObserve(CountryId observer) =>
        Kind == VisibilityKind.Public || Countries.Contains(observer);
}
