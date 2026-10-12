using Game.Contracts;
using Game.Headless;

namespace Meridian.Repro;

/// <summary>
/// The three benchmark scenarios' actor sets. "narrative" reuses
/// <c>Game.Headless.ScriptedStoryActors</c> verbatim (see Meridian.Repro.csproj's project
/// reference) rather than duplicating it, so the benchmark can never drift from the actual
/// headless story. "stochastic-spy-probe" is benchmark-owned: every registered actor forces
/// RNG consumption via <see cref="ActionType.Spy"/> once its trigger turn is reached, which
/// is the whole point of this scenario (narrative consumes none; this one always does).
/// </summary>
public static class Scenarios
{
    public const string Narrative = "narrative";
    public const string StochasticSpyProbe = "stochastic-spy-probe";
    public const string FullProcess = "full-process";

    public static readonly string[] All = { Narrative, StochasticSpyProbe, FullProcess };

    /// <summary>Builds the actor set for an in-process scenario (narrative or stochastic-spy-probe).</summary>
    /// <param name="scenario">One of <see cref="Narrative"/> or <see cref="StochasticSpyProbe"/>.</param>
    /// <returns>A fresh <see cref="ActorRegistration"/> list (stateless actors — safe to reuse across seeds).</returns>
    /// <exception cref="ArgumentException"><paramref name="scenario"/> is <see cref="FullProcess"/> or otherwise unrecognized.</exception>
    public static IReadOnlyList<ActorRegistration> CreateActors(string scenario) => scenario switch
    {
        Narrative => ScriptedStoryActors.Create(),
        StochasticSpyProbe => CreateStochasticSpyProbeActors(),
        _ => throw new ArgumentException($"'{scenario}' has no in-process actor set (full-process spawns the headless binary instead)."),
    };

    /// <summary>Builds the benchmark-owned stochastic-spy-probe actor set (see the class summary for the per-country script).</summary>
    /// <returns>The three-country registration list, all <see cref="SchedulingPolicy.EveryTurn"/>.</returns>
    private static IReadOnlyList<ActorRegistration> CreateStochasticSpyProbeActors() => new ActorRegistration[]
    {
        new(CountryId.Borvia, new ScriptedActor(BorviaScript), SchedulingPolicy.EveryTurn),
        new(CountryId.Alderia, new ScriptedActor(AlderiaScript), SchedulingPolicy.EveryTurn),
        new(CountryId.Cascara, new ScriptedActor(CascaraScript), SchedulingPolicy.EveryTurn),
    };

    /// <summary>Borvia's script: Pass on turn 1, then Spy(Alderia, 0.5) every turn from turn 2 onward.</summary>
    /// <param name="ctx">This turn's decision context.</param>
    /// <returns>The intent for this turn.</returns>
    private static ActionIntent BorviaScript(DecisionContext ctx) => ctx.Turn switch
    {
        >= 2 => new ActionIntent(CountryId.Borvia, ActionType.Spy, CountryId.Alderia, 0.5, "probing Alderia"),
        _ => ActionIntent.Pass(CountryId.Borvia),
    };

    /// <summary>Alderia's script: Mobilize(0.5) at T3, DeployToRegion(0.8) at T5, then Spy(Borvia, 0.5) every turn from T7 onward; Pass otherwise.</summary>
    /// <param name="ctx">This turn's decision context.</param>
    /// <returns>The intent for this turn.</returns>
    private static ActionIntent AlderiaScript(DecisionContext ctx) => ctx.Turn switch
    {
        3 => new ActionIntent(CountryId.Alderia, ActionType.Mobilize, null, 0.5, "readiness posture"),
        5 => new ActionIntent(CountryId.Alderia, ActionType.DeployToRegion, null, 0.8, "standing with Cascara"),
        >= 7 => new ActionIntent(CountryId.Alderia, ActionType.Spy, CountryId.Borvia, 0.5, "probing Borvia"),
        _ => ActionIntent.Pass(CountryId.Alderia),
    };

    /// <summary>Cascara's script: Reassure(Alderia, 0.6) at T4, then Spy(Borvia, 0.4) every turn from T10 onward; Pass otherwise.</summary>
    /// <param name="ctx">This turn's decision context.</param>
    /// <returns>The intent for this turn.</returns>
    private static ActionIntent CascaraScript(DecisionContext ctx) => ctx.Turn switch
    {
        4 => new ActionIntent(CountryId.Cascara, ActionType.Reassure, CountryId.Alderia, 0.6, "the alliance holds"),
        >= 10 => new ActionIntent(CountryId.Cascara, ActionType.Spy, CountryId.Borvia, 0.4, "probing Borvia"),
        _ => ActionIntent.Pass(CountryId.Cascara),
    };
}

/// <summary>Same shape as Game.Headless's private <c>ScriptedActor</c> — a pure function of the decision context, no mutable state, safe to share across concurrently-running seeds.</summary>
file sealed class ScriptedActor(Func<DecisionContext, ActionIntent> script) : IActor
{
    /// <summary>Evaluates the wrapped script function synchronously and wraps its result in a completed task.</summary>
    /// <param name="ctx">This turn's decision context.</param>
    /// <param name="ct">Unused — the script is a pure, synchronous function.</param>
    /// <returns>The script's chosen intent for this turn.</returns>
    public Task<ActionIntent> DecideAsync(DecisionContext ctx, CancellationToken ct) =>
        Task.FromResult(script(ctx));

    /// <summary>No-op — scripted actors never reflect.</summary>
    /// <param name="ctx">Unused.</param>
    /// <param name="ct">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task ReflectAsync(ReflectionContext ctx, CancellationToken ct) => Task.CompletedTask;
}
