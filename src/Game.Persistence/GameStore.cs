using System.Text.Json;
using Game.Contracts;
using Game.Simulation;
using Game.Simulation.State;
using Microsoft.Data.Sqlite;

namespace Game.Persistence;

/// <summary>
/// SQLite-backed persistence for a single run: the append-only event stream, periodic
/// world/engine snapshots, per-country memories, and LLM call telemetry.
///
/// Database lifecycle contract: a fresh run starts from an empty file (the runner is
/// responsible for deleting any existing <c>run.db</c> unless <c>--resume</c> was passed);
/// resume opens the existing file and continues writing turns after the latest snapshot,
/// with no (turn, ordinal) collisions in the events table.
/// </summary>
public sealed class GameStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private static readonly JsonSerializerOptions JsonOptions = new();

    public GameStore(string dbPath)
    {
        // Pooling=False: Microsoft.Data.Sqlite pools native sqlite3 handles per connection
        // string by default, which keeps the OS file handle open past Dispose() for reuse.
        // That's invisible on POSIX but fails outright on Windows, where a still-open handle
        // blocks File.Delete of the temp db in test teardown. Tests own the file's lifetime,
        // so each GameStore gets a dedicated, immediately-released connection.
        _connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _connection.Open();
    }

    public void InitSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                turn INT,
                ordinal INT,
                type TEXT,
                payload TEXT,
                UNIQUE(turn, ordinal)
            );

            CREATE TABLE IF NOT EXISTS snapshots (
                turn INT PRIMARY KEY,
                world_json TEXT,
                agents_json TEXT,
                engine_json TEXT
            );

            CREATE TABLE IF NOT EXISTS memories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                country TEXT,
                turn INT,
                text TEXT
            );

            CREATE TABLE IF NOT EXISTS llm_calls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                turn INT,
                country TEXT,
                kind TEXT,
                valid INT,
                prompt TEXT,
                response TEXT,
                error TEXT,
                ms INT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Persists one turn's snapshot (world + agents + engine) and its event batch in a
    /// single transaction: either everything commits, or nothing does. The events table's
    /// UNIQUE(turn, ordinal) constraint means an accidental double-write of the same turn
    /// throws instead of silently duplicating the replay stream, and the whole transaction
    /// (including the snapshot upsert) rolls back with it.
    /// </summary>
    public void SaveTurn(int turn, WorldState world, IReadOnlyList<DomainEvent> events, string agentsJson, EngineSnapshot engine)
    {
        for (var ordinal = 0; ordinal < events.Count; ordinal++)
        {
            if (events[ordinal].Turn != turn)
            {
                throw new ArgumentException(
                    $"SaveTurn for turn {turn} received {events[ordinal].GetType().Name} " +
                    $"at ordinal {ordinal} stamped for turn {events[ordinal].Turn}.",
                    nameof(events));
            }
        }

        using var transaction = _connection.BeginTransaction();

        var worldJson = JsonSerializer.Serialize(WorldStateDto.FromWorld(world), JsonOptions);
        var engineJson = JsonSerializer.Serialize(EngineSnapshotDto.FromSnapshot(engine), JsonOptions);

        using (var snapshotCmd = _connection.CreateCommand())
        {
            snapshotCmd.Transaction = transaction;
            snapshotCmd.CommandText = """
                INSERT OR REPLACE INTO snapshots (turn, world_json, agents_json, engine_json)
                VALUES ($turn, $world, $agents, $engine);
                """;
            snapshotCmd.Parameters.AddWithValue("$turn", turn);
            snapshotCmd.Parameters.AddWithValue("$world", worldJson);
            snapshotCmd.Parameters.AddWithValue("$agents", agentsJson);
            snapshotCmd.Parameters.AddWithValue("$engine", engineJson);
            snapshotCmd.ExecuteNonQuery();
        }

        for (var ordinal = 0; ordinal < events.Count; ordinal++)
        {
            var e = events[ordinal];
            using var eventCmd = _connection.CreateCommand();
            eventCmd.Transaction = transaction;
            eventCmd.CommandText = """
                INSERT INTO events (turn, ordinal, type, payload)
                VALUES ($turn, $ordinal, $type, $payload);
                """;
            eventCmd.Parameters.AddWithValue("$turn", turn);
            eventCmd.Parameters.AddWithValue("$ordinal", ordinal);
            eventCmd.Parameters.AddWithValue("$type", EventSerializer.TypeName(e));
            eventCmd.Parameters.AddWithValue("$payload", EventSerializer.Serialize(e));
            eventCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<DomainEvent> LoadAllEvents()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT type, payload FROM events ORDER BY turn, ordinal;";
        return ReadEvents(cmd);
    }

    public List<DomainEvent> LoadEventsForTurn(int turn)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT type, payload FROM events WHERE turn = $turn ORDER BY turn, ordinal;";
        cmd.Parameters.AddWithValue("$turn", turn);
        return ReadEvents(cmd);
    }

    private static List<DomainEvent> ReadEvents(SqliteCommand cmd)
    {
        var result = new List<DomainEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var type = reader.GetString(0);
            var payload = reader.GetString(1);
            result.Add(EventSerializer.Deserialize(type, payload));
        }
        return result;
    }

    public (WorldState World, string AgentsJson, EngineSnapshot Engine)? LoadLatestSnapshot()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT world_json, agents_json, engine_json FROM snapshots
            ORDER BY turn DESC LIMIT 1;
            """;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var worldJson = reader.GetString(0);
        var agentsJson = reader.GetString(1);
        var engineJson = reader.GetString(2);

        var worldDto = JsonSerializer.Deserialize<WorldStateDto>(worldJson, JsonOptions)
            ?? throw new InvalidOperationException("LoadLatestSnapshot: world_json deserialized to null.");
        var engineDto = JsonSerializer.Deserialize<EngineSnapshotDto>(engineJson, JsonOptions)
            ?? throw new InvalidOperationException("LoadLatestSnapshot: engine_json deserialized to null.");

        return (worldDto.ToWorld(), agentsJson, engineDto.ToSnapshot());
    }

    public void SaveMemory(CountryId country, int turn, string text)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO memories (country, turn, text) VALUES ($country, $turn, $text);
            """;
        cmd.Parameters.AddWithValue("$country", country.ToString());
        cmd.Parameters.AddWithValue("$turn", turn);
        cmd.Parameters.AddWithValue("$text", text);
        cmd.ExecuteNonQuery();
    }

    public List<(int Turn, string Text)> LoadMemories(CountryId country, int limit)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT turn, text FROM memories
            WHERE country = $country
            ORDER BY turn DESC, id DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$country", country.ToString());
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<(int Turn, string Text)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add((reader.GetInt32(0), reader.GetString(1)));
        return result;
    }

    public void LogLlmCall(int turn, CountryId country, string kind, bool valid, string prompt, string response, string? error, long ms)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO llm_calls (turn, country, kind, valid, prompt, response, error, ms)
            VALUES ($turn, $country, $kind, $valid, $prompt, $response, $error, $ms);
            """;
        cmd.Parameters.AddWithValue("$turn", turn);
        cmd.Parameters.AddWithValue("$country", country.ToString());
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$valid", valid ? 1 : 0);
        cmd.Parameters.AddWithValue("$prompt", prompt);
        cmd.Parameters.AddWithValue("$response", response);
        cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ms", ms);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
