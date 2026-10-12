using System.Collections.Immutable;
using Game.Contracts;
using Game.Simulation.Observation;
using Game.Simulation.Resolution;
using Game.Simulation.State;
using Game.Simulation.Systems;

namespace Game.Simulation;

/// <summary>
/// Resumable engine snapshot: everything needed to reconstruct a <see cref="TurnEngine"/>
/// against a persisted <c>WorldState</c> such that its next <see cref="TurnEngine.AdvanceTurnAsync"/>
/// call behaves identically to an uninterrupted run.
/// </summary>
/// <param name="RelevantWindows">
/// Each observer's rolling window of relevant events (visible, Minor+ significance, newest
/// first, capped at 15) — the engine maintains these incrementally each turn and uses them
/// for <see cref="DecisionContext.RelevantEvents"/>. Snapshotting them is what makes
/// post-resume decision contexts identical to an uninterrupted run's.
/// </param>
public sealed record EngineSnapshot(
    ulong RngState,
    ulong GameSeed,
    SortedDictionary<CountryId, int> AsleepStreaks,
    SortedDictionary<CountryId, ObserverKnowledgeDto> Knowledge,
    IReadOnlyList<DomainEvent> LastTurnEvents,
    SortedDictionary<CountryId, List<DomainEvent>> RelevantWindows);

public sealed record TurnResult(
    int Turn,
    IReadOnlyList<DomainEvent> Events,
    SortedDictionary<CountryId, StrategicSignificance> SignificanceByObserver,
    IReadOnlyList<CountryId> AwakeActors);

/// <summary>
/// The authoritative per-turn orchestrator: ties passive systems, observation, actor
/// scheduling/decisions, resolution, and reflection into the numbered turn sequence
/// of 13 steps. Private <c>StepN_*</c> methods group the numbered stages.
///
/// Resumability: <see cref="Resume"/> restores rng state, asleep streaks, observer
/// knowledge (from DTOs), <c>lastTurnEvents</c>, and each observer's rolling relevant-event
/// window. <see cref="AllEvents"/> starts empty on a resumed engine — the full history is
/// assumed to live in the database, not in memory — but <see cref="DecisionContext.RelevantEvents"/>
/// is built ONLY from the restored per-observer windows, never from <c>AllEvents</c>. That is
/// what makes a resumed engine's decision contexts identical to an uninterrupted run's: nothing
/// about wake computation, significance, or relevant-event history depends on <c>AllEvents</c>.
/// </summary>
public sealed class TurnEngine
{
    private readonly SortedDictionary<CountryId, ActorRegistration> _actors;
    private readonly List<DomainEvent> _allEvents;
    private readonly DeterministicRng _rng;
    private readonly ulong _gameSeed;
    private readonly SortedDictionary<CountryId, ObserverKnowledge> _knowledge;
    private readonly SortedDictionary<CountryId, int> _asleepStreaks;
    private readonly SortedDictionary<CountryId, List<DomainEvent>> _relevantWindows;
    private List<DomainEvent> _lastTurnEvents;

    private TurnEngine(WorldState world, IReadOnlyList<ActorRegistration> actors)
    {
        World = world;
        _actors = new SortedDictionary<CountryId, ActorRegistration>();
        foreach (var reg in actors) _actors[reg.Country] = reg;
        _allEvents = new List<DomainEvent>();

        // Overwritten by whichever public/private constructor below runs next
        // (fresh-state or resumed-from-snapshot) — never observed with these values.
        _rng = new DeterministicRng(1);
        _knowledge = new SortedDictionary<CountryId, ObserverKnowledge>();
        _asleepStreaks = new SortedDictionary<CountryId, int>();
        _relevantWindows = new SortedDictionary<CountryId, List<DomainEvent>>();
        _lastTurnEvents = new List<DomainEvent>();
    }

    public TurnEngine(WorldState world, IReadOnlyList<ActorRegistration> actors, ulong seed)
        : this(world, actors)
    {
        _gameSeed = seed;
        _rng = new DeterministicRng(seed);
        _knowledge = ObservationSystem.Initialize(world, seed);
        _asleepStreaks = new SortedDictionary<CountryId, int>();
        _relevantWindows = new SortedDictionary<CountryId, List<DomainEvent>>();
        foreach (var country in _actors.Keys)
        {
            _asleepStreaks[country] = 0;
            _relevantWindows[country] = new List<DomainEvent>();
        }
        _lastTurnEvents = new List<DomainEvent>();
    }

    private TurnEngine(WorldState world, IReadOnlyList<ActorRegistration> actors, EngineSnapshot snapshot)
        : this(world, actors)
    {
        _gameSeed = snapshot.GameSeed;
        _rng = new DeterministicRng(snapshot.RngState);
        _knowledge = new SortedDictionary<CountryId, ObserverKnowledge>();
        foreach (var (country, dto) in snapshot.Knowledge) _knowledge[country] = ObserverKnowledge.FromDto(dto);
        _relevantWindows = new SortedDictionary<CountryId, List<DomainEvent>>();
        foreach (var (country, list) in snapshot.RelevantWindows) _relevantWindows[country] = new List<DomainEvent>(list);

        // Every registered actor must have both a knowledge entry and a relevant-events
        // window in the snapshot, or Step2/Step13/BuildRelevantEvents would fail deep
        // inside a turn with a bare KeyNotFoundException. Fail fast and clearly instead.
        foreach (var country in _actors.Keys)
        {
            if (!_knowledge.ContainsKey(country))
                throw new ArgumentException(
                    $"Cannot resume: registered actor '{country}' is missing from snapshot.Knowledge.",
                    nameof(snapshot));
            if (!_relevantWindows.ContainsKey(country))
                throw new ArgumentException(
                    $"Cannot resume: registered actor '{country}' is missing from snapshot.RelevantWindows.",
                    nameof(snapshot));
        }

        _asleepStreaks = new SortedDictionary<CountryId, int>(snapshot.AsleepStreaks);
        _lastTurnEvents = new List<DomainEvent>(snapshot.LastTurnEvents);
    }

    public static TurnEngine Resume(WorldState world, IReadOnlyList<ActorRegistration> actors, EngineSnapshot snapshot) =>
        new(world, actors, snapshot);

    public WorldState World { get; }

    public IReadOnlyList<DomainEvent> AllEvents => _allEvents;

    public EngineSnapshot Snapshot()
    {
        var knowledge = new SortedDictionary<CountryId, ObserverKnowledgeDto>();
        foreach (var (country, k) in _knowledge) knowledge[country] = k.ToDto();

        var windows = new SortedDictionary<CountryId, List<DomainEvent>>();
        foreach (var (country, list) in _relevantWindows) windows[country] = new List<DomainEvent>(list);

        return new EngineSnapshot(
            _rng.State,
            _gameSeed,
            new SortedDictionary<CountryId, int>(_asleepStreaks),
            knowledge,
            new List<DomainEvent>(_lastTurnEvents),
            windows);
    }

    public async Task<TurnResult> AdvanceTurnAsync(CancellationToken ct = default)
    {
        var beginEvents = Step1_PassiveBegin();
        var views = Step2_FreezeViews(beginEvents);
        var (awake, lastTurnSignificance) = Step3_ComputeWakeSet();
        var intents = await Step4_5_Decide(views, awake, lastTurnSignificance, beginEvents, ct);
        var actionEvents = Step6_8_Resolve(intents);
        var endEvents = Step9_PassiveEnd();
        Step10_UpdateKnowledge(actionEvents, endEvents);

        var allTurnEvents = new List<DomainEvent>(beginEvents.Count + actionEvents.Count + endEvents.Count);
        allTurnEvents.AddRange(beginEvents);
        allTurnEvents.AddRange(actionEvents);
        allTurnEvents.AddRange(endEvents);

        var significance = Step11_Significance(allTurnEvents);
        await Step12_GovSyncAndReflect(allTurnEvents, significance, ct);
        return Step13_Finish(allTurnEvents, significance, awake);
    }

    // Step 1: TurnStarted(w.Turn + 1) → reduce, then EconomySystem.BeginTurn and
    // ResourceMarketSystem.BeginTurn, each reduced immediately after generation.
    private IReadOnlyList<DomainEvent> Step1_PassiveBegin()
    {
        var events = new List<DomainEvent>();

        var newTurn = World.Turn + 1;
        var turnStarted = new TurnStarted(newTurn, null, Visibility.Public, newTurn);
        events.Add(turnStarted);
        WorldReducer.Apply(World, turnStarted);

        var economyEvents = EconomySystem.BeginTurn(World);
        events.AddRange(economyEvents);
        WorldReducer.ApplyAll(World, economyEvents);

        var marketEvents = ResourceMarketSystem.BeginTurn(World);
        events.AddRange(marketEvents);
        WorldReducer.ApplyAll(World, marketEvents);

        return events;
    }

    // Step 2: decay per observer, then fold in this turn's begin events, then freeze
    // one ActorView per registered actor. All decisions this turn use these frozen views.
    private SortedDictionary<CountryId, ActorView> Step2_FreezeViews(IReadOnlyList<DomainEvent> beginEvents)
    {
        foreach (var observer in _knowledge.Keys)
            ObservationSystem.Decay(World, _knowledge[observer]);

        ObservationSystem.UpdateFromEvents(World, _knowledge, beginEvents, _gameSeed);

        var views = new SortedDictionary<CountryId, ActorView>();
        foreach (var country in _actors.Keys)
            views[country] = ObservationSystem.Freeze(World, _knowledge[country]);

        return views;
    }

    // Step 3: EveryTurn actors always wake. OnSignificance actors wake iff last turn's
    // events were Significant+ for them, an election is imminent, their asleep streak
    // has reached 3, or this is the very first turn. Streaks reset on wake.
    private (List<CountryId> Awake, SortedDictionary<CountryId, StrategicSignificance> LastTurnSignificance) Step3_ComputeWakeSet()
    {
        var lastTurnSignificance = new SortedDictionary<CountryId, StrategicSignificance>();
        var awake = new List<CountryId>();

        foreach (var (country, reg) in _actors)
        {
            var sig = SignificanceCalculator.MaxForObserver(World, _lastTurnEvents, country);
            lastTurnSignificance[country] = sig;

            var electionCountdown = World.Countries[country].ElectionCountdown;
            var streak = _asleepStreaks.GetValueOrDefault(country, 0);

            var wake = reg.Policy == SchedulingPolicy.EveryTurn
                || sig >= StrategicSignificance.Significant
                || electionCountdown is >= 1 and <= 3
                || streak >= 3
                || World.Turn == 1;

            if (wake)
            {
                awake.Add(country);
                _asleepStreaks[country] = 0;
            }
            else
            {
                _asleepStreaks[country] = streak + 1;
            }
        }

        return (awake, lastTurnSignificance);
    }

    // Steps 4-5: awake actors decide, in enum order, awaited sequentially (never in
    // parallel — determinism). Sleeping actors pass. Identity spoofing is corrected here.
    private async Task<SortedDictionary<CountryId, ActionIntent>> Step4_5_Decide(
        SortedDictionary<CountryId, ActorView> views,
        IReadOnlyList<CountryId> awake,
        SortedDictionary<CountryId, StrategicSignificance> lastTurnSignificance,
        IReadOnlyList<DomainEvent> beginEvents,
        CancellationToken ct)
    {
        var awakeSet = new HashSet<CountryId>(awake);
        var intents = new SortedDictionary<CountryId, ActionIntent>();

        foreach (var (country, reg) in _actors)
        {
            if (!awakeSet.Contains(country))
            {
                intents[country] = ActionIntent.Pass(country);
                continue;
            }

            var relevantEvents = BuildRelevantEvents(country, beginEvents);
            var ctx = new DecisionContext(country, World.Turn, views[country], relevantEvents, lastTurnSignificance[country]);
            var intent = await reg.Actor.DecideAsync(ctx, ct);

            // Engine-owned identity: an actor cannot decide on behalf of another country.
            intents[country] = intent.Actor == country ? intent : ActionIntent.Pass(country);
        }

        return intents;
    }

    // Steps 6-8: validate, order, resolve, and reduce all intents via the canonical pipeline.
    private IReadOnlyList<DomainEvent> Step6_8_Resolve(SortedDictionary<CountryId, ActionIntent> intents)
    {
        var ordered = _actors.Keys.Select(c => intents[c]).ToList();
        return ResolutionPipeline.ResolveAll(World, ordered, _rng);
    }

    // Step 9: end-of-turn passive systems, each reduced immediately after generation.
    private IReadOnlyList<DomainEvent> Step9_PassiveEnd()
    {
        var events = new List<DomainEvent>();

        var straitEvents = StraitSystem.EndTurn(World, _rng);
        events.AddRange(straitEvents);
        WorldReducer.ApplyAll(World, straitEvents);

        var politicsEvents = PoliticsSystem.EndTurn(World);
        events.AddRange(politicsEvents);
        WorldReducer.ApplyAll(World, politicsEvents);

        return events;
    }

    // Step 10: fold action + end events into observer knowledge.
    private void Step10_UpdateKnowledge(IReadOnlyList<DomainEvent> actionEvents, IReadOnlyList<DomainEvent> endEvents)
    {
        var combined = new List<DomainEvent>(actionEvents.Count + endEvents.Count);
        combined.AddRange(actionEvents);
        combined.AddRange(endEvents);
        ObservationSystem.UpdateFromEvents(World, _knowledge, combined, _gameSeed);
    }

    // Step 11: per-observer max significance across ALL of this turn's events.
    private SortedDictionary<CountryId, StrategicSignificance> Step11_Significance(IReadOnlyList<DomainEvent> allTurnEvents)
    {
        var result = new SortedDictionary<CountryId, StrategicSignificance>();
        foreach (var country in _actors.Keys)
            result[country] = SignificanceCalculator.MaxForObserver(World, allTurnEvents, country);
        return result;
    }

    // Step 12: sync any government changes into their actor BEFORE any reflection runs,
    // then let every actor whose turn was Significant+ reflect on it.
    private async Task Step12_GovSyncAndReflect(
        IReadOnlyList<DomainEvent> allTurnEvents,
        SortedDictionary<CountryId, StrategicSignificance> significance,
        CancellationToken ct)
    {
        foreach (var e in allTurnEvents)
        {
            if (e is GovernmentChanged gc && _actors.TryGetValue(gc.Country, out var changedReg))
                changedReg.Actor.OnGovernmentChanged(gc.NewTemplateId);
        }

        foreach (var (country, reg) in _actors)
        {
            if (significance[country] < StrategicSignificance.Significant) continue;

            var visible = allTurnEvents.Where(e => e.Visibility.CanObserve(country)).ToImmutableArray();
            var ctx = new ReflectionContext(country, World.Turn, visible, significance[country]);
            await reg.Actor.ReflectAsync(ctx, ct);
        }
    }

    // Step 13: append to history, remember this turn for next turn's wake computation,
    // and fold this turn's relevant events into each observer's rolling window.
    private TurnResult Step13_Finish(
        IReadOnlyList<DomainEvent> allTurnEvents,
        SortedDictionary<CountryId, StrategicSignificance> significance,
        IReadOnlyList<CountryId> awake)
    {
        _allEvents.AddRange(allTurnEvents);
        _lastTurnEvents = new List<DomainEvent>(allTurnEvents);

        foreach (var country in _actors.Keys)
        {
            var relevant = FilterVisibleMinorPlus(allTurnEvents, country);
            relevant.Reverse(); // newest (last-emitted) first within this turn's batch
            var combined = relevant.Concat(_relevantWindows[country]).Take(15).ToList();
            _relevantWindows[country] = combined;
        }

        return new TurnResult(World.Turn, allTurnEvents, significance, new List<CountryId>(awake));
    }

    // This turn's begin-events (visible, Minor+ significance to the observer), newest
    // first, prepended to the observer's existing rolling window, capped at 15.
    private ImmutableArray<DomainEvent> BuildRelevantEvents(CountryId observer, IReadOnlyList<DomainEvent> beginEvents)
    {
        var filtered = FilterVisibleMinorPlus(beginEvents, observer);
        filtered.Reverse();
        return filtered.Concat(_relevantWindows[observer]).Take(15).ToImmutableArray();
    }

    private List<DomainEvent> FilterVisibleMinorPlus(IReadOnlyList<DomainEvent> events, CountryId observer)
    {
        var result = new List<DomainEvent>();
        foreach (var e in events)
        {
            if (!e.Visibility.CanObserve(observer)) continue;
            if (SignificanceCalculator.ForObserver(World, e, observer) < StrategicSignificance.Minor) continue;
            result.Add(e);
        }
        return result;
    }
}
