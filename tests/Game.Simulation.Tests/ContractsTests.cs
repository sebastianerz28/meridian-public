using Game.Contracts;

namespace Game.Simulation.Tests;

public class ContractsTests
{
    [Fact]
    public void Public_IsObservableByEveryone()
    {
        Assert.True(Visibility.Public.CanObserve(CountryId.Alderia));
        Assert.True(Visibility.Public.CanObserve(CountryId.Borvia));
    }

    [Fact]
    public void ActorOnly_IsObservableOnlyByActor()
    {
        var v = Visibility.ActorOnly(CountryId.Alderia);
        Assert.True(v.CanObserve(CountryId.Alderia));
        Assert.False(v.CanObserve(CountryId.Borvia));
    }

    [Fact]
    public void Participants_ObservableByListedCountriesOnly()
    {
        var v = Visibility.Participants(CountryId.Alderia, CountryId.Borvia);
        Assert.True(v.CanObserve(CountryId.Borvia));
        Assert.False(v.CanObserve(CountryId.Cascara));
    }

    [Fact]
    public void ActionTypes_MapToCategories()
    {
        Assert.Equal(ActionCategory.Economic, ActionType.ExportControl.Category());
        Assert.Equal(ActionCategory.Diplomatic, ActionType.Threaten.Category());
        Assert.Equal(ActionCategory.Military, ActionType.DeployToRegion.Category());
        Assert.Equal(ActionCategory.Covert, ActionType.Spy.Category());
        Assert.Equal(ActionCategory.None, ActionType.Pass.Category());
    }

    [Fact]
    public void GovernmentTemplates_SuccessorIsTheOtherDemocraticTemplate()
    {
        Assert.Equal("alderia-prosperity-party",
            GovernmentTemplates.Successor(CountryId.Alderia, "alderia-atlantic-coalition"));
        Assert.Equal("alderia-atlantic-coalition",
            GovernmentTemplates.Successor(CountryId.Alderia, "alderia-prosperity-party"));
        Assert.Throws<InvalidOperationException>(
            () => GovernmentTemplates.Successor(CountryId.Borvia, "borvia-national-front"));
    }

    [Fact]
    public void GovernmentTemplates_HaveNonEmptyFlavor()
    {
        var t = GovernmentTemplates.Get("borvia-national-front");
        Assert.Contains("Regime survival", t.ObjectivesText);
        Assert.NotEmpty(t.PersonaText);
    }
}
