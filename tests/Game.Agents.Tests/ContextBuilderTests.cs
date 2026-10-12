using System.Collections.Immutable;
using Game.Agents;
using Game.Contracts;
using Game.Simulation;
using Game.Simulation.Observation;

namespace Game.Agents.Tests;

public class ContextBuilderTests
{
    private static DecisionContext Ctx(CountryId self)
    {
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, 42);
        var view = ObservationSystem.Freeze(w, all[self]);
        return new DecisionContext(self, 1, view, ImmutableArray<DomainEvent>.Empty, StrategicSignificance.None);
    }

    [Fact]
    public void DecisionPrompt_ShowsEstimatesWithProvenance_AndPublicFacts()
    {
        var s = AgentState.FromTemplate(CountryId.Alderia, "alderia-atlantic-coalition");
        var prompt = ContextBuilder.BuildDecisionPrompt(s, Ctx(CountryId.Alderia));
        Assert.Contains("## Intelligence estimates", prompt);
        Assert.Contains("confidence", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Global situation", prompt);
        Assert.Contains("## Your memories", prompt);
        Assert.Contains("unknown", prompt, StringComparison.OrdinalIgnoreCase); // Borvia treasury starts Unknown
    }

    [Fact]
    public void DecisionPrompt_StatesTheCurrentTurn_BeforeOwnCountrySection()
    {
        var s = AgentState.FromTemplate(CountryId.Alderia, "alderia-atlantic-coalition");
        var prompt = ContextBuilder.BuildDecisionPrompt(s, Ctx(CountryId.Alderia));
        Assert.Contains("Current turn: T1", prompt);
        Assert.True(
            prompt.IndexOf("Current turn: T1", StringComparison.Ordinal)
                < prompt.IndexOf("## Your country (exact)", StringComparison.Ordinal),
            "current turn must be stated before the own-country section");
    }

    [Fact]
    public void SystemPrompt_StatesTheIntentOnlyRule()
    {
        var s = AgentState.FromTemplate(CountryId.Borvia, "borvia-national-front");
        var sys = ContextBuilder.BuildSystemPrompt(s);
        Assert.Contains("simulation determines all consequences", sys);
        Assert.Contains("ExportControl", sys);
    }

    [Fact]
    public void DecisionPrompt_NeverDumpsRawEventFieldsForNonParticipant()
    {
        // SpySucceeded's only narrative rendering is guarded on `Actor == self`; for a
        // non-participant it must fall back to a safe type-name-only rendering, not a
        // raw record dump (which would leak the Target field to an observer who was
        // never supposed to learn who got spied on from this event alone).
        var s = AgentState.FromTemplate(CountryId.Alderia, "alderia-atlantic-coalition");
        var w = Scenario.CreateMeridianStrait();
        var all = ObservationSystem.Initialize(w, 42);
        var view = ObservationSystem.Freeze(w, all[CountryId.Alderia]);

        var spyEvent = new SpySucceeded(2, CountryId.Borvia, Visibility.Public, CountryId.Cascara);
        var events = ImmutableArray.Create<DomainEvent>(spyEvent);
        var ctx = new DecisionContext(CountryId.Alderia, 2, view, events, StrategicSignificance.Minor);

        var prompt = ContextBuilder.BuildDecisionPrompt(s, ctx);

        Assert.Contains("SpySucceeded", prompt);
        Assert.DoesNotContain("SpySucceeded {", prompt);
        Assert.DoesNotContain(CountryId.Cascara.ToString(), prompt.Split('\n').First(l => l.Contains("SpySucceeded")));
    }
}
