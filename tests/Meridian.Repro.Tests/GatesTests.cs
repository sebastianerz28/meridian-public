using Meridian.Repro;

namespace Meridian.Repro.Tests;

/// <summary>
/// Cheap negative checks for <see cref="Gates.Check"/> against synthetic manifests -- the
/// thing --verify relies on to catch real repeat mismatches that an aggregate-fingerprint
/// comparison alone cannot (an aggregate is built from each seed's repeat-0 fingerprint only,
/// so a live-nondeterministic machine could still produce a matching aggregate by luck on
/// repeat 0 while `mismatches` is honestly nonzero). These tests exist specifically to prove
/// Gates.Check actually fails loudly on that nonzero-mismatches case, not just on eyeballing.
/// </summary>
public sealed class GatesTests
{
    private static ReproManifest Manifest(
        string scenario, int seedCount, int mismatches, int distinctTrajectories, int rngConsumedSeeds,
        int? distinctStdout = null, int? stdoutMismatches = null, int? databaseMismatches = null,
        int? distinctDatabaseTrajectories = null) => new(
        GitCommit: "test",
        GitDirty: false,
        Scenario: scenario,
        SeedStart: 1,
        SeedCount: seedCount,
        RunsPerSeed: 2,
        TurnsPerRun: 20,
        Mismatches: mismatches,
        DistinctTrajectories: distinctTrajectories,
        RngConsumedSeeds: rngConsumedSeeds,
        AggregateFingerprint: "deadbeef",
        ElapsedSeconds: 0.0,
        TimingCheckpoints: Array.Empty<TimingCheckpoint>(),
        DistinctStdoutTrajectories: distinctStdout,
        DistinctDatabaseFiles: distinctStdout is null ? null : seedCount,
        // Defaults to seedCount (the passing case) unless a test explicitly overrides it to
        // exercise the distinct_database_trajectories == seed_count gate.
        DistinctDatabaseTrajectories: distinctDatabaseTrajectories ?? (distinctStdout is null ? null : seedCount),
        StdoutMismatches: stdoutMismatches,
        DatabaseMismatches: databaseMismatches,
        MeanPairSeconds: distinctStdout is null ? null : 0.5);

    [Fact]
    public void Stochastic_WithRealRepeatMismatches_Fails()
    {
        // This is the exact scenario a "VERIFY OK via aggregate-only comparison" bug would
        // miss: a run with genuine non-determinism (mismatches=1) whose repeat-0 fingerprints
        // still happen to be internally consistent enough to pass distinct/rng checks.
        var manifest = Manifest(Scenarios.StochasticSpyProbe, seedCount: 10, mismatches: 1, distinctTrajectories: 10, rngConsumedSeeds: 10);

        var failures = Gates.Check(manifest);

        Assert.NotEmpty(failures);
        Assert.Contains(failures, f => f.Contains("0 repeat mismatches", StringComparison.Ordinal));
    }

    [Fact]
    public void Stochastic_Clean_Passes()
    {
        var manifest = Manifest(Scenarios.StochasticSpyProbe, seedCount: 10, mismatches: 0, distinctTrajectories: 10, rngConsumedSeeds: 10);

        Assert.Empty(Gates.Check(manifest));
    }

    [Fact]
    public void Narrative_WithRngConsumption_Fails()
    {
        var manifest = Manifest(Scenarios.Narrative, seedCount: 40, mismatches: 0, distinctTrajectories: 1, rngConsumedSeeds: 3);

        var failures = Gates.Check(manifest);

        Assert.NotEmpty(failures);
        Assert.Contains(failures, f => f.Contains("rng_consumed_seeds == 0", StringComparison.Ordinal));
    }

    [Fact]
    public void FullProcess_Clean_Passes()
    {
        var manifest = Manifest(
            Scenarios.FullProcess, seedCount: 40, mismatches: 0, distinctTrajectories: 40, rngConsumedSeeds: 0,
            distinctStdout: 1, stdoutMismatches: 0, databaseMismatches: 0);

        Assert.Empty(Gates.Check(manifest));
    }

    [Fact]
    public void FullProcess_DatabaseMismatchOnly_Fails()
    {
        // Same shape as the "identical stdout, divergent database" scenario at the
        // FullProcessSeedResult level (see FullProcessFingerprintTests) but exercised through
        // the actual gate that --verify and ordinary generation both call: a database-only
        // divergence must fail even though stdout_mismatches is 0.
        var manifest = Manifest(
            Scenarios.FullProcess, seedCount: 40, mismatches: 1, distinctTrajectories: 1, rngConsumedSeeds: 0,
            distinctStdout: 1, stdoutMismatches: 0, databaseMismatches: 1);

        var failures = Gates.Check(manifest);

        Assert.NotEmpty(failures);
        Assert.Contains(failures, f => f.Contains("0 database repeat mismatches", StringComparison.Ordinal));
        Assert.DoesNotContain(failures, f => f.Contains("0 stdout repeat mismatches", StringComparison.Ordinal));
    }

    [Fact]
    public void FullProcess_StdoutMismatchOnly_Fails()
    {
        var manifest = Manifest(
            Scenarios.FullProcess, seedCount: 40, mismatches: 1, distinctTrajectories: 1, rngConsumedSeeds: 0,
            distinctStdout: 2, stdoutMismatches: 1, databaseMismatches: 0);

        var failures = Gates.Check(manifest);

        Assert.NotEmpty(failures);
        Assert.Contains(failures, f => f.Contains("0 stdout repeat mismatches", StringComparison.Ordinal));
        Assert.DoesNotContain(failures, f => f.Contains("0 database repeat mismatches", StringComparison.Ordinal));
    }

    [Fact]
    public void FullProcess_FewerDistinctDatabaseTrajectoriesThanSeeds_Fails()
    {
        // Every persisted engine snapshot embeds its own seed, so N seeds must yield N distinct
        // canonical logical databases -- even when stdout is identical across all of them (the
        // scripted story is seed-independent). Two seeds collapsing to one canonical database
        // fingerprint would mean the seed genuinely isn't reaching the persisted state, which
        // this gate must catch even though stdout/mismatch counts all look clean.
        var manifest = Manifest(
            Scenarios.FullProcess, seedCount: 40, mismatches: 0, distinctTrajectories: 39, rngConsumedSeeds: 0,
            distinctStdout: 1, stdoutMismatches: 0, databaseMismatches: 0, distinctDatabaseTrajectories: 39);

        var failures = Gates.Check(manifest);

        Assert.NotEmpty(failures);
        Assert.Contains(failures, f => f.Contains("distinct_database_trajectories == seed_count", StringComparison.Ordinal));
    }
}
