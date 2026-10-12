using System.Globalization;
using System.Text.Json;
using Game.Agents;
using Game.Contracts;
using Game.Headless;
using Game.Persistence;
using Game.Simulation;
using Game.Simulation.State;

var options = ProgramOptions.Parse(args);

// Preflight for LLM-backed modes: build the config + client (which validates
// ANTHROPIC_API_KEY for the Claude provider) BEFORE touching run.db at all — a fresh
// run must not delete an existing database, and a resume must not even open one, if
// we're about to fail on a missing key.
HttpClient? http = null;
ILlmClient? llmClient = null;
if (options.ActorsMode is "llm" or "play")
{
    try
    {
        var configPath = ResolveLlmConfigPath();
        var llmConfig = LlmConfig.Load(configPath);
        http = new HttpClient();
        llmClient = llmConfig.CreateClient(http);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Cannot start '--actors {options.ActorsMode}': {ex.Message}");
        http?.Dispose();
        return 1;
    }
}

string dbPath;
if (options.Resume is not null)
{
    dbPath = Path.GetFullPath(options.Resume);
    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"--resume: file not found: {dbPath}");
        http?.Dispose();
        return 1;
    }
}
else
{
    // Fresh-run lifecycle: a bare invocation always starts from an empty database.
    dbPath = Path.Combine(Directory.GetCurrentDirectory(), "run.db");
    if (File.Exists(dbPath))
        File.Delete(dbPath);
}

using var store = new GameStore(dbPath);
store.InitSchema(); // CREATE TABLE IF NOT EXISTS — safe to call again on an existing db.

WorldState world;
EngineSnapshot? resumeSnapshot = null;
Dictionary<string, string> resumedAgents = new();
if (options.Resume is not null)
{
    var loaded = store.LoadLatestSnapshot();
    if (loaded is null)
    {
        Console.Error.WriteLine($"--resume: '{dbPath}' contains no completed turns to resume from.");
        http?.Dispose();
        return 1;
    }
    world = loaded.Value.World;
    resumeSnapshot = loaded.Value.Engine;
    resumedAgents = ParseAgentsJson(loaded.Value.AgentsJson);
}
else
{
    world = Scenario.CreateMeridianStrait();
}

var telemetry = new SqliteAgentTelemetry(store);

IReadOnlyList<ActorRegistration> actors = options.ActorsMode switch
{
    "scripted" => ScriptedStoryActors.Create(),
    "llm" => BuildLlmActors(resumedAgents, llmClient!, telemetry),
    "play" => BuildPlayActors(resumedAgents, llmClient!, telemetry),
    _ => throw new ArgumentException($"Unknown --actors mode '{options.ActorsMode}'. Supported: scripted, llm, play."),
};

var engine = resumeSnapshot is not null
    ? TurnEngine.Resume(world, actors, resumeSnapshot)
    : new TurnEngine(world, actors, options.Seed);

var newsHistory = new List<string>();

for (var i = 0; i < options.Turns; i++)
{
    var result = await engine.AdvanceTurnAsync();

    var agentsJson = BuildAgentsJson(actors);
    store.SaveTurn(result.Turn, engine.World, result.Events, agentsJson, engine.Snapshot());

    var news = NewsRenderer.Render(result.Events, CountryId.Alderia);
    if (news.Length > 0)
    {
        Console.WriteLine(news);
        newsHistory.Add(news);
    }
}

PrintFinalStandings(engine.World, engine.World.Turn);

if (options.ActorsMode is "llm" or "play")
{
    PrintNewsHistory(newsHistory);
    PrintAgentMemories(store, actors);
}

http?.Dispose();
return 0;

static string ResolveLlmConfigPath()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    for (var depth = 0; depth < 8 && dir is not null; depth++)
    {
        var candidate = Path.Combine(dir.FullName, "llm.config.json");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }

    throw new FileNotFoundException(
        "llm.config.json not found in the current directory or any parent directory " +
        "(searched up to 8 levels up). Run from the repository root, where llm.config.json lives, " +
        "or create one there — see README.md.");
}

static IReadOnlyList<ActorRegistration> BuildLlmActors(
    Dictionary<string, string> resumedAgents, ILlmClient llmClient, IAgentTelemetry telemetry) => new ActorRegistration[]
{
    new(CountryId.Alderia,
        new LlmActor(GetOrCreateAgentState(CountryId.Alderia, "alderia-atlantic-coalition", resumedAgents), llmClient, telemetry),
        SchedulingPolicy.OnSignificance),
    new(CountryId.Borvia,
        new LlmActor(GetOrCreateAgentState(CountryId.Borvia, "borvia-national-front", resumedAgents), llmClient, telemetry),
        SchedulingPolicy.OnSignificance),
    new(CountryId.Cascara,
        new LlmActor(GetOrCreateAgentState(CountryId.Cascara, "cascara-maritime-league", resumedAgents), llmClient, telemetry),
        SchedulingPolicy.OnSignificance),
};

static IReadOnlyList<ActorRegistration> BuildPlayActors(
    Dictionary<string, string> resumedAgents, ILlmClient llmClient, IAgentTelemetry telemetry) => new ActorRegistration[]
{
    new(CountryId.Alderia, new ConsolePlayerActor(), SchedulingPolicy.EveryTurn),
    new(CountryId.Borvia,
        new LlmActor(GetOrCreateAgentState(CountryId.Borvia, "borvia-national-front", resumedAgents), llmClient, telemetry),
        SchedulingPolicy.OnSignificance),
    new(CountryId.Cascara,
        new LlmActor(GetOrCreateAgentState(CountryId.Cascara, "cascara-maritime-league", resumedAgents), llmClient, telemetry),
        SchedulingPolicy.OnSignificance),
};

static AgentState GetOrCreateAgentState(CountryId country, string defaultTemplateId, Dictionary<string, string> resumedAgents) =>
    resumedAgents.TryGetValue(country.ToString(), out var json)
        ? AgentState.FromJson(json)
        : AgentState.FromTemplate(country, defaultTemplateId);

// agentsJson is a JSON object mapping country name -> AgentState.ToJson() (itself a JSON
// string), so it round-trips through GameStore's plain-text agents_json column. Actors with
// no AgentState (ConsolePlayerActor) simply contribute no entry.
static string BuildAgentsJson(IReadOnlyList<ActorRegistration> actors)
{
    var map = new Dictionary<string, string>();
    foreach (var reg in actors)
    {
        if (reg.Actor is LlmActor llmActor)
            map[reg.Country.ToString()] = llmActor.State.ToJson();
    }
    return JsonSerializer.Serialize(map);
}

static Dictionary<string, string> ParseAgentsJson(string json)
{
    if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
    return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}

static void PrintFinalStandings(WorldState world, int turns)
{
    Console.WriteLine();
    Console.WriteLine($"=== Final Standings (after turn {turns}) ===");
    Console.WriteLine(
        $"{"Country",-10} {"Gdp",10} {"Inflation",10} {"Treasury",10} {"Approval",10} {"Readiness",10} {"ForcesInStrait",14} {"Stockpile",10} {"Government",-24}");

    // Countries is a SortedDictionary<CountryId,_>, so this iterates in a fixed,
    // deterministic order (Alderia, Borvia, Cascara) every run.
    foreach (var (id, c) in world.Countries)
    {
        var templateName = GovernmentTemplates.Get(c.GovernmentTemplateId).Name;
        Console.WriteLine(
            $"{id,-10} {F(c.Gdp),10} {P(c.Inflation),10} {F(c.Treasury),10} {F(c.Approval),10} {F(c.Readiness),10} {F(c.ForcesInStrait),14} {F(c.ResourceStockpile),10} {templateName,-24}");
    }
}

static void PrintNewsHistory(IReadOnlyList<string> newsHistory)
{
    Console.WriteLine();
    Console.WriteLine("=== Alderia News History ===");
    if (newsHistory.Count == 0)
    {
        Console.WriteLine("(no newsworthy events)");
        return;
    }
    foreach (var batch in newsHistory)
        Console.WriteLine(batch);
}

static void PrintAgentMemories(GameStore store, IReadOnlyList<ActorRegistration> actors)
{
    Console.WriteLine();
    Console.WriteLine("=== Agent Memories ===");
    foreach (var reg in actors)
    {
        if (reg.Actor is not LlmActor) continue;

        Console.WriteLine($"-- {reg.Country} --");
        var memories = store.LoadMemories(reg.Country, limit: 1000);
        if (memories.Count == 0)
        {
            Console.WriteLine("(none)");
            continue;
        }
        // LoadMemories returns newest-first; print oldest-first for a readable timeline.
        foreach (var (turn, text) in Enumerable.Reverse(memories))
            Console.WriteLine($"T{turn.ToString(CultureInfo.InvariantCulture)}: {text}");
    }
}

static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

// "P1" under InvariantCulture inserts a space before the '%' (e.g. "6.0 %"); build the
// string manually to keep the table compact and consistent with the news feed.
static string P(double v) => $"{(v * 100).ToString("0.0", CultureInfo.InvariantCulture)}%";

sealed record ProgramOptions(int Turns, ulong Seed, string ActorsMode, string? Resume)
{
    public static ProgramOptions Parse(string[] args)
    {
        var turns = 20;
        var seed = 42UL;
        var actorsMode = "scripted";
        string? resume = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--turns":
                    turns = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--seed":
                    seed = ulong.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--actors":
                    actorsMode = Next(args, ref i);
                    break;
                case "--resume":
                    resume = Next(args, ref i);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new ProgramOptions(turns, seed, actorsMode, resume);
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for argument '{args[i]}'.");
        return args[++i];
    }
}
