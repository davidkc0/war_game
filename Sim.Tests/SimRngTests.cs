using WarGame.Sim.Math;
using Xunit;

namespace WarGame.Sim.Tests;

public class SimRngTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new SimRng(42);
        var b = new SimRng(42);
        for (int i = 0; i < 10000; i++)
        {
            Assert.Equal(a.NextU64(), b.NextU64());
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var a = new SimRng(1);
        var b = new SimRng(2);
        bool anyDifferent = false;
        for (int i = 0; i < 100; i++)
        {
            if (a.NextU64() != b.NextU64())
            {
                anyDifferent = true;
                break;
            }
        }
        Assert.True(anyDifferent);
    }

    [Fact]
    public void SeedZero_DoesNotProduceAllZeros()
    {
        // xorshift has a fixed point at 0, so the constructor must remap it.
        var rng = new SimRng(0);
        ulong sum = 0;
        for (int i = 0; i < 10; i++) sum |= rng.NextU64();
        Assert.NotEqual(0UL, sum);
    }

    [Fact]
    public void NextInt_RespectsRange()
    {
        var rng = new SimRng(42);
        for (int i = 0; i < 1000; i++)
        {
            int v = rng.NextInt(0, 10);
            Assert.InRange(v, 0, 9);
        }
    }
}
