using System.Globalization;

namespace Meridian.Repro;

/// <summary>Parsed command-line options for a single invocation.</summary>
/// <param name="Scenario">The scenario to run; empty/unused when <paramref name="Verify"/> is true.</param>
/// <param name="Seeds">Seed count; unused when <paramref name="Verify"/> is true (each expected_hashes.json entry carries its own).</param>
/// <param name="Repeats">Repeats per seed.</param>
/// <param name="Turns">Turns per run.</param>
/// <param name="Output">Base output directory for manifests/CSVs.</param>
/// <param name="Verify">True if <c>--verify</c> was passed.</param>
public sealed record CliOptions(
    string Scenario,
    ulong Seeds,
    int Repeats,
    int Turns,
    string Output,
    bool Verify);

/// <summary>Command-line argument parsing and repo-relative path resolution.</summary>
public static class Cli
{
    private const int DefaultRepeats = 2;
    private const int DefaultTurns = 20;
    private const string DefaultOutput = "artifacts/repro";

    /// <summary>Parses the process's command-line arguments into <see cref="CliOptions"/>.</summary>
    /// <param name="args">The raw command-line arguments.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="ArgumentException">An argument was unrecognized, missing its value, out of range, or <c>--scenario</c> was omitted without <c>--verify</c>.</exception>
    public static CliOptions Parse(string[] args)
    {
        string? scenario = null;
        ulong? seeds = null;
        var repeats = DefaultRepeats;
        var turns = DefaultTurns;
        var output = DefaultOutput;
        var verify = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scenario":
                    scenario = Next(args, ref i);
                    break;
                case "--seeds":
                    seeds = ulong.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--repeats":
                    repeats = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--turns":
                    turns = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--output":
                    output = Next(args, ref i);
                    break;
                case "--verify":
                    verify = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'. See README.md for usage.");
            }
        }

        if (verify)
        {
            // --verify ignores --scenario/--seeds (and, in practice, --repeats/--turns too --
            // it replays exactly what's recorded in expected_hashes.json for each entry).
            return new CliOptions(scenario ?? string.Empty, seeds ?? 0, repeats, turns, output, Verify: true);
        }

        if (scenario is null)
            throw new ArgumentException("Missing required argument '--scenario narrative|stochastic-spy-probe|full-process' (or pass --verify).");
        if (!Scenarios.All.Contains(scenario))
            throw new ArgumentException($"Unknown --scenario '{scenario}'. Expected one of: {string.Join(", ", Scenarios.All)}.");

        seeds ??= DefaultSeeds(scenario);
        if (seeds == 0) throw new ArgumentException("--seeds must be >= 1.");
        if (repeats < 1) throw new ArgumentException("--repeats must be >= 1.");
        if (turns < 1) throw new ArgumentException("--turns must be >= 1.");

        return new CliOptions(scenario, seeds.Value, repeats, turns, output, Verify: false);
    }

    /// <summary>The default <c>--seeds</c> value for a scenario when the flag is omitted.</summary>
    /// <param name="scenario">One of <see cref="Scenarios.Narrative"/>, <see cref="Scenarios.StochasticSpyProbe"/>, <see cref="Scenarios.FullProcess"/>.</param>
    /// <returns>40 for narrative and full-process; 10,000 for stochastic-spy-probe.</returns>
    /// <exception cref="ArgumentException"><paramref name="scenario"/> is unrecognized.</exception>
    public static ulong DefaultSeeds(string scenario) => scenario switch
    {
        Scenarios.Narrative => 40,
        Scenarios.StochasticSpyProbe => 10_000,
        Scenarios.FullProcess => 40,
        _ => throw new ArgumentException($"Unknown scenario '{scenario}'."),
    };

    /// <summary>
    /// Search upward from the current directory for Meridian.sln, mirroring
    /// Game.Headless/Program.cs's llm.config.json lookup -- robust to being invoked via
    /// `dotnet run --project benchmarks/Meridian.Repro` from the repo root (the common case,
    /// where CWD already IS the repo root) as well as from a subdirectory or CI runner whose
    /// working directory isn't guaranteed.
    /// </summary>
    /// <returns>The repository root directory, or the current directory if Meridian.sln couldn't be found within 8 levels up.</returns>
    public static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var depth = 0; depth < 8 && dir is not null; depth++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Meridian.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    /// <summary>Computes the expected path to the built headless binary (does not check that it exists).</summary>
    /// <param name="repoRoot">Repository root.</param>
    /// <returns>The full path to <c>src/Game.Headless/bin/Release/net8.0/Game.Headless.dll</c> under <paramref name="repoRoot"/>.</returns>
    public static string ResolveHeadlessDllPath(string repoRoot) =>
        Path.Combine(repoRoot, "src", "Game.Headless", "bin", "Release", "net8.0", "Game.Headless.dll");

    /// <summary>Consumes and returns the argument following <c>args[i]</c>, advancing <paramref name="i"/>.</summary>
    /// <param name="args">The full argument array.</param>
    /// <param name="i">Index of the flag; advanced to the value's index on return.</param>
    /// <returns>The value following the flag.</returns>
    /// <exception cref="ArgumentException">The flag is the last argument (no value follows it).</exception>
    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for argument '{args[i]}'.");
        return args[++i];
    }
}
