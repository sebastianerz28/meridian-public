namespace Meridian.Repro;

/// <summary>One in-process seed's outcome: its per-repeat trajectory fingerprints and whether the engine's rng state moved.</summary>
/// <param name="Seed">The seed this result belongs to.</param>
/// <param name="RepeatFingerprints">One trajectory fingerprint per repeat, in repeat order.</param>
/// <param name="RngConsumed">Whether repeat 0's engine rng state moved off its normalized initial value.</param>
public sealed record SeedResult(ulong Seed, IReadOnlyList<string> RepeatFingerprints, bool RngConsumed)
{
    /// <summary>The trajectory fingerprint used for aggregation/CSV — repeat 0's, since a mismatch (checked separately) means repeats disagreed and the run is already flagged as non-reproducible.</summary>
    public string CanonicalFingerprint => RepeatFingerprints[0];

    /// <summary>True if any two repeats for this seed produced different fingerprints.</summary>
    public bool Mismatch => RepeatFingerprints.Distinct(StringComparer.Ordinal).Count() > 1;
}

/// <summary>
/// One full-process seed's outcome across repeats: PORTABLE canonical fingerprints (what the
/// aggregate proof is built from -- see <see cref="FullProcessFingerprint"/>) plus
/// same-environment raw diagnostics, for both stdout and the persisted database, tracked and
/// gated INDEPENDENTLY (a stdout-only or database-only divergence must each be individually
/// detectable, not folded into one opaque flag).
/// </summary>
/// <param name="Seed">The seed this result belongs to.</param>
/// <param name="StdoutCanonicalFingerprints">One portable (line-ending-normalized) stdout fingerprint per repeat, in repeat order.</param>
/// <param name="StdoutRawHashes">One same-environment raw-stdout-bytes hash per repeat, in repeat order (diagnostic only).</param>
/// <param name="DatabaseCanonicalFingerprints">One portable (logical: re-serialized events + raw snapshot JSON text) database fingerprint per repeat, in repeat order.</param>
/// <param name="DatabaseRawHashes">One same-environment raw run.db-file-bytes hash per repeat, in repeat order (diagnostic only).</param>
/// <param name="RngConsumed">Whether repeat 0's persisted engine snapshot shows rng state moved off its normalized initial value.</param>
public sealed record FullProcessSeedResult(
    ulong Seed,
    IReadOnlyList<string> StdoutCanonicalFingerprints,
    IReadOnlyList<string> StdoutRawHashes,
    IReadOnlyList<string> DatabaseCanonicalFingerprints,
    IReadOnlyList<string> DatabaseRawHashes,
    bool RngConsumed)
{
    /// <summary>The canonical stdout fingerprint used for aggregation — repeat 0's (see <see cref="SeedResult.CanonicalFingerprint"/> for the same rationale).</summary>
    public string CanonicalStdoutFingerprint => StdoutCanonicalFingerprints[0];

    /// <summary>The canonical database fingerprint used for aggregation — repeat 0's.</summary>
    public string CanonicalDatabaseFingerprint => DatabaseCanonicalFingerprints[0];

    /// <summary>The single per-seed value fed into <see cref="Fingerprint.Aggregate"/> — never raw bytes.</summary>
    public string CanonicalCombinedFingerprint => FullProcessFingerprint.Combine(CanonicalStdoutFingerprint, CanonicalDatabaseFingerprint);

    /// <summary>True if any two repeats for this seed produced different CANONICAL stdout fingerprints.</summary>
    public bool StdoutMismatch => StdoutCanonicalFingerprints.Distinct(StringComparer.Ordinal).Count() > 1;

    /// <summary>True if any two repeats for this seed produced different CANONICAL database fingerprints.</summary>
    public bool DatabaseMismatch => DatabaseCanonicalFingerprints.Distinct(StringComparer.Ordinal).Count() > 1;

    /// <summary>True if either the stdout or the database side mismatched across repeats.</summary>
    public bool Mismatch => StdoutMismatch || DatabaseMismatch;
}

/// <summary>Small helper for enumerating an inclusive ulong seed range.</summary>
public static class SeedRange
{
    /// <summary>Enumerates every seed from <paramref name="start"/> to <paramref name="endInclusive"/>, both ends included.</summary>
    /// <param name="start">First seed (inclusive).</param>
    /// <param name="endInclusive">Last seed (inclusive).</param>
    /// <returns>The seeds in ascending order.</returns>
    public static IEnumerable<ulong> Inclusive(ulong start, ulong endInclusive)
    {
        for (var s = start; s <= endInclusive; s++) yield return s;
    }
}
