using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Repro;

/// <summary>One recorded ground-truth row from expected_hashes.json: a single (scenario, seed count) combination and the aggregate fingerprint it must reproduce.</summary>
/// <param name="Scenario">The scenario name (narrative, stochastic-spy-probe, or full-process).</param>
/// <param name="Seeds">Seed count for this entry (1..Seeds).</param>
/// <param name="Repeats">Repeats per seed used when this entry was recorded.</param>
/// <param name="Turns">Turns per run used when this entry was recorded.</param>
/// <param name="AggregateFingerprint">The recorded ground-truth aggregate proof hash.</param>
public sealed record ExpectedEntry(
    [property: JsonPropertyName("scenario")] string Scenario,
    [property: JsonPropertyName("seeds")] int Seeds,
    [property: JsonPropertyName("repeats")] int Repeats,
    [property: JsonPropertyName("turns")] int Turns,
    [property: JsonPropertyName("aggregate_fingerprint")] string AggregateFingerprint);

/// <summary>The full contents of expected/expected_hashes.json.</summary>
/// <param name="SimulationCommit">Full SHA of the simulation commit, or empty for an initial snapshot baseline.</param>
/// <param name="GeneratedUtc">ISO-8601 UTC timestamp of generation.</param>
/// <param name="Entries">The recorded ground-truth rows.</param>
public sealed record ExpectedHashes(
    [property: JsonPropertyName("simulation_commit")] string SimulationCommit,
    [property: JsonPropertyName("generated_utc")] string GeneratedUtc,
    [property: JsonPropertyName("entries")] IReadOnlyList<ExpectedEntry> Entries);

/// <summary>
/// --verify: re-run exactly the (scenario, seeds, repeats, turns) combinations recorded in
/// expected/expected_hashes.json, compare aggregate fingerprints, AND re-apply each
/// scenario's <see cref="Gates"/> to the re-run's own manifest. The gate re-check matters
/// independently of the aggregate comparison: the aggregate is built from each seed's repeat-0
/// fingerprint only (see <see cref="SeedResult.CanonicalFingerprint"/>), so a verifier's
/// machine could in principle produce REAL repeat mismatches (live non-determinism on that
/// machine) while repeat 0 still happens to match the recorded aggregate. <c>Gates.Check</c>
/// reads <c>manifest.Mismatches</c>, which IS computed from every repeat, so folding gate
/// failures into the exit code closes that gap -- a verifier's "VERIFY OK" now certifies both
/// "matches the recorded ground truth" and "was internally reproducible on my machine, not
/// just lucky on repeat 0."
///
/// When simulation_commit is recorded, warn if HEAD differs but continue verification.
/// An empty simulation_commit denotes an initial snapshot baseline. Aggregate fingerprint
/// mismatches and sanity-gate failures fail verification in either case.
/// </summary>
public static class Verify
{
    /// <summary>Loads expected_hashes.json and re-runs every recorded (scenario, seeds) combination, comparing aggregate fingerprints.</summary>
    /// <param name="expectedPath">Path to expected/expected_hashes.json.</param>
    /// <param name="outputDir">Base output directory; re-run artifacts are written under <c>&lt;outputDir&gt;/verify</c>.</param>
    /// <param name="repoRoot">Repository root, used for git provenance and locating the built headless binary.</param>
    /// <param name="log">Sink for progress and result lines.</param>
    /// <param name="ct">Cancellation token observed during the re-runs.</param>
    /// <returns>0 if every entry's aggregate fingerprint reproduced AND every re-run's sanity gates passed; 1 if any aggregate mismatched or any gate failed; 2 if expected_hashes.json was missing or unparsable.</returns>
    public static async Task<int> RunAsync(string expectedPath, string outputDir, string repoRoot, Action<string> log, CancellationToken ct)
    {
        if (!File.Exists(expectedPath))
        {
            log($"--verify: expected hashes file not found at '{expectedPath}'.");
            return 2;
        }

        ExpectedHashes expected;
        try
        {
            var json = await File.ReadAllTextAsync(expectedPath, ct).ConfigureAwait(false);
            expected = JsonSerializer.Deserialize<ExpectedHashes>(json)
                ?? throw new InvalidOperationException("deserialized to null");
        }
        catch (Exception ex)
        {
            log($"--verify: could not parse '{expectedPath}': {ex.Message}");
            return 2;
        }

        var headCommit = ManifestIo.GetGitCommit(repoRoot);
        if (!string.IsNullOrWhiteSpace(expected.SimulationCommit) &&
            !string.Equals(headCommit, expected.SimulationCommit, StringComparison.Ordinal))
        {
            log($"WARNING: HEAD ({headCommit}) differs from the recorded simulation_commit ({expected.SimulationCommit}). " +
                "Proceeding anyway -- verification only fails on an actual aggregate fingerprint mismatch (see README's " +
                "'expected_hashes/--verify workflow' section for the policy).");
        }

        var verifyOutputDir = Path.Combine(outputDir, "verify");
        var failures = new List<string>();

        foreach (var group in expected.Entries.GroupBy(e => e.Scenario))
        {
            switch (group.Key)
            {
                case Scenarios.Narrative:
                    foreach (var entry in group)
                        await VerifyInProcessEntryAsync(entry, verifyOutputDir, repoRoot, checkpointSeeds: Array.Empty<int>(), log, failures, ct).ConfigureAwait(false);
                    break;

                case Scenarios.StochasticSpyProbe:
                    // "One 10,000-seed run yields all three [checkpoint] entries" -- run once at
                    // the group's max seed count and match every entry against the checkpoint
                    // whose `seeds` equals that entry's `seeds`, instead of re-running per entry.
                    await VerifyStochasticGroupAsync(group.ToList(), verifyOutputDir, repoRoot, log, failures, ct).ConfigureAwait(false);
                    break;

                case Scenarios.FullProcess:
                    foreach (var entry in group)
                        await VerifyFullProcessEntryAsync(entry, verifyOutputDir, repoRoot, log, failures, ct).ConfigureAwait(false);
                    break;

                default:
                    failures.Add($"Unknown scenario '{group.Key}' in expected_hashes.json entries.");
                    break;
            }
        }

        if (failures.Count > 0)
        {
            log("");
            log("VERIFY FAILED:");
            foreach (var f in failures) log($"  - {f}");
            return 1;
        }

        log("");
        log($"VERIFY OK: all {expected.Entries.Count} recorded aggregate fingerprint(s) reproduced, and every re-run's sanity gates passed.");
        return 0;
    }

    /// <summary>Re-runs a single narrative entry in-process and compares its aggregate fingerprint.</summary>
    /// <param name="entry">The recorded entry to reproduce.</param>
    /// <param name="outputDir">Directory to write the re-run's manifest/CSV into.</param>
    /// <param name="repoRoot">Repository root, for git provenance.</param>
    /// <param name="checkpointSeeds">Checkpoint seed counts to pass through (empty for narrative).</param>
    /// <param name="log">Sink for progress lines.</param>
    /// <param name="failures">Mismatch/error messages are appended here (never thrown).</param>
    /// <param name="ct">Cancellation token observed during the re-run.</param>
    private static async Task VerifyInProcessEntryAsync(
        ExpectedEntry entry, string outputDir, string repoRoot, IReadOnlyList<int> checkpointSeeds,
        Action<string> log, List<string> failures, CancellationToken ct)
    {
        log($"[verify] re-running {entry.Scenario}: seeds=1..{entry.Seeds} repeats={entry.Repeats} turns={entry.Turns}");
        var manifest = await InProcessRunner.RunAsync(
            entry.Scenario, (ulong)entry.Seeds, entry.Repeats, entry.Turns, outputDir, repoRoot, checkpointSeeds, log, ct).ConfigureAwait(false);
        LogAndGate(entry.Scenario, entry.Seeds, manifest, log, failures);
        Compare(entry, entry.Seeds, manifest.AggregateFingerprint, failures);
    }

    /// <summary>Re-runs every stochastic-spy-probe entry in the group with a single run at the group's max seed count, matching each entry against the checkpoint at its own seed count.</summary>
    /// <param name="entries">All expected_hashes.json entries for the stochastic-spy-probe scenario.</param>
    /// <param name="outputDir">Directory to write the re-run's manifest/CSV into.</param>
    /// <param name="repoRoot">Repository root, for git provenance.</param>
    /// <param name="log">Sink for progress lines.</param>
    /// <param name="failures">Mismatch/error messages are appended here (never thrown).</param>
    /// <param name="ct">Cancellation token observed during the re-run.</param>
    private static async Task VerifyStochasticGroupAsync(
        List<ExpectedEntry> entries, string outputDir, string repoRoot, Action<string> log, List<string> failures, CancellationToken ct)
    {
        var maxSeeds = entries.Max(e => e.Seeds);
        var repeats = entries[0].Repeats;
        var turns = entries[0].Turns;
        if (entries.Any(e => e.Repeats != repeats || e.Turns != turns))
        {
            failures.Add("stochastic-spy-probe entries in expected_hashes.json disagree on repeats/turns -- cannot verify with a single run.");
            return;
        }

        var checkpointSeeds = entries.Select(e => e.Seeds).Distinct().OrderBy(s => s).ToList();
        log($"[verify] re-running {Scenarios.StochasticSpyProbe}: seeds=1..{maxSeeds} repeats={repeats} turns={turns} (checkpoints: {string.Join(", ", checkpointSeeds)})");
        var manifest = await InProcessRunner.RunAsync(
            Scenarios.StochasticSpyProbe, (ulong)maxSeeds, repeats, turns, outputDir, repoRoot, checkpointSeeds, log, ct).ConfigureAwait(false);

        // Gates.Check reads the FULL run's manifest (every seed, every repeat) -- this is what
        // catches real repeat mismatches that the per-checkpoint aggregate comparison below
        // cannot (an aggregate is built from repeat-0 fingerprints only). One gate check
        // covers all of this group's entries since they all come from the same run.
        LogAndGate(Scenarios.StochasticSpyProbe, maxSeeds, manifest, log, failures);

        foreach (var entry in entries)
        {
            var checkpoint = manifest.TimingCheckpoints.FirstOrDefault(c => c.Seeds == entry.Seeds);
            if (checkpoint is null)
            {
                failures.Add($"stochastic-spy-probe: no checkpoint recorded at seeds={entry.Seeds} (this should be unreachable -- report a bug).");
                continue;
            }
            log($"[verify]   checkpoint seeds={checkpoint.Seeds}: mismatches={checkpoint.Mismatches} distinct_trajectories={checkpoint.DistinctTrajectories} aggregate_fingerprint={checkpoint.AggregateFingerprint}");
            if (checkpoint.Mismatches != 0)
                failures.Add($"stochastic-spy-probe @ seeds={entry.Seeds}: checkpoint reports {checkpoint.Mismatches} repeat mismatch(es) (expected 0).");
            Compare(entry, entry.Seeds, checkpoint.AggregateFingerprint, failures);
        }
    }

    /// <summary>Logs a re-run manifest's headline numbers and folds any <see cref="Gates"/> failures into <paramref name="failures"/>.</summary>
    /// <param name="scenario">The scenario name, for log/failure-message prefixing.</param>
    /// <param name="seeds">The seed count this re-run covers, for log/failure-message context.</param>
    /// <param name="manifest">The re-run's manifest to check.</param>
    /// <param name="log">Sink for the summary line.</param>
    /// <param name="failures">Gate failure messages are appended here (never thrown).</param>
    private static void LogAndGate(string scenario, int seeds, ReproManifest manifest, Action<string> log, List<string> failures)
    {
        log($"[verify] {scenario} @ seeds={seeds}: mismatches={manifest.Mismatches} distinct_trajectories={manifest.DistinctTrajectories} " +
            $"rng_consumed_seeds={manifest.RngConsumedSeeds}/{manifest.SeedCount}" +
            (manifest.DistinctStdoutTrajectories is null ? "" :
                $" stdout_mismatches={manifest.StdoutMismatches} database_mismatches={manifest.DatabaseMismatches} " +
                $"distinct_stdout_trajectories={manifest.DistinctStdoutTrajectories} " +
                $"distinct_database_trajectories={manifest.DistinctDatabaseTrajectories} distinct_database_files={manifest.DistinctDatabaseFiles}"));

        foreach (var gateFailure in Gates.Check(manifest))
            failures.Add($"[gate] {gateFailure}");
    }

    /// <summary>Re-runs a single full-process entry by spawning the headless binary and compares its aggregate fingerprint.</summary>
    /// <param name="entry">The recorded entry to reproduce.</param>
    /// <param name="outputDir">Directory to write the re-run's manifest into.</param>
    /// <param name="repoRoot">Repository root, used to locate the built headless binary.</param>
    /// <param name="log">Sink for progress lines.</param>
    /// <param name="failures">Mismatch/error messages are appended here (never thrown).</param>
    /// <param name="ct">Cancellation token observed during the re-run.</param>
    private static async Task VerifyFullProcessEntryAsync(
        ExpectedEntry entry, string outputDir, string repoRoot, Action<string> log, List<string> failures, CancellationToken ct)
    {
        var dllPath = Cli.ResolveHeadlessDllPath(repoRoot);
        if (!File.Exists(dllPath))
        {
            failures.Add($"full-process: '{dllPath}' not found -- run `dotnet build -c Release` first.");
            return;
        }

        log($"[verify] re-running {entry.Scenario}: seeds=1..{entry.Seeds} repeats={entry.Repeats} turns={entry.Turns}");
        var manifest = await FullProcessRunner.RunAsync(
            (ulong)entry.Seeds, entry.Repeats, entry.Turns, outputDir, repoRoot, dllPath, log, ct).ConfigureAwait(false);
        LogAndGate(entry.Scenario, entry.Seeds, manifest, log, failures);
        Compare(entry, entry.Seeds, manifest.AggregateFingerprint, failures);
    }

    /// <summary>Appends a failure message to <paramref name="failures"/> if the actual aggregate fingerprint doesn't match the recorded one.</summary>
    /// <param name="entry">The recorded entry being checked.</param>
    /// <param name="seeds">The seed count this comparison is for (may be a checkpoint's, not <paramref name="entry"/>'s own seed count field).</param>
    /// <param name="actualAggregate">The aggregate fingerprint produced by the re-run.</param>
    /// <param name="failures">Mismatch messages are appended here.</param>
    private static void Compare(ExpectedEntry entry, int seeds, string actualAggregate, List<string> failures)
    {
        if (!string.Equals(entry.AggregateFingerprint, actualAggregate, StringComparison.Ordinal))
        {
            failures.Add(
                $"{entry.Scenario} @ seeds={seeds}: expected aggregate {entry.AggregateFingerprint}, got {actualAggregate}");
        }
    }
}
