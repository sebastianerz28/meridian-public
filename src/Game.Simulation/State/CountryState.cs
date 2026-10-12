namespace Game.Simulation.State;

public class CountryState
{
    public double Gdp { get; set; }
    public double GrowthAnnual { get; set; }
    public double Inflation { get; set; }
    public double Treasury { get; set; }
    public double ResourceProduction { get; set; }
    public double ResourceStockpile { get; set; }
    public double ResourceDependence { get; set; }
    public double MilitaryStrength { get; set; }
    public double Readiness { get; set; }
    public double ForcesInStrait { get; set; }
    public double Approval { get; set; }
    public GovernmentKind Government { get; set; }
    public int ElectionCountdown { get; set; }
    public string GovernmentTemplateId { get; set; } = string.Empty;
    public bool InShortage { get; set; }
}

public enum GovernmentKind
{
    Democracy,
    Autocracy
}
