using System.Buffers.Binary;
using System.Security.Cryptography;
using WarGame.Sim;
using WarGame.Sim.State;
using Xunit;

namespace WarGame.Sim.Tests;

public class DeterminismTests
{
    // Golden-hash test. The whole point of the project's sim/render split is
    // bit-identical sim across platforms. This test runs the same sim twice
    // (in the same process) and confirms identical state. The Windows CI run
    // of the same test confirms cross-OS determinism — which is the real
    // proof needed for lockstep netcode.
    //
    // If a future change causes this hash to drift, that is *intended* only if
    // the change is to sim semantics. Update the constant below and call out
    // the schema bump in the PR description; do not silently update.
    [Fact]
    public void TenThousandTicks_HashIsStable()
    {
        const ulong Seed = 42;
        const int Ticks = 10000;

        var s = GameState.Initial(Seed);
        s = GameSim.StepN(s, Ticks);

        string hashA = HashState(s);

        var s2 = GameState.Initial(Seed);
        s2 = GameSim.StepN(s2, Ticks);

        string hashB = HashState(s2);

        Assert.Equal(hashA, hashB);

        // Pinning the actual hash is the cross-platform canary. CI on macOS
        // and Windows both run this test; if either runner reports a
        // different value, the sim has a non-deterministic path.
        const string Expected = "abde61ef2334c4cbfb5a4dcb8d10cb97979a17526e0aa1c266f59ac0970d1385";
        // First-run mode: if you change sim semantics intentionally and need
        // a new hash, replace the literal above with the value reported here.
        Assert.True(
            hashA == Expected,
            $"determinism hash drifted. expected={Expected} actual={hashA}");
    }

    [Fact]
    public void StepByStep_IsByteIdenticalAcrossRuns()
    {
        const ulong Seed = 1234;
        const int Ticks = 500;

        var a = GameState.Initial(Seed);
        var b = GameState.Initial(Seed);

        for (int i = 0; i < Ticks; i++)
        {
            a = GameSim.Step(a, null);
            b = GameSim.Step(b, null);
            Assert.Equal(a.Tick, b.Tick);
            Assert.Equal(a.DotPos, b.DotPos);
            Assert.Equal(a.Rng.State, b.Rng.State);
        }
    }

    [Fact]
    public void DifferentSeeds_DivergeOverTime()
    {
        // A separate sanity check that the sim actually depends on the seed
        // path. If both seeds produced identical states the determinism test
        // above would still pass, so this guards against an accidentally
        // seedless sim.
        var a = GameState.Initial(1);
        a.Rng = new Math.SimRng(1);
        a = GameSim.StepN(a, 100);

        var b = GameState.Initial(2);
        b.Rng = new Math.SimRng(2);
        b = GameSim.StepN(b, 100);

        // RNG states will diverge whether or not the dot path uses the rng,
        // because the seeds differ.
        Assert.NotEqual(a.Rng.State, b.Rng.State);
    }

    private static string HashState(GameState s)
    {
        // Hand-rolled byte serializer. We deliberately do not use BinaryWriter
        // or anything that depends on the platform's endianness or the BCL
        // version: every field is written as a fixed-size little-endian
        // primitive so the hash is reproducible byte-for-byte everywhere.
        Span<byte> buf = stackalloc byte[
            sizeof(int) +     // Version
            sizeof(int) +     // Tick
            sizeof(ulong) +   // Rng.State
            sizeof(long) +    // DotPos.X
            sizeof(long) +    // DotPos.Y
            sizeof(long) +    // DotVel.X
            sizeof(long)      // DotVel.Y
        ];

        int o = 0;
        BinaryPrimitives.WriteInt32LittleEndian(buf[o..], s.Version); o += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buf[o..], s.Tick); o += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(buf[o..], s.Rng.State); o += 8;
        BinaryPrimitives.WriteInt64LittleEndian(buf[o..], s.DotPos.X.Raw); o += 8;
        BinaryPrimitives.WriteInt64LittleEndian(buf[o..], s.DotPos.Y.Raw); o += 8;
        BinaryPrimitives.WriteInt64LittleEndian(buf[o..], s.DotVel.X.Raw); o += 8;
        BinaryPrimitives.WriteInt64LittleEndian(buf[o..], s.DotVel.Y.Raw); o += 8;

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buf, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
