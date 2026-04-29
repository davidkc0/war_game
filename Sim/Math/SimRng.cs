using System.Runtime.CompilerServices;

namespace WarGame.Sim.Math;

// Seeded deterministic RNG. xorshift64*. State fits in a single ulong, so it
// serializes trivially as part of GameState. Bit-identical across all .NET 8
// platforms because every operation is on uint/ulong.
//
// Why not System.Random: documented to be implementation-defined and has
// changed between .NET versions. Anything in /Sim with a non-deterministic
// dependency is a desync waiting to happen.
public struct SimRng
{
    public ulong State;

    public SimRng(ulong seed)
    {
        // 0 is a fixed point for xorshift; map it to a non-zero sentinel.
        State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong NextU64()
    {
        ulong x = State;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        State = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextU32() => (uint)(NextU64() >> 32);

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        uint range = (uint)(maxExclusive - minInclusive);
        // Plain modulo has slight bias; for v1 it is acceptable. We can swap
        // for rejection sampling if a test ever needs uniform-perfect output.
        return minInclusive + (int)(NextU32() % range);
    }

    public bool NextBool() => (NextU64() & 1UL) != 0;

    // Returns FP in [0, 1) by taking the top 32 bits of the random output as
    // the fractional part of a Q32.32 fixed-point.
    public FP NextFP()
    {
        ulong r = NextU64();
        return FP.FromRaw((long)((r >> 32) & 0xFFFFFFFFUL));
    }
}
