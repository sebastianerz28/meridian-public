using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation.Systems;

/// <summary>
/// Passive economic drift, applied once per turn before ResourceMarketSystem.
///
/// Constants:
///   MonthlyGrowthDivisor      = 12    (GrowthAnnual is annualized; divide to get a monthly increment)
///   TreasuryRevenueRate       = 0.02  (fraction of GDP collected into treasury per year)
///   InflationTarget           = 0.02  (central-bank-style anchor inflation drifts toward)
///   InflationCorrectionRate   = 0.1   (fraction of the gap to target closed per turn)
/// </summary>
public static class EconomySystem
{
    private const double MonthlyGrowthDivisor = 12.0;
    private const double TreasuryRevenueRate = 0.02;
    private const double InflationTarget = 0.02;
    private const double InflationCorrectionRate = 0.1;

    public static IReadOnlyList<DomainEvent> BeginTurn(WorldState w)
    {
        var events = new List<DomainEvent>();

        foreach (var (id, country) in w.Countries)
        {
            events.Add(new GdpChanged(w.Turn, null, Visibility.Public, id,
                country.Gdp * country.GrowthAnnual / MonthlyGrowthDivisor));

            events.Add(new TreasuryChanged(w.Turn, null, Visibility.ActorOnly(id), id,
                country.Gdp * TreasuryRevenueRate / MonthlyGrowthDivisor));

            events.Add(new InflationChanged(w.Turn, null, Visibility.Public, id,
                (InflationTarget - country.Inflation) * InflationCorrectionRate));
        }

        return events;
    }
}
