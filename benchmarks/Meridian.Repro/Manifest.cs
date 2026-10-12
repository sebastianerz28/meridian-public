using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Repro;

/// <summary>
/// One cumulative-progress sample within a scenario run (a timing checkpoint),
/// e.g. the stochastic-spy-probe scenario's samples at 100/1,000/10,000 seeds.
/// </summary>
/// <param name="Seeds">Number of seeds processed so far (1..this value, inclusive), cumulative from the start of the run.</param>
/// <param name="ElapsedSeconds">Wall-clock seconds elapsed since the run started, up to and including this checkpoint.</param>
/// <param name="Mismatches">Count of seeds (within 1..<paramref name="Seeds"/>) whose repeats disagreed.</param>
/// <param name="DistinctTrajectories">Count of distinct canonical fingerprints among seeds 1..<paramref name="Seeds"/>.</param>
/// <param name="AggregateFingerprint">The aggregate proof hash over seeds 1..<paramref name="Seeds"/> only.</param>
/// <param name="SeedsPerSecond">Cumulative throughput at this checkpoint (<paramref name="Seeds"/> / <paramref name="ElapsedSeconds"/>).</param>
public sealed record TimingCheckpoint(
    int Seeds,
    double ElapsedSeconds,
    int Mismatches,
    int DistinctTrajectories,
    string AggregateFingerprint,
    double SeedsPerSecond);

/// <summary>
/// The ReproManifest schema (proof_schema_version "1"), field names in
/// snake_case on the wire via <see cref="ManifestIo.JsonOptions"/>, PascalCase in C#.
/// The full-process-only fields are omitted from narrative/stochastic manifests via
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/>.
/// </summary>
/// <param name="GitCommit">Full SHA of `git rev-parse HEAD` at run time, or "unknown" if git failed.</param>
/// <param name="GitDirty">True if `git status --porcelain` reported any changes at run time.</param>
/// <param name="Scenario">One of <see cref="Scenarios.Narrative"/>, <see cref="Scenarios.StochasticSpyProbe"/>, <see cref="Scenarios.FullProcess"/>.</param>
/// <param name="SeedStart">First seed in the run (always 1 for this harness).</param>
/// <param name="SeedCount">Number of seeds run (1..SeedCount inclusive).</param>
/// <param name="RunsPerSeed">Repeats performed per seed (the <c>--repeats</c> value).</param>
/// <param name="TurnsPerRun">Turns advanced per run (the <c>--turns</c> value).</param>
/// <param name="Mismatches">Count of seeds whose repeats disagreed (full-process: either stdout OR database side — see <see cref="StdoutMismatches"/>/<see cref="DatabaseMismatches"/> for the independently-tracked breakdown).</param>
/// <param name="DistinctTrajectories">Count of distinct canonical fingerprints across all seeds (full-process: distinct COMBINED stdout+database fingerprints — see <see cref="FullProcessFingerprint.Combine"/>).</param>
/// <param name="RngConsumedSeeds">Count of seeds whose engine rng state moved off its normalized initial value.</param>
/// <param name="AggregateFingerprint">The aggregate proof hash over every seed in the run (full-process: built from each seed's combined CANONICAL stdout+database fingerprint — never raw platform-dependent bytes).</param>
/// <param name="ElapsedSeconds">Total wall-clock seconds for the whole run.</param>
/// <param name="TimingCheckpoints">Cumulative progress samples; empty except for stochastic-spy-probe.</param>
/// <param name="DistinctStdoutTrajectories">full-process only: count of distinct CANONICAL (portable, line-ending-normalized) stdout fingerprints across seeds.</param>
/// <param name="DistinctDatabaseFiles">full-process only, DIAGNOSTIC (same-environment, raw bytes): count of distinct run.db file-content hashes across (seed, repeat) pairs.</param>
/// <param name="DistinctDatabaseTrajectories">full-process only, PORTABLE: count of distinct CANONICAL (logical) database fingerprints across seeds — see <see cref="FullProcessFingerprint.CanonicalDatabase"/>.</param>
/// <param name="StdoutMismatches">full-process only: count of seeds whose CANONICAL stdout fingerprint disagreed across repeats.</param>
/// <param name="DatabaseMismatches">full-process only: count of seeds whose CANONICAL database fingerprint disagreed across repeats.</param>
/// <param name="MeanPairSeconds">full-process only: mean wall-clock seconds to complete one seed's full repeat set.</param>
public sealed record ReproManifest(
    string GitCommit,
    bool GitDirty,
    string Scenario,
    ulong SeedStart,
    int SeedCount,
    int RunsPerSeed,
    int TurnsPerRun,
    int Mismatches,
    int DistinctTrajectories,
    int RngConsumedSeeds,
    string AggregateFingerprint,
    double ElapsedSeconds,
    IReadOnlyList<TimingCheckpoint> TimingCheckpoints,
    int? DistinctStdoutTrajectories = null,
    int? DistinctDatabaseFiles = null,
    int? DistinctDatabaseTrajectories = null,
    int? StdoutMismatches = null,
    int? DatabaseMismatches = null,
    double? MeanPairSeconds = null)
{
    public const string ProofSchemaVersion = "1";
    public const string RulesetVersion = "meridian-core-v1";
    public const string ScenarioVersionConst = "meridian-strait-v1";
    public const string EventSerializerVersion = "1";

    /// <summary>The ACTUAL .NET runtime that produced this manifest, read live from <see cref="RuntimeInformation"/> (never stored, so it can't go stale relative to a value a caller forgot to pass through).</summary>
    public string DotnetRuntime => RuntimeInformation.FrameworkDescription;

    /// <summary>The ACTUAL operating system that produced this manifest, read live from <see cref="RuntimeInformation"/>.</summary>
    public string Os => RuntimeInformation.OSDescription;

    /// <summary>The ACTUAL process architecture that produced this manifest, read live from <see cref="RuntimeInformation"/>.</summary>
    public string Architecture => RuntimeInformation.OSArchitecture.ToString();
}

/// <summary>Manifest and fingerprint-CSV file I/O, plus the git provenance lookups a manifest embeds.</summary>
public static class ManifestIo
{
    /// <summary>
    /// Shared instance (determinism rule: one JsonSerializerOptions for all manifest I/O).
    /// snake_case on the wire; indented for human/CI readability; nulls dropped so
    /// full-process-only fields don't appear on narrative/stochastic manifests.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes <paramref name="manifest"/> to snake_case JSON at <paramref name="path"/>, creating parent directories as needed.</summary>
    /// <param name="path">Destination file path (e.g. <c>&lt;output&gt;/&lt;scenario&gt;-manifest.json</c>); parent directories are created if missing.</param>
    /// <param name="manifest">The manifest to write.</param>
    public static void Write(string path, ReproManifest manifest)
    {
        // proof_schema_version/ruleset_version/scenario_version/event_serializer_version are
        // fixed consts, and dotnet_runtime/os/architecture are computed properties read from
        // RuntimeInformation at write time (never stored, so a manifest always reports the
        // ACTUAL runtime that produced it). Project into an anonymous object in the exact
        // documented field order, then serialize that -- this keeps ReproManifest's
        // constructor lean while still emitting every required field under its exact
        // snake_case name.
        var payload = new
        {
            proof_schema_version = ReproManifest.ProofSchemaVersion,
            git_commit = manifest.GitCommit,
            git_dirty = manifest.GitDirty,
            scenario = manifest.Scenario,
            ruleset_version = ReproManifest.RulesetVersion,
            scenario_version = ReproManifest.ScenarioVersionConst,
            seed_start = manifest.SeedStart,
            seed_count = manifest.SeedCount,
            runs_per_seed = manifest.RunsPerSeed,
            turns_per_run = manifest.TurnsPerRun,
            dotnet_runtime = manifest.DotnetRuntime,
            os = manifest.Os,
            architecture = manifest.Architecture,
            event_serializer_version = ReproManifest.EventSerializerVersion,
            mismatches = manifest.Mismatches,
            distinct_trajectories = manifest.DistinctTrajectories,
            rng_consumed_seeds = manifest.RngConsumedSeeds,
            aggregate_fingerprint = manifest.AggregateFingerprint,
            elapsed_seconds = manifest.ElapsedSeconds,
            timing_checkpoints = manifest.TimingCheckpoints,
            distinct_stdout_trajectories = manifest.DistinctStdoutTrajectories,
            distinct_database_files = manifest.DistinctDatabaseFiles,
            distinct_database_trajectories = manifest.DistinctDatabaseTrajectories,
            stdout_mismatches = manifest.StdoutMismatches,
            database_mismatches = manifest.DatabaseMismatches,
            mean_pair_seconds = manifest.MeanPairSeconds,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    /// <summary>Writes the <c>&lt;scenario&gt;-fingerprints.csv</c> file (in-process scenarios only): one "seed,fingerprint" line per row, sorted by seed ascending, LF line endings.</summary>
    /// <param name="path">Destination file path; parent directories are created if missing.</param>
    /// <param name="rows">The (seed, fingerprint) rows to write; need not already be sorted.</param>
    public static void WriteFingerprintsCsv(string path, IEnumerable<(ulong Seed, string Fingerprint)> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, append: false);
        writer.NewLine = "\n";
        foreach (var (seed, fp) in rows.OrderBy(r => r.Seed))
            writer.WriteLine($"{seed.ToString(CultureInfo.InvariantCulture)},{fp}");
    }

    /// <summary>Runs `git rev-parse HEAD` in <paramref name="repoRoot"/>. Returns "unknown" on any failure (no repo, git not on PATH, detached weirdness, etc.) — a manifest must never throw just because provenance couldn't be determined.</summary>
    /// <param name="repoRoot">Working directory to run git in (the repository root).</param>
    /// <returns>The full commit SHA of HEAD, or "unknown" on failure.</returns>
    public static string GetGitCommit(string repoRoot)
    {
        try
        {
            var output = RunGit(repoRoot, "rev-parse HEAD");
            return output is { Length: > 0 } ? output : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>Runs `git status --porcelain` in <paramref name="repoRoot"/>. Returns false (clean) on any failure, matching <see cref="GetGitCommit"/>'s never-throw contract.</summary>
    /// <param name="repoRoot">Working directory to run git in (the repository root).</param>
    /// <returns>True if the working tree has any uncommitted changes; false if clean or if git could not be run.</returns>
    public static bool GetGitDirty(string repoRoot)
    {
        try
        {
            var output = RunGit(repoRoot, "status --porcelain");
            return output.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Runs a git subcommand synchronously and returns its trimmed stdout.</summary>
    /// <param name="repoRoot">Working directory for the git process.</param>
    /// <param name="arguments">The full argument string (e.g. "rev-parse HEAD").</param>
    /// <returns>Trimmed stdout from the process.</returns>
    /// <exception cref="InvalidOperationException">The process could not start, or exited with a non-zero code.</exception>
    private static string RunGit(string repoRoot, string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git failed to start.");
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {arguments} exited {process.ExitCode}.");
        return stdout.Trim();
    }
}
