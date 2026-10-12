using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation.Validation;

public static class IntentValidator
{
    public static ValidationResult Validate(WorldState w, ActionIntent intent, int commandId)
    {
        // Clamp magnitude to [0, 1] first
        var clampedMagnitude = Math.Max(0, Math.Min(1, intent.RequestedMagnitude));

        // Pass → accepted, effective 0
        if (intent.Type == ActionType.Pass)
        {
            var acceptedPass = new AcceptedCommand(
                commandId,
                intent with { RequestedMagnitude = clampedMagnitude },
                0.0);
            return ValidationResult.Accept(acceptedPass);
        }

        // Targeted actions: Trade, Sanction, ExportControl, Reassure, Threaten, SignPact, BreakPact, Spy
        var targetedActions = new[]
        {
            ActionType.Trade, ActionType.Sanction, ActionType.ExportControl,
            ActionType.Reassure, ActionType.Threaten, ActionType.SignPact,
            ActionType.BreakPact, ActionType.Spy
        };

        if (targetedActions.Contains(intent.Type))
        {
            if (intent.Target == null || intent.Target == intent.Actor)
            {
                var rejection = new IntentRejected(
                    w.Turn,
                    intent.Actor,
                    Visibility.ActorOnly(intent.Actor),
                    intent.Actor,
                    intent.Type,
                    "invalid target");
                return ValidationResult.Reject(rejection);
            }
        }

        // Apply specific validation rules based on action type
        switch (intent.Type)
        {
            case ActionType.ExportControl:
                return ValidateExportControl(w, intent, clampedMagnitude, commandId);

            case ActionType.Trade:
                return ValidateTrade(w, intent, clampedMagnitude, commandId);

            case ActionType.Subsidize:
            case ActionType.Mobilize:
            case ActionType.Spy:
                return ValidateTreasuryRequired(w, intent, clampedMagnitude, commandId);

            case ActionType.DeployToRegion:
                return ValidateDeployToRegion(w, intent, clampedMagnitude, commandId);

            case ActionType.Withdraw:
                return ValidateWithdraw(w, intent, clampedMagnitude, commandId);

            case ActionType.SignPact:
                return ValidateSignPact(w, intent, clampedMagnitude, commandId);

            case ActionType.BreakPact:
                return ValidateBreakPact(w, intent, clampedMagnitude, commandId);

            case ActionType.Sanction:
            case ActionType.Reassure:
            case ActionType.Threaten:
                // Effective = requested
                var acceptedDefault = new AcceptedCommand(
                    commandId,
                    intent with { RequestedMagnitude = clampedMagnitude },
                    clampedMagnitude);
                return ValidationResult.Accept(acceptedDefault);

            default:
                // Everything else: effective = requested
                var accepted = new AcceptedCommand(
                    commandId,
                    intent with { RequestedMagnitude = clampedMagnitude },
                    clampedMagnitude);
                return ValidationResult.Accept(accepted);
        }
    }

    private static ValidationResult ValidateExportControl(WorldState w, ActionIntent intent, double clampedMagnitude, int commandId)
    {
        // Requires existing trade link Actor→Target
        var link = w.Link(intent.Actor, intent.Target!.Value);
        if (link == null)
        {
            var rejection = new IntentRejected(
                w.Turn,
                intent.Actor,
                Visibility.ActorOnly(intent.Actor),
                intent.Actor,
                intent.Type,
                "no trade link to restrict");
            return ValidationResult.Reject(rejection);
        }

        // Effective = requested × 0.75 if actor is Borvia else × 0.25
        var multiplier = intent.Actor == CountryId.Borvia ? 0.75 : 0.25;
        var effectiveMagnitude = clampedMagnitude * multiplier;

        var accepted = new AcceptedCommand(
            commandId,
            intent with { RequestedMagnitude = clampedMagnitude },
            effectiveMagnitude);
        return ValidationResult.Accept(accepted);
    }

    private static ValidationResult ValidateTrade(WorldState w, ActionIntent intent, double clampedMagnitude, int commandId)
    {
        // Requires existing link Actor→Target (actor-owned export link)
        var link = w.Link(intent.Actor, intent.Target!.Value);
        if (link == null)
        {
            var rejection = new IntentRejected(
                w.Turn,
                intent.Actor,
                Visibility.ActorOnly(intent.Actor),
                intent.Actor,
                intent.Type,
                "no export relationship to expand");
            return ValidationResult.Reject(rejection);
        }

        var accepted = new AcceptedCommand(
            commandId,
            intent with { RequestedMagnitude = clampedMagnitude },
            clampedMagnitude);
        return ValidationResult.Accept(accepted);
    }

    private static ValidationResult ValidateTreasuryRequired(WorldState w, ActionIntent intent, double clampedMagnitude, int commandId)
    {
        // Requires Treasury ≥ 10 × requested
        var actor = w.Countries[intent.Actor];
        if (actor.Treasury < 10 * clampedMagnitude)
        {
            var rejection = new IntentRejected(
                w.Turn,
                intent.Actor,
                Visibility.ActorOnly(intent.Actor),
                intent.Actor,
                intent.Type,
                "insufficient treasury");
            return ValidationResult.Reject(rejection);
        }

        var accepted = new AcceptedCommand(
            commandId,
            intent with { RequestedMagnitude = clampedMagnitude },
            clampedMagnitude);
        return ValidationResult.Accept(accepted);
    }

    private static ValidationResult ValidateDeployToRegion(WorldState w, ActionIntent intent, double clampedMagnitude, int commandId)
    {
        // Requires Readiness ≥ 30
        var actor = w.Countries[intent.Actor];
        if (actor.Readiness < 30)
        {
            var rejection = new IntentRejected(
                w.Turn,
                intent.Actor,
                Visibility.ActorOnly(intent.Actor),
                intent.Actor,
                intent.Type,
                "forces not ready");
            return ValidationResult.Reject(rejection);
        }

        var accepted = new AcceptedCommand(
            commandId,
            intent with { RequestedMagnitude = clampedMagnitude },
            clampedMagnitude);
        return ValidationResult.Accept(accepted);
    }

    private static ValidationResult ValidateWithdraw(WorldState w, ActionIntent intent, double clampedMagnitude, int commandId)
    {
        // Requires ForcesInStrait > 0
        var actor = w.Countries[intent.Actor];
        if (actor.ForcesInStrait <= 0)
        {
            var rejection = new IntentRejected(
                w.Turn,
                intent.Actor,
                Visibility.ActorOnly(intent.Actor),
                intent.Actor,
                intent.Type,
                "no forces deployed");
            return ValidationResult.Reject(rejection);
        }

        var accepted = new AcceptedCommand(
            commandId,
            intent with { RequestedMagnitude = clampedMagnitude },
            clampedMagnitude);
        return ValidationResult.Accept(accepted);
    }

    private static ValidationResult ValidateSignPact(WorldState w, ActionIntent intent, double clampedMagnitude, int commandId)
    {
        // Requires both directional trusts > 0
        var trustActorToTarget = w.Rel(intent.Actor, intent.Target!.Value).Trust;
        var trustTargetToActor = w.Rel(intent.Target.Value, intent.Actor).Trust;

        if (trustActorToTarget <= 0 || trustTargetToActor <= 0)
        {
            var rejection = new IntentRejected(
                w.Turn,
                intent.Actor,
                Visibility.ActorOnly(intent.Actor),
                intent.Actor,
                intent.Type,
                "insufficient mutual trust");
            return ValidationResult.Reject(rejection);
        }

        // Reject if pact exists
        if (w.HasPact(intent.Actor, intent.Target.Value))
        {
            var rejection = new IntentRejected(
                w.Turn,
                intent.Actor,
                Visibility.ActorOnly(intent.Actor),
                intent.Actor,
                intent.Type,
                "pact already exists");
            return ValidationResult.Reject(rejection);
        }

        var accepted = new AcceptedCommand(
            commandId,
            intent with { RequestedMagnitude = clampedMagnitude },
            clampedMagnitude);
        return ValidationResult.Accept(accepted);
    }

    private static ValidationResult ValidateBreakPact(WorldState w, ActionIntent intent, double clampedMagnitude, int commandId)
    {
        // Requires existing pact
        if (!w.HasPact(intent.Actor, intent.Target!.Value))
        {
            var rejection = new IntentRejected(
                w.Turn,
                intent.Actor,
                Visibility.ActorOnly(intent.Actor),
                intent.Actor,
                intent.Type,
                "no pact exists");
            return ValidationResult.Reject(rejection);
        }

        var accepted = new AcceptedCommand(
            commandId,
            intent with { RequestedMagnitude = clampedMagnitude },
            clampedMagnitude);
        return ValidationResult.Accept(accepted);
    }
}
