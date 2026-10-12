using System.Diagnostics;

namespace Meridian.Repro;

/// <summary>
/// Drives the "narrative" and "stochastic-spy-probe" scenarios: fresh
/// <c>Scenario.CreateMeridianStrait()</c> + <c>TurnEngine</c> per (seed, repeat), entirely
/// in-process. Seeds within a checkpoint range run concurrently via
/// <c>Parallel.ForEachAsync</c> (per-seed state is fully isolated — its own
/// World/TurnEngine/rng — and results are written into a seed-indexed array, so the
/// aggregate computed afterward from the sorted array is order-independent regardless of
/// which thread finished which seed first).
/// </summary>
public static class InProcessRunner
{
    /// <summary>Runs a full in-process scenario sweep (all seeds, all repeats), writes its manifest and fingerprints CSV, and returns the manifest.</summary>
    /// <param name="scenario"><see cref="Scenarios.Narrative"/> or <see cref="Scenarios.StochasticSpyProbe"/>.</param>
    /// <param name="seedCount">Number of seeds to run (seeds 1..seedCount inclusive).</param>
    /// <param name="repeats">Number of repeats per seed.</param>
    /// <param name="turns">Number of turns to advance per run.</param>
    /// <param name="outputDir">Directory to write <c>&lt;scenario&gt;-manifest.json</c> and <c>&lt;scenario&gt;-fingerprints.csv</c> into (created if missing).</param>
    /// <param name="repoRoot">Repository root, used for the manifest's git provenance fields.</param>
    /// <param name="checkpointSeeds">Seed counts (e.g. 100/1,000/10,000) at which to record a cumulative <see cref="TimingCheckpoint"/>; ignored for scenarios other than stochastic-spy-probe. Values above <paramref name="seedCount"/> are skipped; <paramref name="seedCount"/> itself is always included.</param>
    /// <param name="progress">Optional sparse progress callback, invoked roughly every 10% of seeds completed.</param>
    /// <param name="ct">Cancellation token observed between seeds.</param>
    /// <returns>The completed run's manifest (already written to disk).</returns>
    public static async Task<ReproManifest> RunAsync(
        string scenario,
        ulong seedCount,
        int repeats,
        int turns,
        string outputDir,
        string repoRoot,
        IReadOnlyList<int> checkpointSeeds,
        Action<string>? progress,
        CancellationToken ct)
    {
        var actors = Scenarios.CreateActors(scenario);
        var results = new SeedResult?[seedCount];

        // Checkpoint boundaries: every requested checkpoint <= seedCount, plus the final
        // seedCount itself (so the top-level manifest's own numbers are always the last
        // checkpoint computed, never a separate pass).
        var boundaries = checkpointSeeds.Where(c => c > 0 && c <= (int)seedCount).Distinct().OrderBy(c => c).ToList();
        if (boundaries.Count == 0 || boundaries[^1] != (int)seedCount) boundaries.Add((int)seedCount);

        var sw = Stopwatch.StartNew();
        var checkpoints = new List<TimingCheckpoint>();
        var completed = 0;
        var progressStep = Math.Max(1, (int)seedCount / 10);

        var nextSeed = 1UL;
        foreach (var boundary in boundaries)
        {
            var rangeEnd = (ulong)boundary;
            if (rangeEnd < nextSeed) continue;

            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };
            await Parallel.ForEachAsync(SeedRange.Inclusive(nextSeed, rangeEnd), options, async (seed, token) =>
            {
                var fingerprints = new string[repeats];
                var rngConsumed = false;
                for (var r = 0; r < repeats; r++)
                {
                    var (fp, consumed) = await Fingerprint.RunTrajectoryAsync(actors, seed, turns, token).ConfigureAwait(false);
                    fingerprints[r] = fp;
                    if (r == 0) rngConsumed = consumed;
                }
                results[seed - 1] = new SeedResult(seed, fingerprints, rngConsumed);

                var done = Interlocked.Increment(ref completed);
                if (done % progressStep == 0 || done == (int)seedCount)
                    progress?.Invoke($"[{scenario}] {done}/{seedCount} seeds done ({sw.Elapsed.TotalSeconds:F1}s elapsed)");
            }).ConfigureAwait(false);

            nextSeed = rangeEnd + 1;

            var slice = results.Take(boundary).Select(r => r ?? throw new InvalidOperationException("missing seed result")).ToList();
            var elapsed = sw.Elapsed.TotalSeconds;
            checkpoints.Add(new TimingCheckpoint(
                Seeds: boundary,
                ElapsedSeconds: elapsed,
                Mismatches: slice.Count(r => r.Mismatch),
                DistinctTrajectories: slice.Select(r => r.CanonicalFingerprint).Distinct(StringComparer.Ordinal).Count(),
                AggregateFingerprint: Fingerprint.Aggregate(slice.Select(r => (r.Seed, r.CanonicalFingerprint))),
                SeedsPerSecond: boundary / Math.Max(elapsed, 1e-9)));
        }

        sw.Stop();

        var all = results.Select(r => r ?? throw new InvalidOperationException("missing seed result")).ToList();
        var manifest = new ReproManifest(
            GitCommit: ManifestIo.GetGitCommit(repoRoot),
            GitDirty: ManifestIo.GetGitDirty(repoRoot),
            Scenario: scenario,
            SeedStart: 1,
            SeedCount: (int)seedCount,
            RunsPerSeed: repeats,
            TurnsPerRun: turns,
            Mismatches: all.Count(r => r.Mismatch),
            DistinctTrajectories: all.Select(r => r.CanonicalFingerprint).Distinct(StringComparer.Ordinal).Count(),
            RngConsumedSeeds: all.Count(r => r.RngConsumed),
            AggregateFingerprint: Fingerprint.Aggregate(all.Select(r => (r.Seed, r.CanonicalFingerprint))),
            ElapsedSeconds: sw.Elapsed.TotalSeconds,
            // Only the stochastic scenario's checkpoints are meaningful (narrative has one
            // trajectory regardless of sample size); keep narrative's manifest checkpoint-free.
            TimingCheckpoints: scenario == Scenarios.StochasticSpyProbe ? checkpoints : Array.Empty<TimingCheckpoint>());

        Directory.CreateDirectory(outputDir);
        ManifestIo.Write(Path.Combine(outputDir, $"{scenario}-manifest.json"), manifest);
        ManifestIo.WriteFingerprintsCsv(
            Path.Combine(outputDir, $"{scenario}-fingerprints.csv"),
            all.Select(r => (r.Seed, r.CanonicalFingerprint)));

        return manifest;
    }
}
