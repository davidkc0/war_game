using System.Security.Cryptography;
using WarGame.Sim;
using WarGame.Sim.State;
using Xunit;

namespace WarGame.Sim.Tests;

public class DeterminismTests
{
    // Golden-hash test. Same seed + same commands -> byte-identical state on
    // every platform we support. This test runs the same sim twice in the
    // same process and confirms identical state; the Windows CI matrix run
    // proves *cross-OS* determinism, which is the actual lockstep guarantee.
    //
    // If a future change drifts this hash, that is *intended* only when the
    // schema or sim semantics change. Update the constant below and bump
    // GameState.CurrentVersion in the same commit; do not silently rebase.
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

        // Cross-platform canary. CI on macOS and Windows must produce this
        // exact value. If either runner reports a different value, the sim
        // has a non-deterministic path — find it before merging.
        // v9 schema (Phase 3a: + supply state, road/bridge engineering,
        // Bridge tile type, PendingRoads).
        const string Expected = "2dd1ef52ea367fd3d0a71a1a1a9900463eb70f5faae521087571eb277440e1a3";
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
            Assert.Equal(a.Rng.State, b.Rng.State);
            Assert.Equal(a.Units.Count, b.Units.Count);
        }
    }

    [Fact]
    public void DifferentSeeds_DivergeOverTime()
    {
        // Sanity: the sim actually depends on the seed path. If both seeds
        // produced identical states the determinism test above would still
        // pass, so this guards against an accidentally seedless sim.
        var a = GameState.Initial(1);
        a.Rng = new Math.SimRng(1);
        a = GameSim.StepN(a, 100);

        var b = GameState.Initial(2);
        b.Rng = new Math.SimRng(2);
        b = GameSim.StepN(b, 100);

        Assert.NotEqual(a.Rng.State, b.Rng.State);
    }

    [Fact]
    public void Serializer_RoundTripIsStable()
    {
        // Serialize the same state twice and compare bytes. Catches any
        // hidden non-determinism in the serializer itself (e.g. a stray
        // Dictionary iteration).
        var s = GameState.Initial(7);
        s = GameSim.StepN(s, 100);

        byte[] a = StateSerializer.ToBytes(s);
        byte[] b = StateSerializer.ToBytes(s);
        Assert.Equal(a, b);
    }

    private static string HashState(GameState s)
    {
        byte[] bytes = StateSerializer.ToBytes(s);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
