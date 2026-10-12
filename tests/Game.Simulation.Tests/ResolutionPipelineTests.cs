using Game.Contracts;
using Game.Simulation;
using Game.Simulation.Resolution;

namespace Game.Simulation.Tests;

public class ResolutionPipelineTests
{
    [Fact]
    public void Commands_ResolveInCanonicalOrder_NotSubmissionOrder()
    {
        var w = Scenario.CreateMeridianStrait();
        var intents = new List<ActionIntent>
        {
            new(CountryId.Alderia, ActionType.DeployToRegion, null, 0.5, ""),
            new(CountryId.Borvia, ActionType.ExportControl, CountryId.Alderia, 0.5, ""),
        };
        var events = ResolutionPipeline.ResolveAll(w, intents, new DeterministicRng(1)).ToList();
        var economicIdx = events.FindIndex(e => e is ExportRestrictionSet);
        var militaryIdx = events.FindIndex(e => e is ForcesInStraitChanged);
        Assert.True(economicIdx >= 0 && militaryIdx >= 0 && economicIdx < militaryIdx);
    }

    [Fact]
    public void SameIntents_SameSeed_ProduceIdenticalEventStream()
    {
        List<ActionIntent> Intents() => new()
        {
            new(CountryId.Alderia, ActionType.Spy, CountryId.Borvia, 0.5, ""),
            new(CountryId.Cascara, ActionType.Mobilize, null, 0.3, ""),
        };
        var e1 = ResolutionPipeline.ResolveAll(Scenario.CreateMeridianStrait(), Intents(), new DeterministicRng(9));
        var e2 = ResolutionPipeline.ResolveAll(Scenario.CreateMeridianStrait(), Intents(), new DeterministicRng(9));
        Assert.Equal(e1.Select(e => e.ToString()), e2.Select(e => e.ToString()));
    }

    [Fact]
    public void RejectedIntents_ProduceRejectionEvents()
    {
        var events = ResolutionPipeline.ResolveAll(Scenario.CreateMeridianStrait(),
            new List<ActionIntent> { new(CountryId.Alderia, ActionType.Withdraw, null, 1, "") },
            new DeterministicRng(1));
        Assert.Contains(events, e => e is IntentRejected);
    }
}
