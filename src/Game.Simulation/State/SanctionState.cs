using Game.Contracts;

namespace Game.Simulation.State;

public class SanctionState
{
    public CountryId Source { get; set; }
    public CountryId Target { get; set; }
    public double Level { get; set; }
}
