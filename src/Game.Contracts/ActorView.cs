using System.Collections.Immutable;

namespace Game.Contracts;

// Immutable collection TYPES, not IReadOnly* wrappers over castable mutable
// collections — mutation is impossible by construction (System.Collections.Immutable
// is in-box on net8.0). ForeignEstimates is declared as the concrete
// ImmutableSortedDictionary<string, Estimate>, not IReadOnlyDictionary<...>: a
// concrete, sealed immutable type gives the compiler no path back to a mutable
// Dictionary at all (an `as Dictionary<...>` probe is a compile error against it,
// not merely a runtime null) — that is strictly stronger than an interface-typed
// wrapper, which is exactly the castable-wrapper pattern this rule bans.

public sealed record CountrySnapshot(
    double Gdp, double GrowthAnnual, double Inflation, double Treasury,
    double ResourceProduction, double ResourceStockpile, double ResourceDependence,
    double MilitaryStrength, double Readiness, double ForcesInStrait,
    double Approval, int ElectionCountdown, string GovernmentTemplateId, bool InShortage);

public sealed record OutgoingRelation(CountryId Target, double Trust, double Threat);

public sealed record PactInfo(CountryId A, CountryId B);

public sealed record PublicFacts(double RareEarthPrice, double StraitTension, ImmutableArray<PactInfo> Pacts);

public sealed record ActorView(
    CountryId Self, int Turn, CountrySnapshot Own,
    ImmutableArray<OutgoingRelation> OutgoingRelations,
    ImmutableSortedDictionary<string, Estimate> ForeignEstimates,
    PublicFacts Public);

public static class ObservationProperties
{
    public static string Gdp(CountryId c) => $"{c}.Economy.Gdp";
    public static string Inflation(CountryId c) => $"{c}.Economy.Inflation";
    public static string Treasury(CountryId c) => $"{c}.Economy.Treasury";
    public static string Production(CountryId c) => $"{c}.Resource.Production";
    public static string Dependence(CountryId c) => $"{c}.Resource.Dependence";
    public static string Strength(CountryId c) => $"{c}.Military.Strength";
    public static string Readiness(CountryId c) => $"{c}.Military.Readiness";
    public static string ForcesInStrait(CountryId c) => $"{c}.Military.ForcesInStrait";
    public static string Approval(CountryId c) => $"{c}.Politics.Approval";
    public static string Trust(CountryId src, CountryId dst) => $"{src}->{dst}.Trust";
    public static string Threat(CountryId src, CountryId dst) => $"{src}->{dst}.Threat";
}
