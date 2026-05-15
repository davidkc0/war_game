using WarGame.Sim.Math;
using WarGame.Sim.State;
using Xunit;

namespace WarGame.Sim.Tests;

public class UnitStatsTests
{
    // FP division truncates, so "(per-second / 30) * 30" is not exactly the
    // per-second value. The drift is sub-nanosecond — fine for gameplay,
    // tracked here only to confirm the tuning is *close* to spec.
    private static readonly FP Eps = FP.FromRaw(64); // ~1.5e-8 FP units

    private static void AssertCloseTo(FP expected, FP actual)
    {
        FP diff = FP.Abs(expected - actual);
        Assert.True(diff <= Eps,
            $"expected≈{expected} actual={actual} diff={diff} raw_diff={(actual - expected).Raw}");
    }

    [Fact]
    public void LightDamage_30TicksApprox8PerSecond()
        => AssertCloseTo(FP.FromInt(8), UnitStats.LightDamagePerTick * FP.FromInt(UnitStats.TicksPerSecond));

    [Fact]
    public void HeavyDamage_30TicksApprox20PerSecond()
        => AssertCloseTo(FP.FromInt(20), UnitStats.HeavyDamagePerTick * FP.FromInt(UnitStats.TicksPerSecond));

    [Fact]
    public void HeavyTilesPerTick_30TicksApprox1Point5()
        => AssertCloseTo(FP.FromInt(3) / FP.FromInt(2),
            UnitStats.HeavyTilesPerTick * FP.FromInt(UnitStats.TicksPerSecond));

    [Fact]
    public void LightTilesPerTick_30TicksApprox4PerSecond()
        => AssertCloseTo(FP.FromInt(4), UnitStats.LightTilesPerTick * FP.FromInt(UnitStats.TicksPerSecond));

    [Fact]
    public void CapitalProducesApproxThreeTimesCity()
        => AssertCloseTo(UnitStats.CityEcoPerTick * FP.FromInt(3), UnitStats.CapitalEcoPerTick);

    [Theory]
    [InlineData(false, 1, 1)]
    [InlineData(false, 2, 2)]
    [InlineData(false, 3, 3)]
    [InlineData(true, 1, 3)]
    [InlineData(true, 2, 4)]
    [InlineData(true, 3, 5)]
    public void CityDevelopment_EcoPerSecondLookup(bool capital, byte level, int expected)
    {
        var c = City.Create(0, 0, 0, PlayerId.Player1, capital);
        c.DevelopmentLevel = level;
        Assert.Equal(expected, UnitStats.EcoPerSecond(c));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 8)]
    [InlineData(3, 12)]
    public void CityDevelopment_SupplyCapacityLookup(byte level, int expected)
    {
        var c = City.Create(0, 0, 0, PlayerId.Player1, isCapital: false);
        c.DevelopmentLevel = level;
        Assert.Equal(expected, UnitStats.SupplyCapacity(c));
    }

    [Theory]
    [InlineData(1, 40)]
    [InlineData(2, 90)]
    [InlineData(3, 0)]
    public void CityDevelopment_UpgradeCostLookup(byte level, int expected)
        => Assert.Equal(expected, UnitStats.UpgradeCost(level));

    [Theory]
    [InlineData(UnitType.Light, 60)]
    [InlineData(UnitType.Heavy, 150)]
    public void MaxHpLookup(UnitType t, int expected)
        => Assert.Equal(FP.FromInt(expected), UnitStats.MaxHp(t));

    [Theory]
    [InlineData(UnitType.Light, 1)]
    [InlineData(UnitType.Heavy, 2)]
    public void SupplyCostLookup(UnitType t, int expected)
        => Assert.Equal(expected, UnitStats.SupplyCost(t));

    [Theory]
    [InlineData(UnitType.Light, 12)]
    [InlineData(UnitType.Heavy, 36)]
    public void EcoCostLookup(UnitType t, int expected)
        => Assert.Equal(expected, UnitStats.EcoCost(t));

    [Theory]
    [InlineData(UnitType.Light, 5)]
    [InlineData(UnitType.Heavy, 4)]
    public void VisionRadiusLookup(UnitType t, int expected)
        => Assert.Equal(expected, UnitStats.VisionRadius(t));
}

public class GameStateInitialTests
{
    [Fact]
    public void Initial_VersionIsCurrent()
    {
        var s = GameState.Initial(0);
        Assert.Equal(GameState.CurrentVersion, s.Version);
    }

    [Fact]
    public void Initial_HasThreePlayerSlots()
    {
        var s = GameState.Initial(0);
        Assert.Equal(3, s.Players.Length);
        Assert.Equal(PlayerId.None, s.Players[0].Id);
        Assert.Equal(PlayerId.Player1, s.Players[1].Id);
        Assert.Equal(PlayerId.Player2, s.Players[2].Id);
    }

    [Fact]
    public void Initial_NoUnitsOrCitiesByDefault()
    {
        var s = GameState.Initial(0);
        Assert.Empty(s.Units);
        Assert.Empty(s.Cities);
    }

    [Fact]
    public void Initial_HasMinimalMap()
    {
        var s = GameState.Initial(0);
        Assert.True(s.Map.Width > 0);
        Assert.True(s.Map.Height > 0);
    }
}

public class UnitTests
{
    [Fact]
    public void IsAlive_TrueWhenHpPositive()
    {
        var u = new Unit { Hp = FP.FromInt(10) };
        Assert.True(u.IsAlive);
    }

    [Fact]
    public void IsAlive_FalseAtZeroHp()
    {
        var u = new Unit { Hp = FP.Zero };
        Assert.False(u.IsAlive);
    }

    [Fact]
    public void IsAlive_FalseAtNegativeHp()
    {
        var u = new Unit { Hp = FP.FromInt(-1) };
        Assert.False(u.IsAlive);
    }
}
