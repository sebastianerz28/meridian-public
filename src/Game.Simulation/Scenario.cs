using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation;

public static class Scenario
{
    public const ulong GameSeedDefault = 42UL;

    public static WorldState CreateMeridianStrait()
    {
        var world = new WorldState
        {
            Turn = 0,
            RareEarthPrice = 1.0,
            StraitTension = 20
        };

        // Create countries
        world.Countries.Add(CountryId.Alderia, new CountryState
        {
            Gdp = 1000,
            GrowthAnnual = 0.02,
            Inflation = 0.02,
            Treasury = 100,
            ResourceProduction = 10,
            ResourceStockpile = 30,
            ResourceDependence = 0.7,
            MilitaryStrength = 70,
            Readiness = 40,
            ForcesInStrait = 0,
            Approval = 55,
            Government = GovernmentKind.Democracy,
            ElectionCountdown = 18,
            GovernmentTemplateId = "alderia-atlantic-coalition"
        });

        world.Countries.Add(CountryId.Borvia, new CountryState
        {
            Gdp = 600,
            GrowthAnnual = 0.025,
            Inflation = 0.04,
            Treasury = 80,
            ResourceProduction = 100,
            ResourceStockpile = 200,
            ResourceDependence = 0.1,
            MilitaryStrength = 65,
            Readiness = 55,
            ForcesInStrait = 10,
            Approval = 60,
            Government = GovernmentKind.Autocracy,
            ElectionCountdown = -1,
            GovernmentTemplateId = "borvia-national-front"
        });

        world.Countries.Add(CountryId.Cascara, new CountryState
        {
            Gdp = 300,
            GrowthAnnual = 0.02,
            Inflation = 0.02,
            Treasury = 40,
            ResourceProduction = 5,
            ResourceStockpile = 10,
            ResourceDependence = 0.5,
            MilitaryStrength = 30,
            Readiness = 35,
            ForcesInStrait = 15,
            Approval = 50,
            Government = GovernmentKind.Democracy,
            ElectionCountdown = 30,
            GovernmentTemplateId = "cascara-maritime-league"
        });

        // Create relationships (directional)
        world.Relationships.Add(new RelationshipState { Source = CountryId.Alderia, Target = CountryId.Borvia, Trust = -30, Threat = 40 });
        world.Relationships.Add(new RelationshipState { Source = CountryId.Borvia, Target = CountryId.Alderia, Trust = -35, Threat = 45 });
        world.Relationships.Add(new RelationshipState { Source = CountryId.Alderia, Target = CountryId.Cascara, Trust = 60, Threat = 5 });
        world.Relationships.Add(new RelationshipState { Source = CountryId.Cascara, Target = CountryId.Alderia, Trust = 65, Threat = 5 });
        world.Relationships.Add(new RelationshipState { Source = CountryId.Borvia, Target = CountryId.Cascara, Trust = -40, Threat = 30 });
        world.Relationships.Add(new RelationshipState { Source = CountryId.Cascara, Target = CountryId.Borvia, Trust = -45, Threat = 60 });

        // Create trade links
        world.TradeLinks.Add(new TradeLink { Exporter = CountryId.Borvia, Importer = CountryId.Alderia, BaseFlow = 60, ExportRestriction = 0 });
        world.TradeLinks.Add(new TradeLink { Exporter = CountryId.Borvia, Importer = CountryId.Cascara, BaseFlow = 15, ExportRestriction = 0 });

        // Pacts (stored as normalized keys)
        world.Pacts.Add(WorldState.PactKey(CountryId.Alderia, CountryId.Cascara));

        return world;
    }
}
