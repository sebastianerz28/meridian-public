using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation.Systems;

/// <summary>
/// Rare-earth resource market: aggregate supply/demand price discovery, per-importer
/// shortage detection with stockpile drawdown, and sanction drag on GDP.
/// Runs after EconomySystem.BeginTurn (consumes this turn's GDP figures).
///
/// Constants:
///   DemandFactor            = 0.08   (fraction of GDP*dependence consumed as resource demand)
///   PriceAdjustSpeed        = 0.3    (fraction of the gap to priceTarget closed per turn)
///   PriceTargetMin/Max      = 0.25 / 4.0  (clamp band for the computed price target)
///   OwnProductionCoverage   = 0.5    (fraction of a country's own production that offsets its own need)
///   MinDependenceForImport  = 0.05   (countries below this dependence are not modeled as importers)
///   ShortageThreshold       = 0.01   (shortage ratio below which effects/markers are suppressed)
///   ShortageInflationFactor = 0.01   (inflation added per unit of shortage ratio)
///   ShortageGdpFactor       = 0.004  (fraction of GDP lost per unit of shortage ratio)
///   ShortageApprovalFactor  = 3      (approval points lost per unit of shortage ratio)
///   SanctionGdpFactor       = 0.002  (fraction of GDP lost per unit of sanction level, both sides)
/// </summary>
public static class ResourceMarketSystem
{
    private const double DemandFactor = 0.08;
    private const double PriceAdjustSpeed = 0.3;
    private const double PriceTargetMin = 0.25;
    private const double PriceTargetMax = 4.0;
    private const double OwnProductionCoverage = 0.5;
    private const double MinDependenceForImport = 0.05;
    private const double ShortageThreshold = 0.01;
    private const double ShortageInflationFactor = 0.01;
    private const double ShortageGdpFactor = 0.004;
    private const double ShortageApprovalFactor = 3.0;
    private const double SanctionGdpFactor = 0.002;

    public static IReadOnlyList<DomainEvent> BeginTurn(WorldState w)
    {
        var events = new List<DomainEvent>();

        // 1-3: aggregate supply/demand -> price target -> RareEarthPriceChanged
        double effectiveSupply = 0;
        foreach (var (id, country) in w.Countries)
        {
            effectiveSupply += country.ResourceProduction * OpenShare(w, id);
        }

        double demand = 0;
        foreach (var (_, country) in w.Countries)
        {
            demand += country.Gdp * country.ResourceDependence * DemandFactor;
        }

        var priceTarget = Clamp(demand / Math.Max(effectiveSupply, 1.0), PriceTargetMin, PriceTargetMax);
        events.Add(new RareEarthPriceChanged(w.Turn, null, Visibility.Public,
            PriceAdjustSpeed * (priceTarget - w.RareEarthPrice)));

        // 4: per-importer shortage detection & stockpile drawdown
        foreach (var (id, country) in w.Countries)
        {
            if (country.ResourceDependence < MinDependenceForImport) continue;

            var need = country.Gdp * country.ResourceDependence * DemandFactor;
            if (need <= 0) continue;

            var inflow = w.TradeLinks
                .Where(l => l.Importer == id)
                .Sum(l => l.BaseFlow * (1 - l.ExportRestriction));

            var own = country.ResourceProduction * OwnProductionCoverage;
            var gap = Math.Max(0, need - inflow - own);
            var draw = Math.Min(country.ResourceStockpile, gap);

            events.Add(new StockpileChanged(w.Turn, null, Visibility.ActorOnly(id), id, -draw));

            var shortage = (gap - draw) / need;

            if (shortage > ShortageThreshold)
            {
                events.Add(new InflationChanged(w.Turn, null, Visibility.Public, id,
                    ShortageInflationFactor * shortage));
                events.Add(new GdpChanged(w.Turn, null, Visibility.Public, id,
                    -country.Gdp * ShortageGdpFactor * shortage));
                events.Add(new ApprovalChanged(w.Turn, null, Visibility.ActorOnly(id), id,
                    -ShortageApprovalFactor * shortage));

                if (!country.InShortage)
                    events.Add(new ShortageStarted(w.Turn, null, Visibility.Public, id, shortage));
            }
            else if (country.InShortage)
            {
                events.Add(new ShortageEnded(w.Turn, null, Visibility.Public, id));
            }
        }

        // 5: sanction drag on GDP, ordered by Source then Target
        foreach (var sanction in w.Sanctions.OrderBy(s => s.Source).ThenBy(s => s.Target))
        {
            var sourceGdp = w.Countries[sanction.Source].Gdp;
            var targetGdp = w.Countries[sanction.Target].Gdp;

            events.Add(new GdpChanged(w.Turn, null, Visibility.Public, sanction.Source,
                -sourceGdp * SanctionGdpFactor * sanction.Level));
            events.Add(new GdpChanged(w.Turn, null, Visibility.Public, sanction.Target,
                -targetGdp * SanctionGdpFactor * sanction.Level));
        }

        return events;
    }

    /// <summary>
    /// Flow-weighted fraction of a country's outgoing trade that is unrestricted.
    /// A country with no outgoing links is fully open (1.0) since none of its
    /// production is routed through export controls.
    /// </summary>
    private static double OpenShare(WorldState w, CountryId exporter)
    {
        var outgoing = w.TradeLinks.Where(l => l.Exporter == exporter).ToList();
        if (outgoing.Count == 0) return 1.0;

        var totalFlow = outgoing.Sum(l => l.BaseFlow);
        if (totalFlow <= 0) return 1.0;

        var openFlow = outgoing.Sum(l => l.BaseFlow * (1 - l.ExportRestriction));
        return openFlow / totalFlow;
    }

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
}
