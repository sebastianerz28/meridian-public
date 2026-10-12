using Game.Agents;
using Game.Contracts;

namespace Game.Agents.Tests;

public class AgentStateTests
{
    [Fact]
    public void FromTemplate_LoadsGovernmentBlock()
    {
        var s = AgentState.FromTemplate(CountryId.Borvia, "borvia-national-front");
        Assert.Contains("Regime survival", s.ObjectivesText);
        Assert.NotEmpty(s.PersonaText);
        Assert.NotEmpty(s.PoliticalBeliefs);
    }

    [Fact]
    public void SwapGovernment_ReplacesGovernmentKeepsInstitutionalMemory()
    {
        var s = AgentState.FromTemplate(CountryId.Alderia, "alderia-atlantic-coalition");
        s.Memories.Add("Borvia restricted exports in T2");
        s.PoliticalBeliefs.Add("Borvia is bluffing");
        s.LatestAssessment = "hold firm";
        s.SwapGovernment("alderia-prosperity-party");
        Assert.Equal("alderia-prosperity-party", s.GovernmentTemplateId);
        Assert.Contains("Borvia restricted exports in T2", s.Memories);
        Assert.DoesNotContain("Borvia is bluffing", s.PoliticalBeliefs);
        Assert.Equal("", s.LatestAssessment);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var s = AgentState.FromTemplate(CountryId.Cascara, "cascara-maritime-league");
        s.Memories.Add("m1");
        s.LatestAssessment = "a1";
        var restored = AgentState.FromJson(s.ToJson());
        Assert.Equal(s.Country, restored.Country);
        Assert.Equal(s.Memories, restored.Memories);
        Assert.Equal("a1", restored.LatestAssessment);
        Assert.Equal(s.GovernmentTemplateId, restored.GovernmentTemplateId);
    }

    [Fact]
    public async Task ScriptedClient_ReplaysAndRecords()
    {
        var client = new ScriptedClient(new Queue<string>(new[] { "one", "two" }));
        Assert.Equal("one", await client.CompleteAsync("sys", "usr", default));
        Assert.Equal("two", await client.CompleteAsync("sys", "usr2", default));
        Assert.Equal(2, client.Calls.Count);
    }
}
