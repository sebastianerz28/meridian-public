namespace Meridian.Repro;

/// <summary>
/// The per-scenario sanity gates, enforced every time a manifest is produced --
/// both during ordinary generation and during --verify's re-runs. A gate failure does not
/// stop the manifest from being written (it's diagnostic evidence), but it does make the
/// process exit non-zero.
/// </summary>
public static class Gates
{
    /// <summary>Checks a completed manifest against its scenario's sanity gates.</summary>
    /// <param name="m">The manifest to check.</param>
    /// <returns>Human-readable failure messages, one per violated gate; empty if all gates passed.</returns>
    public static IReadOnlyList<string> Check(ReproManifest m) => m.Scenario switch
    {
        Scenarios.Narrative => CheckNarrative(m),
        Scenarios.StochasticSpyProbe => CheckStochastic(m),
        Scenarios.FullProcess => CheckFullProcess(m),
        _ => Array.Empty<string>(),
    };

    /// <summary>Narrative gates: zero rng consumption, exactly one distinct trajectory.</summary>
    /// <param name="m">The narrative-scenario manifest to check.</param>
    /// <returns>Failure messages; empty if both gates passed.</returns>
    private static IReadOnlyList<string> CheckNarrative(ReproManifest m)
    {
        var failures = new List<string>();
        if (m.RngConsumedSeeds != 0)
            failures.Add($"narrative: expected rng_consumed_seeds == 0, got {m.RngConsumedSeeds} (the scripted story should never touch the rng).");
        if (m.DistinctTrajectories != 1)
            failures.Add($"narrative: expected distinct_trajectories == 1, got {m.DistinctTrajectories} (the story is seed-independent).");
        return failures;
    }

    /// <summary>Stochastic-spy-probe gates: zero repeat mismatches, every seed consumes rng, every seed produces a distinct trajectory.</summary>
    /// <param name="m">The stochastic-spy-probe manifest to check.</param>
    /// <returns>Failure messages; empty if all gates passed.</returns>
    private static IReadOnlyList<string> CheckStochastic(ReproManifest m)
    {
        var failures = new List<string>();
        if (m.Mismatches != 0)
            failures.Add($"stochastic-spy-probe: expected 0 repeat mismatches, got {m.Mismatches}.");
        if (m.RngConsumedSeeds != m.SeedCount)
            failures.Add($"stochastic-spy-probe: expected every seed to consume rng ({m.SeedCount}), got {m.RngConsumedSeeds}.");
        if (m.DistinctTrajectories != m.SeedCount)
            failures.Add($"stochastic-spy-probe: expected distinct_trajectories == seed_count ({m.SeedCount}), got {m.DistinctTrajectories}.");
        return failures;
    }

    /// <summary>
    /// Full-process gates: zero STDOUT repeat mismatches, zero DATABASE repeat mismatches
    /// (checked independently — a same-seed database divergence must fail even when stdout
    /// is byte-identical across repeats, and vice versa), exactly one distinct stdout
    /// trajectory, and exactly <c>seed_count</c> distinct database trajectories (every
    /// persisted engine snapshot embeds its own seed, so N seeds must yield N distinct
    /// canonical logical databases even though the scripted story's stdout is seed-independent).
    /// </summary>
    /// <param name="m">The full-process manifest to check.</param>
    /// <returns>Failure messages; empty if all gates passed.</returns>
    private static IReadOnlyList<string> CheckFullProcess(ReproManifest m)
    {
        var failures = new List<string>();

        var stdoutMismatches = m.StdoutMismatches ?? m.Mismatches;
        var databaseMismatches = m.DatabaseMismatches ?? m.Mismatches;

        if (stdoutMismatches != 0)
            failures.Add($"full-process: expected 0 stdout repeat mismatches, got {stdoutMismatches}.");
        if (databaseMismatches != 0)
            failures.Add($"full-process: expected 0 database repeat mismatches, got {databaseMismatches}.");
        if (m.DistinctStdoutTrajectories != 1)
            failures.Add($"full-process: expected distinct_stdout_trajectories == 1, got {m.DistinctStdoutTrajectories}.");
        if (m.DistinctDatabaseTrajectories != m.SeedCount)
            failures.Add($"full-process: expected distinct_database_trajectories == seed_count ({m.SeedCount}), got {m.DistinctDatabaseTrajectories}.");
        return failures;
    }
}
