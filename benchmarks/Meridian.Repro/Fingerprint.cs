using Game.Contracts;
using Game.Persistence;
using Game.Simulation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Meridian.Repro;

/// <summary>
/// The trajectory-fingerprint and aggregate-proof-hash specification documented in the
/// README, so a third party can reimplement it independently.
///
/// Determinism notes:
///  - <see cref="JsonOptions"/> is the ONE shared JsonSerializerOptions instance used for the
///    "FINAL" world-state line of every fingerprint in the process — never construct a second
///    one, or two logically-identical runs could hash differently for spurious reasons.
///  - JSON member order for a given CLR type is stable (declaration order) across processes,
///    platforms, and .NET versions, which is what makes the fingerprint portable.
///  - All hex output is lowercase; all hashed text is UTF-8 encoded explicitly (never relies
///    on Console/OS default encoding).
/// </summary>
public static class Fingerprint
{
    /// <summary>
    /// Default, unindented options — deliberately the same shape as
    /// <c>Game.Persistence.EventSerializer</c>'s own private <c>Options</c> (both are
    /// <c>new JsonSerializerOptions()</c>), so "FINAL\n" + this serialization composes with
    /// the per-event lines using one consistent JSON dialect.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new();

    /// <summary>
    /// Runs one trajectory (fresh scenario + engine, <paramref name="turns"/> turns) and
    /// returns its fingerprint plus whether the engine's rng state moved off its normalized
    /// initial value.
    /// </summary>
    /// <param name="actors">The actor set to register on the fresh engine (must be safe to reuse/share across concurrently-running seeds — see <see cref="Scenarios"/>).</param>
    /// <param name="seed">The engine's game seed.</param>
    /// <param name="turns">Number of turns to advance.</param>
    /// <param name="ct">Cancellation token observed between turns.</param>
    /// <returns>The lowercase-hex trajectory fingerprint, and whether the engine's rng state ever moved off its seed-normalized initial value.</returns>
    public static async Task<(string TrajectoryFingerprint, bool RngConsumed)> RunTrajectoryAsync(
        IReadOnlyList<ActorRegistration> actors, ulong seed, int turns, CancellationToken ct = default)
    {
        var world = Scenario.CreateMeridianStrait();
        var engine = new TurnEngine(world, actors, seed);

        var sb = new StringBuilder();
        for (var i = 0; i < turns; i++)
        {
            var result = await engine.AdvanceTurnAsync(ct).ConfigureAwait(false);
            foreach (var e in result.Events)
            {
                sb.Append(e.GetType().Name).Append('\n');
                sb.Append(EventSerializer.Serialize(e)).Append('\n');
            }
        }

        sb.Append("FINAL\n");
        sb.Append(JsonSerializer.Serialize(WorldStateDto.FromWorld(engine.World), JsonOptions));

        var trajectoryFingerprint = Sha256Hex(sb.ToString());

        var initialRngState = new DeterministicRng(seed).State;
        var rngConsumed = engine.Snapshot().RngState != initialRngState;

        return (trajectoryFingerprint, rngConsumed);
    }

    /// <summary>
    /// Sort mapping by seed ascending; SHA-256 over UTF-8 of the concatenation of
    /// "{seed}:{fingerprint}\n" lines → lowercase hex. <paramref name="perSeed"/> need not
    /// already be sorted.
    /// </summary>
    /// <param name="perSeed">The (seed, trajectory fingerprint) pairs to aggregate; any order.</param>
    /// <returns>The lowercase-hex aggregate proof hash.</returns>
    public static string Aggregate(IEnumerable<(ulong Seed, string TrajectoryFingerprint)> perSeed)
    {
        var sb = new StringBuilder();
        foreach (var (seed, fp) in perSeed.OrderBy(p => p.Seed))
            sb.Append(seed.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(fp).Append('\n');
        return Sha256Hex(sb.ToString());
    }

    /// <summary>Hashes <paramref name="text"/> as UTF-8 with SHA-256.</summary>
    /// <param name="text">The text to hash.</param>
    /// <returns>The lowercase-hex digest.</returns>
    public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    /// <summary>Hashes raw bytes with SHA-256.</summary>
    /// <param name="bytes">The bytes to hash.</param>
    /// <returns>The lowercase-hex digest.</returns>
    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
