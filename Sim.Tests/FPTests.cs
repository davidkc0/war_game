using WarGame.Sim.Math;
using Xunit;

namespace WarGame.Sim.Tests;

public class FPTests
{
    [Fact]
    public void FromInt_ToInt_RoundTrips()
    {
        Assert.Equal(0, FP.FromInt(0).ToInt());
        Assert.Equal(1, FP.FromInt(1).ToInt());
        Assert.Equal(-1, FP.FromInt(-1).ToInt());
        Assert.Equal(12345, FP.FromInt(12345).ToInt());
    }

    [Fact]
    public void Add_Sub_BasicAlgebra()
    {
        FP a = FP.FromInt(7);
        FP b = FP.FromInt(3);
        Assert.Equal(FP.FromInt(10), a + b);
        Assert.Equal(FP.FromInt(4), a - b);
        Assert.Equal(FP.FromInt(-7), -a);
    }

    [Fact]
    public void Mul_LargeOperands_NoOverflow()
    {
        // The whole point of using BigMul: 1000 * 1000 should be 1,000,000,
        // not garbage from a 64-bit overflow.
        FP a = FP.FromInt(1000);
        Assert.Equal(FP.FromInt(1_000_000), a * a);
    }

    [Fact]
    public void Mul_FractionalOperands_AreExact()
    {
        FP half = FP.Half;
        FP quarter = half * half;
        // 1/4 in Q32.32 is exactly OneRaw / 4.
        Assert.Equal(FP.OneRaw / 4, quarter.Raw);
    }

    [Fact]
    public void Mul_NegativeOperands_PreserveSign()
    {
        FP a = FP.FromInt(-7);
        FP b = FP.FromInt(3);
        Assert.Equal(FP.FromInt(-21), a * b);
        Assert.Equal(FP.FromInt(-21), b * a);
        Assert.Equal(FP.FromInt(21), a * FP.FromInt(-3));
    }

    [Fact]
    public void Div_BasicCases()
    {
        Assert.Equal(FP.FromInt(2), FP.FromInt(10) / FP.FromInt(5));
        Assert.Equal(FP.Half, FP.FromInt(1) / FP.FromInt(2));
        Assert.Equal(FP.FromInt(-2), FP.FromInt(10) / FP.FromInt(-5));
    }

    [Fact]
    public void Sqrt_PerfectSquares()
    {
        Assert.Equal(FP.Zero, FP.Sqrt(FP.Zero));
        Assert.Equal(FP.One, FP.Sqrt(FP.One));
        Assert.Equal(FP.FromInt(2), FP.Sqrt(FP.FromInt(4)));
        Assert.Equal(FP.FromInt(10), FP.Sqrt(FP.FromInt(100)));
        Assert.Equal(FP.FromInt(100), FP.Sqrt(FP.FromInt(10000)));
    }

    [Fact]
    public void Sqrt_IsConsistentAcrossCalls()
    {
        // Determinism: same input gives same output, every time.
        FP value = FP.FromInt(123456);
        FP r1 = FP.Sqrt(value);
        FP r2 = FP.Sqrt(value);
        FP r3 = FP.Sqrt(value);
        Assert.Equal(r1.Raw, r2.Raw);
        Assert.Equal(r1.Raw, r3.Raw);
    }

    [Fact]
    public void Comparisons()
    {
        FP a = FP.FromInt(1);
        FP b = FP.FromInt(2);
        FP aCopy = FP.FromInt(1);
        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(a <= aCopy);
        Assert.True(a >= aCopy);
        Assert.False(a == b);
        Assert.True(a != b);
    }
}
