using Game.Contracts;

namespace Game.Simulation.Validation;

public sealed record ValidationResult(AcceptedCommand? Command, IntentRejected? Rejection)
{
    public static ValidationResult Accept(AcceptedCommand command) =>
        new(command, null);

    public static ValidationResult Reject(IntentRejected rejection) =>
        new(null, rejection);
}
