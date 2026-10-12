using Game.Contracts;

namespace Game.Simulation.State;

public class TradeLink
{
    public CountryId Exporter { get; set; }
    public CountryId Importer { get; set; }
    public double BaseFlow { get; set; }
    public double ExportRestriction { get; set; }
}
