using Game.Contracts;

namespace Game.Simulation.Observation;

using Game.Simulation;

/// <summary>
/// Per-observer mutable belief store: what one country currently believes about the world.
/// </summary>
public sealed class ObserverKnowledge
{
    public CountryId Observer { get; set; }

    public SortedDictionary<string, Estimate> Estimates { get; set; } = new();

    // property -> last interval index (age / 4) at which decay was applied.
    // Removed whenever the property is refreshed, which restarts its aging clock.
    public SortedDictionary<string, int> LastDecayAppliedAge { get; set; } = new();

    public ObserverKnowledgeDto ToDto() => new(
        Observer,
        Estimates.Values.ToList(),
        LastDecayAppliedAge.Select(kv => new DecayMarker(kv.Key, kv.Value)).ToList());

    public static ObserverKnowledge FromDto(ObserverKnowledgeDto dto)
    {
        var k = new ObserverKnowledge { Observer = dto.Observer };
        foreach (var estimate in dto.Estimates)
            k.Estimates[estimate.Property] = estimate;
        foreach (var marker in dto.LastDecayAppliedAge)
            k.LastDecayAppliedAge[marker.Property] = marker.Age;
        return k;
    }
}
