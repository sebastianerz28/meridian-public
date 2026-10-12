namespace Game.Simulation;

public sealed class DeterministicRng(ulong seed)
{
    private ulong _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public ulong State => _state;

    public double NextDouble()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        var x = _state * 0x2545F4914F6CDD1DUL;
        return (x >> 11) * (1.0 / (1UL << 53));
    }
}
