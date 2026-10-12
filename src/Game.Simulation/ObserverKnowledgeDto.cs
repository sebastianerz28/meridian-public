using Game.Contracts;

namespace Game.Simulation;

public sealed record DecayMarker(string Property, int Age);

/// <summary>Plain-list persistence shape for <see cref="Observation.ObserverKnowledge"/>.</summary>
public sealed record ObserverKnowledgeDto(
    CountryId Observer,
    List<Estimate> Estimates,
    List<DecayMarker> LastDecayAppliedAge);
