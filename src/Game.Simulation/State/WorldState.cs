using Game.Contracts;

namespace Game.Simulation.State;

public class WorldState
{
    public int Turn { get; set; }
    public SortedDictionary<CountryId, CountryState> Countries { get; set; } = new();
    public List<RelationshipState> Relationships { get; set; } = new();
    public List<TradeLink> TradeLinks { get; set; } = new();
    public List<SanctionState> Sanctions { get; set; } = new();
    public HashSet<(CountryId, CountryId)> Pacts { get; set; } = new();
    public double RareEarthPrice { get; set; }
    public double StraitTension { get; set; }

    public RelationshipState Rel(CountryId source, CountryId target)
    {
        var rel = Relationships.FirstOrDefault(r => r.Source == source && r.Target == target);
        if (rel == null)
            throw new KeyNotFoundException($"No relationship from {source} to {target}");
        return rel;
    }

    public TradeLink? Link(CountryId exporter, CountryId importer)
    {
        return TradeLinks.FirstOrDefault(l => l.Exporter == exporter && l.Importer == importer);
    }

    public double SanctionLevel(CountryId source, CountryId target)
    {
        var sanction = Sanctions.FirstOrDefault(s => s.Source == source && s.Target == target);
        return sanction?.Level ?? 0.0;
    }

    public bool HasPact(CountryId a, CountryId b)
    {
        var key = PactKey(a, b);
        return Pacts.Contains(key);
    }

    public static (CountryId, CountryId) PactKey(CountryId a, CountryId b)
    {
        // Normalize the pact key so that the smaller country ID comes first
        return a < b ? (a, b) : (b, a);
    }
}
