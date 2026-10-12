using System.Text;
using Game.Contracts;

namespace Game.Simulation.Observation;

/// <summary>
/// Pure, deterministic pseudo-noise for observation error. No unseeded randomness:
/// the same (seed, observer, property, turn, half-width) always yields the same offset.
/// </summary>
public static class ObservationNoise
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>
    /// Deterministic offset in [-0.6 * halfWidth, +0.6 * halfWidth], derived by
    /// FNV-1a hashing "{gameSeed}|{observer}|{property}|{observedTurn}" and mapping
    /// the hash into [-1, 1].
    /// </summary>
    public static double Offset(ulong gameSeed, CountryId observer, string property, int observedTurn, double halfWidth)
    {
        var input = $"{gameSeed}|{observer}|{property}|{observedTurn}";
        var hash = Fnv1a(input);

        // Map the full ulong range onto [-1, 1].
        var unit = hash / (double)ulong.MaxValue; // [0, 1]
        var signed = unit * 2.0 - 1.0;             // [-1, 1]

        return signed * 0.6 * halfWidth;
    }

    private static ulong Fnv1a(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = FnvOffsetBasis;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }
}
