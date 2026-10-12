using Game.Persistence;
using Game.Simulation;
using System.Diagnostics;
using System.Globalization;

namespace Meridian.Repro;

/// <summary>
/// Drives the "full-process" scenario: spawns the actual built <c>Game.Headless.dll</c> once
/// per (seed, repeat), each in a FRESH temp working directory (the headless binary writes
/// <c>run.db</c> relative to its CWD). This is the expensive, most-honest mode — it proves
/// reproducibility survives process boundaries, cold JIT, and a real SQLite round-trip, not
/// just repeated calls into the same warmed-up in-process engine.
///
/// stdout and the persisted database are each fingerprinted TWICE: a CANONICAL, portable
/// fingerprint (line-ending-normalized stdout; a logical database fingerprint built from
/// re-serialized events + raw snapshot JSON text — see <see cref="FullProcessFingerprint"/>)
/// that feeds the aggregate proof and is meaningful across machines/OSes, and a RAW,
/// same-environment diagnostic hash (exact bytes, no normalization) that is NOT comparable
/// across environments but is useful for spotting exactly where two same-machine runs
/// diverged. Repeat mismatches are tracked independently for stdout vs. database (see
/// <see cref="FullProcessSeedResult.StdoutMismatch"/> / <see cref="FullProcessSeedResult.DatabaseMismatch"/>)
/// so a stdout-only or database-only divergence is never silently folded into one opaque flag.
/// </summary>
public static class FullProcessRunner
{
    /// <summary>Runs a full full-process scenario sweep (all seeds, all repeats), writes its manifest, and returns it. No fingerprints CSV is written (that's reserved for the in-process scenarios).</summary>
    /// <param name="seedCount">Number of seeds to run (seeds 1..seedCount inclusive).</param>
    /// <param name="repeats">Number of repeats per seed.</param>
    /// <param name="turns">Value passed to the headless binary's <c>--turns</c>.</param>
    /// <param name="outputDir">Directory to write <c>full-process-manifest.json</c> into (created if missing).</param>
    /// <param name="repoRoot">Repository root, used for the manifest's git provenance fields.</param>
    /// <param name="headlessDllPath">Path to the already-built <c>Game.Headless.dll</c> (caller is responsible for verifying it exists before calling).</param>
    /// <param name="progress">Optional sparse progress callback, invoked roughly every 10% of seeds completed.</param>
    /// <param name="ct">Cancellation token observed between seeds.</param>
    /// <returns>The completed run's manifest (already written to disk).</returns>
    public static async Task<ReproManifest> RunAsync(
        ulong seedCount,
        int repeats,
        int turns,
        string outputDir,
        string repoRoot,
        string headlessDllPath,
        Action<string>? progress,
        CancellationToken ct)
    {
        var results = new FullProcessSeedResult?[seedCount];
        var pairSeconds = new double[seedCount];
        var sw = Stopwatch.StartNew();
        var completed = 0;
        var progressStep = Math.Max(1, (int)seedCount / 10);

        // Real process spawns, unlike the in-process scenarios -- cap concurrency so we don't
        // thrash the OS with hundreds of simultaneous `dotnet` invocations.
        var degree = Math.Min(Environment.ProcessorCount, 8);
        var options = new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = ct };

        await Parallel.ForEachAsync(SeedRange.Inclusive(1, seedCount), options, async (seed, token) =>
        {
            var seedSw = Stopwatch.StartNew();
            var stdoutCanonical = new string[repeats];
            var stdoutRaw = new string[repeats];
            var dbCanonical = new string[repeats];
            var dbRaw = new string[repeats];
            var rngConsumed = false;
            for (var r = 0; r < repeats; r++)
            {
                var run = await RunOnceAsync(headlessDllPath, seed, turns, token).ConfigureAwait(false);
                stdoutCanonical[r] = run.StdoutCanonical;
                stdoutRaw[r] = run.StdoutRaw;
                dbCanonical[r] = run.DbCanonical;
                dbRaw[r] = run.DbRaw;
                if (r == 0) rngConsumed = run.RngConsumed;
            }
            seedSw.Stop();

            results[seed - 1] = new FullProcessSeedResult(seed, stdoutCanonical, stdoutRaw, dbCanonical, dbRaw, rngConsumed);
            pairSeconds[seed - 1] = seedSw.Elapsed.TotalSeconds;

            var done = Interlocked.Increment(ref completed);
            if (done % progressStep == 0 || done == (int)seedCount)
                progress?.Invoke($"[full-process] {done}/{seedCount} seeds done ({sw.Elapsed.TotalSeconds:F1}s elapsed)");
        }).ConfigureAwait(false);

        sw.Stop();

        var all = results.Select(r => r ?? throw new InvalidOperationException("missing seed result")).ToList();
        var manifest = new ReproManifest(
            GitCommit: ManifestIo.GetGitCommit(repoRoot),
            GitDirty: ManifestIo.GetGitDirty(repoRoot),
            Scenario: Scenarios.FullProcess,
            SeedStart: 1,
            SeedCount: (int)seedCount,
            RunsPerSeed: repeats,
            TurnsPerRun: turns,
            Mismatches: all.Count(r => r.Mismatch),
            DistinctTrajectories: all.Select(r => r.CanonicalCombinedFingerprint).Distinct(StringComparer.Ordinal).Count(),
            RngConsumedSeeds: all.Count(r => r.RngConsumed),
            // Never raw platform-dependent bytes: the aggregate proof is built from each
            // seed's CANONICAL combined (stdout + database) fingerprint.
            AggregateFingerprint: Fingerprint.Aggregate(all.Select(r => (r.Seed, r.CanonicalCombinedFingerprint))),
            ElapsedSeconds: sw.Elapsed.TotalSeconds,
            TimingCheckpoints: Array.Empty<TimingCheckpoint>(),
            DistinctStdoutTrajectories: all.Select(r => r.CanonicalStdoutFingerprint).Distinct(StringComparer.Ordinal).Count(),
            // DIAGNOSTIC, same-environment only: distinct raw run.db file-content hashes across
            // every (seed, repeat) pair -- expected == seed_count on a single machine, NOT
            // seed_count * repeats: same-seed repeats reliably produce byte-identical files (the
            // exact same deterministic writes happen), which is itself a useful same-environment
            // diagnostic (it says raw SQLite output is reproducible here too, not just the
            // canonical logical layer). No universal count is promised beyond that -- this is
            // NOT expected to be comparable across OSes.
            DistinctDatabaseFiles: all.SelectMany(r => r.DatabaseRawHashes).Distinct(StringComparer.Ordinal).Count(),
            // PORTABLE: distinct canonical (logical) database fingerprints across seeds --
            // expected == seed_count (each seed's persisted engine snapshot embeds the seed).
            DistinctDatabaseTrajectories: all.Select(r => r.CanonicalDatabaseFingerprint).Distinct(StringComparer.Ordinal).Count(),
            StdoutMismatches: all.Count(r => r.StdoutMismatch),
            DatabaseMismatches: all.Count(r => r.DatabaseMismatch),
            MeanPairSeconds: pairSeconds.Average());

        Directory.CreateDirectory(outputDir);
        ManifestIo.Write(Path.Combine(outputDir, $"{Scenarios.FullProcess}-manifest.json"), manifest);
        // Fingerprints CSV output is limited to in-process scenarios.

        return manifest;
    }

    /// <summary>Spawns one `dotnet Game.Headless.dll --actors scripted` run in a fresh temp working directory, computes both canonical (portable) and raw (same-environment diagnostic) fingerprints for its stdout and resulting run.db, and reads the persisted rng state — then deletes the temp directory.</summary>
    /// <param name="headlessDllPath">Path to the built Game.Headless.dll.</param>
    /// <param name="seed">Seed to pass as <c>--seed</c>.</param>
    /// <param name="turns">Turn count to pass as <c>--turns</c>.</param>
    /// <param name="ct">Cancellation token observed while the process runs.</param>
    /// <returns>The canonical and raw stdout fingerprints, the canonical and raw database fingerprints, and whether the persisted engine snapshot's rng state moved off its normalized initial value.</returns>
    /// <exception cref="InvalidOperationException">The process could not start, exited non-zero, or its run.db had no completed turns.</exception>
    private static async Task<(string StdoutCanonical, string StdoutRaw, string DbCanonical, string DbRaw, bool RngConsumed)> RunOnceAsync(
        string headlessDllPath, ulong seed, int turns, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "meridian-repro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(headlessDllPath);
            psi.ArgumentList.Add("--turns");
            psi.ArgumentList.Add(turns.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--seed");
            psi.ArgumentList.Add(seed.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--actors");
            psi.ArgumentList.Add("scripted");

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("dotnet failed to start.");

            // Read the RAW underlying pipe stream (StreamReader.BaseStream), not the decoded
            // TextReader -- we need the exact bytes the child process emitted, both to hash
            // as-is (the raw diagnostic) and to decode/normalize ourselves (the canonical,
            // portable fingerprint) rather than trusting any implicit re-encoding.
            var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, ct);
            var stderrDrainTask = process.StandardError.ReadToEndAsync(ct); // drain to avoid pipe-buffer deadlock
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdoutBytes = await stdoutTask.ConfigureAwait(false);
            var stderrText = await stderrDrainTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Game.Headless exited {process.ExitCode} for seed {seed}. stderr:\n{stderrText}");

            var stdoutCanonical = FullProcessFingerprint.CanonicalStdout(stdoutBytes);
            var stdoutRaw = FullProcessFingerprint.RawStdout(stdoutBytes);

            var dbPath = Path.Combine(tempDir, "run.db");
            var dbBytes = await File.ReadAllBytesAsync(dbPath, ct).ConfigureAwait(false);
            var dbRaw = FullProcessFingerprint.RawDatabase(dbBytes);
            var dbCanonical = FullProcessFingerprint.CanonicalDatabase(dbPath);

            bool rngConsumed;
            using (var store = new GameStore(dbPath))
            {
                store.InitSchema();
                var loaded = store.LoadLatestSnapshot()
                    ?? throw new InvalidOperationException($"run.db for seed {seed} has no completed turns.");
                var initialRngState = new DeterministicRng(seed).State;
                rngConsumed = loaded.Engine.RngState != initialRngState;
            }

            return (stdoutCanonical, stdoutRaw, dbCanonical, dbRaw, rngConsumed);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>Reads a stream to completion into a byte array.</summary>
    /// <param name="stream">The stream to read (consumed to end-of-stream).</param>
    /// <param name="ct">Cancellation token observed while reading.</param>
    /// <returns>All bytes read from the stream.</returns>
    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }
}
