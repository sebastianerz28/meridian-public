using Game.Contracts;
using Game.Simulation;
using Game.Simulation.Systems;

namespace Game.Simulation.Tests;

public class PassiveSystemsTests
{
    [Fact]
    public void EconomyBeginTurn_GrowsGdpMonthly()
    {
        var w = Scenario.CreateMeridianStrait();
        WorldReducer.ApplyAll(w, EconomySystem.BeginTurn(w));
        Assert.Equal(1000 + 1000 * 0.02 / 12, w.Countries[CountryId.Alderia].Gdp, 5);
    }

    [Fact]
    public void Market_UnrestrictedTrade_ProducesNoShortage()
    {
        var w = Scenario.CreateMeridianStrait();
        var events = ResourceMarketSystem.BeginTurn(w);
        Assert.DoesNotContain(events, e => e is ShortageStarted);
    }

    [Fact]
    public void Market_PersistentRestriction_DrainsStockpileThenCausesOngoingShortage()
    {
        var w = Scenario.CreateMeridianStrait();
        w.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction = 0.6;
        var shortageTurns = 0;
        for (var t = 1; t <= 12; t++)
        {
            w.Turn = t;
            var events = ResourceMarketSystem.BeginTurn(w);
            WorldReducer.ApplyAll(w, events);
            if (events.Any(e => e is InflationChanged { Delta: > 0, Country: CountryId.Alderia }
                && w.Countries[CountryId.Alderia].ResourceStockpile == 0)) shortageTurns++;
        }
        Assert.Equal(0, w.Countries[CountryId.Alderia].ResourceStockpile);
        Assert.True(shortageTurns >= 3, "restriction must keep hurting after stockpile is gone");
        Assert.True(w.Countries[CountryId.Alderia].Inflation > 0.02, "shortage must raise inflation");
        Assert.True(w.RareEarthPrice > 1.0, "restricted supply must raise the market price");
    }

    [Fact]
    public void Market_SupplyImpactIsFlowWeighted()
    {
        var big = Scenario.CreateMeridianStrait();
        big.Link(CountryId.Borvia, CountryId.Alderia)!.ExportRestriction = 0.6;   // 60-flow link
        var bigDelta = ((RareEarthPriceChanged)ResourceMarketSystem.BeginTurn(big)
            .First(e => e is RareEarthPriceChanged)).Delta;

        var small = Scenario.CreateMeridianStrait();
        small.Link(CountryId.Borvia, CountryId.Cascara)!.ExportRestriction = 0.6; // 15-flow link
        var smallDelta = ((RareEarthPriceChanged)ResourceMarketSystem.BeginTurn(small)
            .First(e => e is RareEarthPriceChanged)).Delta;

        Assert.True(bigDelta > smallDelta,
            "restricting the large route must move the price more than the small one");
    }

    [Fact]
    public void Election_JudgesPostUpdateApproval_NotStaleValue()
    {
        // approval 45.5 sits above the threshold, but this turn's update pushes it below:
        // inflation 0.05 → delta = 0.4 - 8 = -7.6 → post ≈ 37.9 → incumbent must fall
        var w = Scenario.CreateMeridianStrait();
        var alderia = w.Countries[CountryId.Alderia];
        alderia.ElectionCountdown = 1;
        alderia.Approval = 45.5;
        alderia.Inflation = 0.05;
        var events = PoliticsSystem.EndTurn(w);
        Assert.Contains(events, e => e is ElectionHeld { IncumbentRetained: false });
    }

    [Fact]
    public void Strait_TensionRisesWithForcePresenceAndDecaysWithout()
    {
        var w = Scenario.CreateMeridianStrait();
        w.Countries[CountryId.Alderia].ForcesInStrait = 40; // total 65 → target 90
        WorldReducer.ApplyAll(w, StraitSystem.EndTurn(w, new DeterministicRng(1)));
        Assert.True(w.StraitTension > 20);

        var calm = Scenario.CreateMeridianStrait();
        calm.Countries[CountryId.Borvia].ForcesInStrait = 0;
        calm.Countries[CountryId.Cascara].ForcesInStrait = 0;
        WorldReducer.ApplyAll(calm, StraitSystem.EndTurn(calm, new DeterministicRng(1)));
        Assert.True(calm.StraitTension < 20);
    }

    [Fact]
    public void Strait_IncidentRollIsDeterministicAndOnlyAtHighTension()
    {
        var hot = Scenario.CreateMeridianStrait();
        hot.StraitTension = 95;
        hot.Countries[CountryId.Alderia].ForcesInStrait = 40;
        // contract: roll consumed iff post-update tension >= 80; outcome derived from same rng sequence
        var probe = new DeterministicRng(11);
        var incidentExpected = probe.NextDouble() < 0.15;
        var events = StraitSystem.EndTurn(hot, new DeterministicRng(11));
        Assert.Equal(incidentExpected, events.Any(e => e is StraitIncident));

        var cool = Scenario.CreateMeridianStrait(); // tension 20, no roll
        var eventsCool = StraitSystem.EndTurn(cool, new DeterministicRng(11));
        Assert.DoesNotContain(eventsCool, e => e is StraitIncident);
    }

    [Fact]
    public void Election_SwapsGovernmentOnLowApproval_AndIncumbentSurvivesHigh()
    {
        var w = Scenario.CreateMeridianStrait();
        var alderia = w.Countries[CountryId.Alderia];
        alderia.ElectionCountdown = 1;
        alderia.Approval = 30;
        WorldReducer.ApplyAll(w, PoliticsSystem.EndTurn(w));
        Assert.Equal("alderia-prosperity-party", alderia.GovernmentTemplateId);
        Assert.Equal(48, alderia.ElectionCountdown);

        var w2 = Scenario.CreateMeridianStrait();
        var a2 = w2.Countries[CountryId.Alderia];
        a2.ElectionCountdown = 1;
        a2.Approval = 70;
        WorldReducer.ApplyAll(w2, PoliticsSystem.EndTurn(w2));
        Assert.Equal("alderia-atlantic-coalition", a2.GovernmentTemplateId);
        Assert.Equal(48, a2.ElectionCountdown);
    }

    [Fact]
    public void GovernmentChange_ResetsOutgoingTrustTowardZero()
    {
        var w = Scenario.CreateMeridianStrait();
        var alderia = w.Countries[CountryId.Alderia];
        alderia.ElectionCountdown = 1;
        alderia.Approval = 10;
        WorldReducer.ApplyAll(w, PoliticsSystem.EndTurn(w));
        Assert.Equal(-21, w.Rel(CountryId.Alderia, CountryId.Borvia).Trust, 5);
    }

    [Fact]
    public void Autocracy_NeverHoldsElections()
    {
        var w = Scenario.CreateMeridianStrait();
        Assert.DoesNotContain(PoliticsSystem.EndTurn(w),
            e => e is ElectionHeld h && h.Country == CountryId.Borvia);
    }
}
