using Game.Persistence;
using Game.Simulation;
using Microsoft.Data.Sqlite;

namespace Meridian.Repro;

/// <summary>
/// The full-process scenario's PORTABLE fingerprint spec: what actually goes into the
/// aggregate proof hash. Raw bytes (stdout as the OS/runtime emitted them, the SQLite file as
/// written to disk) are same-environment diagnostics only -- they can differ for reasons that
/// have nothing to do with simulation determinism (line-ending conventions, SQLite page
/// layout/freelist churn, file-system metadata) and comparing them across Windows and Linux is
/// not a meaningful proof of anything. The canonical fingerprints below strip that noise:
///
///  - Canonical stdout: raw bytes decoded as UTF-8, CRLF and lone-CR line endings normalized
///    to LF, then hashed. This is what the aggregate proof uses.
///  - Canonical (logical) database: the ordered, RE-SERIALIZED event stream (via
///    <c>GameStore.LoadAllEvents()</c> + <c>EventSerializer</c> -- the same pair the in-process
///    trajectory fingerprint uses) plus EVERY persisted snapshot row, ordered by turn, each
///    contributing its raw <c>world_json</c> / <c>agents_json</c> / <c>engine_json</c> column
///    text (already portable, platform-independent JSON emitted by GameStore's own fixed
///    <c>JsonSerializerOptions</c> -- read verbatim rather than re-parsed, since re-parsing
///    would only risk introducing a second point of non-determinism for no benefit). Meridian
///    persists a snapshot every turn, not just the last one, and an intermediate snapshot could
///    be corrupt or non-resumable while the events table and the final snapshot both stay
///    correct -- fingerprinting only the latest row would never see that. This full-history
///    fingerprint is what the aggregate proof uses for the database side, NOT the raw `.db`
///    file's bytes.
/// </summary>
public static class FullProcessFingerprint
{
    /// <summary>The PORTABLE stdout fingerprint used in the aggregate proof: UTF-8 decode, normalize CRLF/CR to LF, SHA-256 the result.</summary>
    /// <param name="rawStdoutBytes">The raw bytes read from the child process's stdout pipe.</param>
    /// <returns>The lowercase-hex canonical stdout fingerprint.</returns>
    public static string CanonicalStdout(byte[] rawStdoutBytes)
    {
        var text = System.Text.Encoding.UTF8.GetString(rawStdoutBytes);
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return Fingerprint.Sha256Hex(normalized);
    }

    /// <summary>The same-environment DIAGNOSTIC stdout hash: the raw bytes, hashed as-is (no normalization). Two runs that differ only by line-ending convention will differ here even though <see cref="CanonicalStdout"/> agrees -- that's the point, it's what lets a verifier SEE the raw-vs-portable distinction rather than just being told about it.</summary>
    /// <param name="rawStdoutBytes">The raw bytes read from the child process's stdout pipe.</param>
    /// <returns>The lowercase-hex raw stdout hash.</returns>
    public static string RawStdout(byte[] rawStdoutBytes) => Fingerprint.Sha256Hex(rawStdoutBytes);

    /// <summary>The same-environment DIAGNOSTIC database hash: the run.db file's raw bytes, hashed as-is.</summary>
    /// <param name="dbBytes">The raw bytes of the run.db file.</param>
    /// <returns>The lowercase-hex raw database file hash.</returns>
    public static string RawDatabase(byte[] dbBytes) => Fingerprint.Sha256Hex(dbBytes);

    /// <summary>
    /// The PORTABLE, logical database fingerprint used in the aggregate proof: every persisted
    /// event (in (turn, ordinal) order) re-serialized through <see cref="EventSerializer"/>,
    /// exactly like the in-process trajectory fingerprint's event lines, followed by EVERY
    /// persisted snapshot row -- not just the latest -- ordered by turn ascending, each
    /// contributing its turn number and raw <c>world_json</c>/<c>agents_json</c>/<c>engine_json</c>
    /// column text under explicit delimiters. Covering every snapshot row (Meridian persists one
    /// every turn) certifies the entire persisted resume history, not just the final state: an
    /// intermediate snapshot could be corrupt or non-resumable while events + the final snapshot
    /// both stay correct, and a latest-only fingerprint would never see that. The query orders
    /// explicitly by turn, so the result is independent of the rows' physical insertion/storage
    /// order. Opens its own short-lived connection (independent of any <see cref="GameStore"/>
    /// the caller may also have open) so it can be safely called standalone, e.g. to verify it's
    /// stable across a close-and-reopen of the same file.
    /// </summary>
    /// <param name="dbPath">Path to the run.db file.</param>
    /// <returns>The lowercase-hex canonical (logical) database fingerprint.</returns>
    /// <exception cref="InvalidOperationException">The database has no snapshot rows.</exception>
    public static string CanonicalDatabase(string dbPath)
    {
        var sb = new System.Text.StringBuilder();

        using (var store = new GameStore(dbPath))
        {
            store.InitSchema(); // CREATE TABLE IF NOT EXISTS -- safe no-op on an already-populated db.
            foreach (var e in store.LoadAllEvents())
            {
                sb.Append(e.GetType().Name).Append('\n');
                sb.Append(EventSerializer.Serialize(e)).Append('\n');
            }
        }

        var snapshotRows = ReadAllSnapshotRowsRawJson(dbPath);
        if (snapshotRows.Count == 0)
            throw new InvalidOperationException($"'{dbPath}' has no snapshot rows to build a canonical database fingerprint from.");

        // Explicit per-row delimiters (a "SNAPSHOT" header, then turn/world/agents/engine each
        // on their own line) so the buffer is unambiguous even though every field it concatenates
        // is itself free-form JSON text.
        foreach (var (turn, worldJson, agentsJson, engineJson) in snapshotRows)
        {
            sb.Append("SNAPSHOT\n");
            sb.Append(turn.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(worldJson).Append('\n');
            sb.Append(agentsJson).Append('\n');
            sb.Append(engineJson).Append('\n');
        }

        return Fingerprint.Sha256Hex(sb.ToString());
    }

    /// <summary>Combines a seed's canonical stdout and canonical database fingerprints into the single per-seed value fed into <see cref="Fingerprint.Aggregate"/> for full-process. Never operates on raw bytes.</summary>
    /// <param name="canonicalStdout">The seed's canonical (portable) stdout fingerprint.</param>
    /// <param name="canonicalDatabase">The seed's canonical (portable) database fingerprint.</param>
    /// <returns>The lowercase-hex combined per-seed fingerprint.</returns>
    public static string Combine(string canonicalStdout, string canonicalDatabase) =>
        Fingerprint.Sha256Hex($"STDOUT\n{canonicalStdout}\nDATABASE\n{canonicalDatabase}\n");

    /// <summary>Reads every snapshot row's raw JSON column text directly via SQL, ordered by turn ascending -- deliberately bypassing <see cref="GameStore.LoadLatestSnapshot"/>, which only exposes the latest row's PARSED objects, not the verbatim column text of every row the canonical fingerprint needs.</summary>
    /// <param name="dbPath">Path to the run.db file.</param>
    /// <returns>Every <c>snapshots</c> row's turn number and raw <c>world_json</c>/<c>agents_json</c>/<c>engine_json</c> column text, ordered by turn ascending. Empty if the table has no rows (the caller decides whether that's an error).</returns>
    private static List<(int Turn, string WorldJson, string AgentsJson, string EngineJson)> ReadAllSnapshotRowsRawJson(string dbPath)
    {
        // Pooling=False mirrors GameStore's own connection string: an unpooled connection
        // releases its file handle immediately on Dispose, which matters on Windows where a
        // still-open handle would block the caller's temp-directory cleanup.
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        // ORDER BY turn ASC (not physical/insertion order) is what makes the resulting
        // fingerprint independent of how the rows happen to be stored on disk.
        cmd.CommandText = "SELECT turn, world_json, agents_json, engine_json FROM snapshots ORDER BY turn ASC;";
        using var reader = cmd.ExecuteReader();

        var rows = new List<(int, string, string, string)>();
        while (reader.Read())
            rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }
}
