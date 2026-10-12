using Game.Contracts;
using Game.Simulation.State;
using Game.Simulation.Validation;

namespace Game.Simulation.Resolution;

/// <summary>
/// Canonical resolution pipeline: validates all intents, sorts accepted commands
/// deterministically, resolves each in order while applying events to world state
/// between each resolution.
///
/// Order: rejections first, then resolution events (in resolution order).
///
/// Accepted command sort key: (Category asc, ActionType asc, ActorId asc, TargetId asc, CommandId asc)
/// Categories: Economic → Diplomatic → Military → Covert
/// TargetId nulls sort before values.
/// </summary>
public static class ResolutionPipeline
{
    public static IReadOnlyList<DomainEvent> ResolveAll(
        WorldState w,
        IReadOnlyList<ActionIntent> intents,
        DeterministicRng rng)
    {
        var allEvents = new List<DomainEvent>();
        var acceptedCommands = new List<AcceptedCommand>();

        // Step 1: Validate all intents in input order, collecting rejections
        for (int i = 0; i < intents.Count; i++)
        {
            var result = IntentValidator.Validate(w, intents[i], i);
            if (result.Rejection != null)
            {
                allEvents.Add(result.Rejection);
            }
            else if (result.Command != null)
            {
                acceptedCommands.Add(result.Command);
            }
        }

        // Step 2: Sort accepted commands by canonical order
        acceptedCommands.Sort((a, b) =>
        {
            // Compare by Category
            var catA = a.Intent.Type.Category();
            var catB = b.Intent.Type.Category();
            var catCmp = catA.CompareTo(catB);
            if (catCmp != 0) return catCmp;

            // Compare by ActionType
            var typeCmp = a.Intent.Type.CompareTo(b.Intent.Type);
            if (typeCmp != 0) return typeCmp;

            // Compare by ActorId
            var actorCmp = a.Intent.Actor.CompareTo(b.Intent.Actor);
            if (actorCmp != 0) return actorCmp;

            // Compare by TargetId (nulls sort before values)
            var targetACmp = a.Intent.Target.HasValue ? 1 : 0;
            var targetBCmp = b.Intent.Target.HasValue ? 1 : 0;
            if (targetACmp != targetBCmp) return targetACmp.CompareTo(targetBCmp);

            // If both null or both have values
            if (a.Intent.Target.HasValue && b.Intent.Target.HasValue)
            {
                var targetCmp = a.Intent.Target.Value.CompareTo(b.Intent.Target.Value);
                if (targetCmp != 0) return targetCmp;
            }

            // Compare by CommandId
            return a.CommandId.CompareTo(b.CommandId);
        });

        // Step 3: Resolve each command in order, applying events to world before next
        foreach (var command in acceptedCommands)
        {
            var resolutionEvents = Resolvers.Resolve(w, command, rng);
            allEvents.AddRange(resolutionEvents);
            // Apply events to world state before resolving the next command
            WorldReducer.ApplyAll(w, resolutionEvents);
        }

        return allEvents;
    }
}
