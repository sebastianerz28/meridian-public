using Game.Agents;
using Game.Contracts;

namespace Game.Agents.Tests;

public class DecisionParserTests
{
    [Fact]
    public void Parses_WellFormedDecision()
    {
        var d = DecisionParser.Parse(CountryId.Borvia, """
            {"assessment":"Pressure works.","action":{"type":"ExportControl","target":"Alderia","magnitude":0.6},"public_statement":"A response."}
            """);
        Assert.NotNull(d);
        Assert.Equal(ActionType.ExportControl, d!.Intent.Type);
        Assert.Equal(CountryId.Alderia, d.Intent.Target);
        Assert.Equal(CountryId.Borvia, d.Intent.Actor);
    }

    [Fact]
    public void StripsMarkdownFences()
    {
        var text = "```json\n{\"assessment\":\"a\",\"action\":{\"type\":\"Pass\"},\"public_statement\":\"\"}\n```";
        Assert.NotNull(DecisionParser.Parse(CountryId.Alderia, text));
    }

    [Fact]
    public void ModelCannotActAsAnotherCountry()
    {
        var d = DecisionParser.Parse(CountryId.Borvia,
            """{"assessment":"a","action":{"type":"Sanction","actor":"Alderia","target":"Cascara","magnitude":1},"public_statement":""}""");
        Assert.Equal(CountryId.Borvia, d!.Intent.Actor);
    }

    [Fact]
    public void GarbageAndUnknownActions_ReturnNull()
    {
        Assert.Null(DecisionParser.Parse(CountryId.Borvia, "I think we should invade!"));
        Assert.Null(DecisionParser.Parse(CountryId.Borvia,
            """{"assessment":"a","action":{"type":"Nuke","target":"Alderia"},"public_statement":""}"""));
    }

    [Fact]
    public void OutputsAreBoundedInCode_NotJustRequested()
    {
        var longText = new string('x', 5000);
        var d = DecisionParser.Parse(CountryId.Borvia, $$"""
            {"assessment":"{{longText}}","beliefs_update":["{{longText}}","b","c","d","e","f","g","h","i","j"],
             "action":{"type":"Pass"},"public_statement":"{{longText}}"}
            """);
        Assert.NotNull(d);
        Assert.True(d!.Assessment.Length <= Bounds.MaxAssessment);
        Assert.True(d.Intent.Statement.Length <= Bounds.MaxStatement);
        Assert.True(d.BeliefsUpdate!.Count <= Bounds.MaxBeliefs);
        Assert.All(d.BeliefsUpdate, b => Assert.True(b.Length <= Bounds.MaxBeliefLength));
    }

    [Fact]
    public void MagnitudeIsClamped()
    {
        var d = DecisionParser.Parse(CountryId.Borvia,
            """{"assessment":"a","action":{"type":"Mobilize","magnitude":7.5},"public_statement":""}""");
        Assert.Equal(1.0, d!.Intent.RequestedMagnitude);
    }
}
