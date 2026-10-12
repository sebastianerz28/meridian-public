namespace Game.Contracts;

public record ActionIntent(
    CountryId Actor,
    ActionType Type,
    CountryId? Target,
    double RequestedMagnitude,
    string Statement)
{
    public static ActionIntent Pass(CountryId actor) =>
        new(actor, ActionType.Pass, null, 0, string.Empty);
}

public record AcceptedCommand(
    int CommandId,
    ActionIntent Intent,
    double EffectiveMagnitude);
