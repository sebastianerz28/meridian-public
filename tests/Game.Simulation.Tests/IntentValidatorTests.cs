using Game.Contracts;
using Game.Simulation;
using Game.Simulation.Validation;

namespace Game.Simulation.Tests;

public class IntentValidatorTests
{
    [Fact]
    public void ExportControl_IsDowngraded_KeepingRequestedMagnitude()
    {
        var w = Scenario.CreateMeridianStrait();
        var result = IntentValidator.Validate(w, new ActionIntent(
            CountryId.Borvia, ActionType.ExportControl, CountryId.Alderia, 0.8, "punish"), 1);
        Assert.NotNull(result.Command);
        Assert.Equal(0.8, result.Command!.Intent.RequestedMagnitude);
        Assert.Equal(0.8 * 0.75, result.Command.EffectiveMagnitude, 5);
    }

    [Fact]
    public void ExportControl_WithoutTradeLink_IsRejected()
    {
        var w = Scenario.CreateMeridianStrait();
        var result = IntentValidator.Validate(w, new ActionIntent(
            CountryId.Alderia, ActionType.ExportControl, CountryId.Borvia, 0.5, ""), 1);
        Assert.Null(result.Command);
    }

    [Fact]
    public void Withdraw_WithNoForces_IsRejected_ActorOnlyVisibility()
    {
        var w = Scenario.CreateMeridianStrait();
        var result = IntentValidator.Validate(w,
            new ActionIntent(CountryId.Alderia, ActionType.Withdraw, null, 0.5, ""), 1);
        Assert.Null(result.Command);
        Assert.Equal(VisibilityKind.ActorOnly, result.Rejection!.Visibility.Kind);
    }

    [Fact]
    public void SignPact_RequiresMutualPositiveTrust()
    {
        var w = Scenario.CreateMeridianStrait();
        Assert.Null(IntentValidator.Validate(w,
            new ActionIntent(CountryId.Alderia, ActionType.SignPact, CountryId.Borvia, 1.0, ""), 1).Command);
    }

    [Fact]
    public void SelfTargeting_IsRejected()
    {
        var w = Scenario.CreateMeridianStrait();
        Assert.Null(IntentValidator.Validate(w,
            new ActionIntent(CountryId.Borvia, ActionType.Sanction, CountryId.Borvia, 0.5, ""), 1).Command);
    }

    [Fact]
    public void Trade_RequiresActorOwnedExportLink()
    {
        var w = Scenario.CreateMeridianStrait();
        // Borvia exports to Alderia → Borvia may expand it
        Assert.NotNull(IntentValidator.Validate(w,
            new ActionIntent(CountryId.Borvia, ActionType.Trade, CountryId.Alderia, 0.5, ""), 1).Command);
        // Alderia is only the importer → cannot pump the exporter's flow to offset a restriction
        Assert.Null(IntentValidator.Validate(w,
            new ActionIntent(CountryId.Alderia, ActionType.Trade, CountryId.Borvia, 0.5, ""), 1).Command);
    }
}
