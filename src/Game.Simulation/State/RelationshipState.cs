using Game.Contracts;

namespace Game.Simulation.State;

public class RelationshipState
{
    public CountryId Source { get; set; }
    public CountryId Target { get; set; }
    public double Trust { get; set; }
    public double Threat { get; set; }
}
