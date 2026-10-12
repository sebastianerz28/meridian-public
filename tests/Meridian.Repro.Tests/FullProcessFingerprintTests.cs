using System.Text;
using Game.Persistence;
using Game.Simulation;
using Meridian.Repro;
using Microsoft.Data.Sqlite;

namespace Meridian.Repro.Tests;

/// <summary>
/// Covers the portable-vs-diagnostic fingerprint split for the full-process scenario
/// (<see cref="FullProcessFingerprint"/>, <see cref="FullProcessSeedResult"/>): the canonical
/// stdout fingerprint must ignore line-ending conventions while the raw diagnostic hash must
/// NOT, stdout/database repeat mismatches must be detected independently of each other, and
/// the canonical database fingerprint must cover EVERY persisted snapshot row (not just the
/// latest) while remaining independent of those rows' physical insertion/storage order.
/// </summary>
public sealed class FullProcessFingerprintTests
{
    [Fact]
    public void CanonicalStdout_NormalizesCrlfAndCrToLf()
    {
        var lf = Encoding.UTF8.GetBytes("=== Alderia News History ===\nturn 1\nturn 2\n");
        var crlf = Encoding.UTF8.GetBytes("=== Alderia News History ===\r\nturn 1\r\nturn 2\r\n");
        var cr = Encoding.UTF8.GetBytes("=== Alderia News History ===\rturn 1\rturn 2\r");

        var canonicalLf = FullProcessFingerprint.CanonicalStdout(lf);
        var canonicalCrlf = FullProcessFingerprint.CanonicalStdout(crlf);
        var canonicalCr = FullProcessFingerprint.CanonicalStdout(cr);

        Assert.Equal(canonicalLf, canonicalCrlf);
        Assert.Equal(canonicalLf, canonicalCr);
    }

    [Fact]
    public void RawStdout_StillDiffersAcrossLineEndingConventions_WhileCanonicalAgrees()
    {
        var lf = Encoding.UTF8.GetBytes("same content, different line endings\n");
        var crlf = Encoding.UTF8.GetBytes("same content, different line endings\r\n");

        // The raw diagnostic hash is exactly what its name promises: exact bytes, no
        // normalization -- so a same-machine investigator can still SEE that stdout bytes
        // differed, even though the portable proof (canonical) correctly ignores it.
        Assert.NotEqual(FullProcessFingerprint.RawStdout(lf), FullProcessFingerprint.RawStdout(crlf));
        Assert.Equal(FullProcessFingerprint.CanonicalStdout(lf), FullProcessFingerprint.CanonicalStdout(crlf));
    }

    [Fact]
    public void IdenticalStdout_DivergentDatabase_IsDetectedAsAMismatch()
    {
        var result = new FullProcessSeedResult(
            Seed: 1,
            StdoutCanonicalFingerprints: new[] { "same-stdout", "same-stdout" },
            StdoutRawHashes: new[] { "raw-a", "raw-a" },
            DatabaseCanonicalFingerprints: new[] { "db-fingerprint-1", "db-fingerprint-2" },
            DatabaseRawHashes: new[] { "db-raw-a", "db-raw-b" },
            RngConsumed: false);

        Assert.False(result.StdoutMismatch);
        Assert.True(result.DatabaseMismatch);
        Assert.True(result.Mismatch); // must NOT be silently masked by the stdout side agreeing
    }

    [Fact]
    public void DivergentStdout_IdenticalDatabase_IsDetectedAsAMismatch()
    {
        var result = new FullProcessSeedResult(
            Seed: 1,
            StdoutCanonicalFingerprints: new[] { "stdout-1", "stdout-2" },
            StdoutRawHashes: new[] { "raw-a", "raw-b" },
            DatabaseCanonicalFingerprints: new[] { "same-db", "same-db" },
            DatabaseRawHashes: new[] { "db-raw-a", "db-raw-a" },
            RngConsumed: false);

        Assert.True(result.StdoutMismatch);
        Assert.False(result.DatabaseMismatch);
        Assert.True(result.Mismatch); // must NOT be silently masked by the database side agreeing
    }

    [Fact]
    public async Task CanonicalDatabase_IsStableAcrossCloseAndReopenOfTheSameFile()
    {
        var tempDir = Directory.CreateTempSubdirectory("meridian-repro-tests-");
        try
        {
            var dbPath = Path.Combine(tempDir.FullName, "run.db");

            // 3 turns -> 3 persisted snapshot rows, so this also exercises stability of the
            // FULL snapshot history (not just a single latest row) across a genuine reopen.
            var world = Scenario.CreateMeridianStrait();
            var actors = Scenarios.CreateActors(Scenarios.Narrative);
            var engine = new TurnEngine(world, actors, seed: 7);

            using (var store = new GameStore(dbPath))
            {
                store.InitSchema();
                for (var t = 0; t < 3; t++)
                {
                    var result = await engine.AdvanceTurnAsync();
                    store.SaveTurn(result.Turn, engine.World, result.Events, "{}", engine.Snapshot());
                }
            } // Dispose closes the connection (Pooling=False, same as GameStore's own contract)
              // and releases the file handle -- the next open below is a genuine reopen.

            var first = FullProcessFingerprint.CanonicalDatabase(dbPath);
            var second = FullProcessFingerprint.CanonicalDatabase(dbPath);  // fresh connection #1
            var third = FullProcessFingerprint.CanonicalDatabase(dbPath);   // fresh connection #2

            Assert.Equal(first, second);
            Assert.Equal(first, third);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task CanonicalDatabase_ChangesWhenAnIntermediateSnapshotRowIsMutated()
    {
        // The whole point of covering every snapshot row (not just the latest): Meridian
        // persists a snapshot every turn, and an INTERMEDIATE one could be corrupt/non-resumable
        // while events + the final snapshot both stay correct. A latest-only fingerprint would
        // never see that corruption; this fingerprint must.
        var tempDir = Directory.CreateTempSubdirectory("meridian-repro-tests-");
        try
        {
            var dbPath = Path.Combine(tempDir.FullName, "run.db");

            var world = Scenario.CreateMeridianStrait();
            var actors = Scenarios.CreateActors(Scenarios.Narrative);
            var engine = new TurnEngine(world, actors, seed: 11);

            using (var store = new GameStore(dbPath))
            {
                store.InitSchema();
                for (var t = 0; t < 4; t++)
                {
                    var result = await engine.AdvanceTurnAsync();
                    store.SaveTurn(result.Turn, engine.World, result.Events, "{}", engine.Snapshot());
                }
            }

            var before = FullProcessFingerprint.CanonicalDatabase(dbPath);

            // Mutate turn 2's world_json in place -- turn 2 is neither the first nor the last
            // (latest) of the 4 persisted rows, i.e. genuinely intermediate.
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE snapshots SET world_json = $world WHERE turn = 2;";
                cmd.Parameters.AddWithValue("$world", "{\"corrupted\":true}");
                var rowsAffected = cmd.ExecuteNonQuery();
                Assert.Equal(1, rowsAffected); // sanity: the UPDATE actually hit a row
            }

            var after = FullProcessFingerprint.CanonicalDatabase(dbPath);

            Assert.NotEqual(before, after);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void CanonicalDatabase_IsIndependentOfSnapshotRowPhysicalInsertionOrder()
    {
        var tempDir = Directory.CreateTempSubdirectory("meridian-repro-tests-");
        try
        {
            var rows = new (int Turn, string World, string Agents, string Engine)[]
            {
                (1, "{\"turn\":1}", "{}", "{\"rng\":\"111\",\"seed\":\"7\"}"),
                (2, "{\"turn\":2}", "{}", "{\"rng\":\"222\",\"seed\":\"7\"}"),
                (3, "{\"turn\":3}", "{}", "{\"rng\":\"333\",\"seed\":\"7\"}"),
            };

            var inOrderPath = Path.Combine(tempDir.FullName, "in-order.db");
            var outOfOrderPath = Path.Combine(tempDir.FullName, "out-of-order.db");

            CreateSyntheticDatabase(inOrderPath, new[] { rows[0], rows[1], rows[2] });   // insert turn 1, 2, 3
            CreateSyntheticDatabase(outOfOrderPath, new[] { rows[2], rows[0], rows[1] }); // insert turn 3, 1, 2

            var inOrderFingerprint = FullProcessFingerprint.CanonicalDatabase(inOrderPath);
            var outOfOrderFingerprint = FullProcessFingerprint.CanonicalDatabase(outOfOrderPath);

            Assert.Equal(inOrderFingerprint, outOfOrderFingerprint);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>Builds a run.db with exactly the given snapshot rows, inserted in the given order (no events) -- used to prove the canonical fingerprint doesn't depend on physical insertion order, since the underlying query explicitly orders by turn.</summary>
    private static void CreateSyntheticDatabase(string dbPath, IEnumerable<(int Turn, string World, string Agents, string Engine)> rowsInInsertionOrder)
    {
        using (var store = new GameStore(dbPath))
        {
            store.InitSchema(); // creates the events/snapshots/memories/llm_calls tables.
        }

        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();
        foreach (var row in rowsInInsertionOrder)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO snapshots (turn, world_json, agents_json, engine_json) VALUES ($turn, $world, $agents, $engine);";
            cmd.Parameters.AddWithValue("$turn", row.Turn);
            cmd.Parameters.AddWithValue("$world", row.World);
            cmd.Parameters.AddWithValue("$agents", row.Agents);
            cmd.Parameters.AddWithValue("$engine", row.Engine);
            cmd.ExecuteNonQuery();
        }
    }
}
