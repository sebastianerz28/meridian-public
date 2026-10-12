using Meridian.Repro;

// Meridian.Repro: durable reproducibility benchmark bundle. See README.md for the full
// fingerprint spec, manifest schema, and certification workflow -- this file is just CLI
// dispatch; all the actual work lives in InProcessRunner / FullProcessRunner / Verify.

var repoRoot = Cli.ResolveRepoRoot();

/// <summary>Writes a progress/result line to stdout.</summary>
/// <param name="line">The line to write.</param>
void Log(string line) => Console.WriteLine(line);

CliOptions options;
try
{
    options = Cli.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (options.Verify)
{
    var expectedPath = Path.Combine(repoRoot, "benchmarks", "Meridian.Repro", "expected", "expected_hashes.json");
    return await Verify.RunAsync(expectedPath, options.Output, repoRoot, Log, cts.Token).ConfigureAwait(false);
}

Log($"Meridian.Repro: scenario={options.Scenario} seeds=1..{options.Seeds} repeats={options.Repeats} turns={options.Turns} output={options.Output}");

ReproManifest manifest;
if (options.Scenario == Scenarios.FullProcess)
{
    var dllPath = Cli.ResolveHeadlessDllPath(repoRoot);
    if (!File.Exists(dllPath))
    {
        Console.Error.WriteLine(
            $"full-process: '{dllPath}' not found. Run `dotnet build -c Release` from the repository root first " +
            "(full-process spawns the built Game.Headless binary rather than `dotnet run`, to measure realistic " +
            "process-per-seed cost -- it deliberately does not build for you).");
        return 2;
    }

    manifest = await FullProcessRunner.RunAsync(
        options.Seeds, options.Repeats, options.Turns, options.Output, repoRoot, dllPath, Log, cts.Token).ConfigureAwait(false);
}
else
{
    var checkpointSeeds = options.Scenario == Scenarios.StochasticSpyProbe
        ? new[] { 100, 1_000, 10_000 }
        : Array.Empty<int>();

    manifest = await InProcessRunner.RunAsync(
        options.Scenario, options.Seeds, options.Repeats, options.Turns, options.Output, repoRoot,
        checkpointSeeds, Log, cts.Token).ConfigureAwait(false);
}

Log("");
Log($"[{options.Scenario}] mismatches={manifest.Mismatches} distinct_trajectories={manifest.DistinctTrajectories} " +
    $"rng_consumed_seeds={manifest.RngConsumedSeeds}/{manifest.SeedCount} elapsed={manifest.ElapsedSeconds:F2}s");
Log($"[{options.Scenario}] aggregate_fingerprint={manifest.AggregateFingerprint}");
if (manifest.DistinctStdoutTrajectories is not null)
{
    Log($"[{options.Scenario}] stdout_mismatches={manifest.StdoutMismatches} database_mismatches={manifest.DatabaseMismatches}");
    Log($"[{options.Scenario}] distinct_stdout_trajectories={manifest.DistinctStdoutTrajectories} " +
        $"distinct_database_trajectories={manifest.DistinctDatabaseTrajectories} distinct_database_files={manifest.DistinctDatabaseFiles} " +
        $"mean_pair_seconds={manifest.MeanPairSeconds:F3}");
}

var gateFailures = Gates.Check(manifest);
if (gateFailures.Count > 0)
{
    Log("");
    Log("SANITY GATE FAILED:");
    foreach (var f in gateFailures) Log($"  - {f}");
    return 1;
}

Log("");
Log($"[{options.Scenario}] sanity gates passed.");
return 0;
