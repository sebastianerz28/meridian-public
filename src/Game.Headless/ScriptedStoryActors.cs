using Game.Contracts;

namespace Game.Headless;

/// <summary>
/// A small hand-authored story used to smoke-test the engine end to end: Borvia squeezes
/// Alderia's rare-earth supply, both sides posture militarily, and Cascara tries to hold
/// the alliance together. Every registration is <see cref="SchedulingPolicy.EveryTurn"/>
/// so the scripted beats always land on the exact turn they're written for, regardless of
/// the wake/sleep heuristics that would otherwise apply to an LLM-driven actor.
/// </summary>
public static class ScriptedStoryActors
{
    public static IReadOnlyList<ActorRegistration> Create() => new ActorRegistration[]
    {
        new(CountryId.Borvia, new ScriptedActor(BorviaScript), SchedulingPolicy.EveryTurn),
        new(CountryId.Alderia, new ScriptedActor(AlderiaScript), SchedulingPolicy.EveryTurn),
        new(CountryId.Cascara, new ScriptedActor(CascaraScript), SchedulingPolicy.EveryTurn),
    };

    private static ActionIntent BorviaScript(DecisionContext ctx) => ctx.Turn switch
    {
        2 => new ActionIntent(CountryId.Borvia, ActionType.ExportControl, CountryId.Alderia, 0.8, "punishment"),
        6 => new ActionIntent(CountryId.Borvia, ActionType.DeployToRegion, null, 0.6, "securing the strait"),
        _ => ActionIntent.Pass(CountryId.Borvia),
    };

    private static ActionIntent AlderiaScript(DecisionContext ctx) => ctx.Turn switch
    {
        3 => new ActionIntent(CountryId.Alderia, ActionType.Mobilize, null, 0.6, "readiness posture"),
        5 => new ActionIntent(CountryId.Alderia, ActionType.DeployToRegion, null, 0.6, "standing with Cascara"),
        _ => ActionIntent.Pass(CountryId.Alderia),
    };

    private static ActionIntent CascaraScript(DecisionContext ctx) => ctx.Turn switch
    {
        4 => new ActionIntent(CountryId.Cascara, ActionType.Reassure, CountryId.Alderia, 0.6, "the alliance holds"),
        _ => ActionIntent.Pass(CountryId.Cascara),
    };
}

file sealed class ScriptedActor(Func<DecisionContext, ActionIntent> script) : IActor
{
    public Task<ActionIntent> DecideAsync(DecisionContext ctx, CancellationToken ct) =>
        Task.FromResult(script(ctx));

    public Task ReflectAsync(ReflectionContext ctx, CancellationToken ct) => Task.CompletedTask;
}
