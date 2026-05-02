using System;
using WarGame.Sim.State;
using Xunit;

namespace WarGame.Sim.Tests;

public class MapStateTests
{
    [Fact]
    public void Builder_DefaultsToPlains()
    {
        var map = new MapState.Builder(10, 10).Build();
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                Assert.Equal(TileType.Plains, map.GetTile(x, y));
    }

    [Fact]
    public void Builder_FillsBackgroundType()
    {
        var map = new MapState.Builder(5, 5, TileType.Forest).Build();
        Assert.Equal(TileType.Forest, map.GetTile(0, 0));
        Assert.Equal(TileType.Forest, map.GetTile(4, 4));
    }

    [Fact]
    public void Builder_SetsAndReadsBackIndividualTiles()
    {
        var map = new MapState.Builder(8, 6)
            .Set(3, 4, TileType.Mountain)
            .Set(0, 0, TileType.Capital)
            .Set(7, 5, TileType.City)
            .Build();

        Assert.Equal(TileType.Mountain, map.GetTile(3, 4));
        Assert.Equal(TileType.Capital, map.GetTile(0, 0));
        Assert.Equal(TileType.City, map.GetTile(7, 5));
        Assert.Equal(TileType.Plains, map.GetTile(1, 1));
    }

    [Fact]
    public void Builder_FillRect()
    {
        var map = new MapState.Builder(10, 10)
            .FillRect(2, 2, 4, 5, TileType.Water)
            .Build();
        for (int y = 2; y <= 5; y++)
            for (int x = 2; x <= 4; x++)
                Assert.Equal(TileType.Water, map.GetTile(x, y));
        // Corners outside rect remain plains.
        Assert.Equal(TileType.Plains, map.GetTile(1, 1));
        Assert.Equal(TileType.Plains, map.GetTile(5, 5));
    }

    [Fact]
    public void Builder_BuildIsDefensiveCopy()
    {
        var b = new MapState.Builder(3, 3).Set(1, 1, TileType.Mountain);
        var snapshot = b.Build();
        b.Set(1, 1, TileType.Water);
        // The snapshot should not see the post-Build mutation.
        Assert.Equal(TileType.Mountain, snapshot.GetTile(1, 1));
    }

    [Fact]
    public void GetTile_OutOfBoundsThrows()
    {
        var map = new MapState.Builder(4, 4).Build();
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetTile(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetTile(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetTile(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetTile(0, 4));
    }

    [Fact]
    public void InBounds_HandlesNegativesWithoutAllocation()
    {
        var map = new MapState.Builder(4, 4).Build();
        Assert.True(map.InBounds(0, 0));
        Assert.True(map.InBounds(3, 3));
        Assert.False(map.InBounds(-1, 0));
        Assert.False(map.InBounds(0, 4));
        Assert.False(map.InBounds(int.MinValue, 0));
    }

    [Fact]
    public void Constructor_RejectsMismatchedBuffer()
    {
        Assert.Throws<ArgumentException>(() =>
            new MapState(4, 4, new byte[15])); // should be 16
    }

    [Fact]
    public void Constructor_RejectsZeroOrNegativeDimensions()
    {
        Assert.Throws<ArgumentException>(() => new MapState(0, 4, new byte[0]));
        Assert.Throws<ArgumentException>(() => new MapState(4, 0, new byte[0]));
        Assert.Throws<ArgumentException>(() => new MapState(-1, 4, new byte[0]));
    }
}

public class TileTypePassabilityTests
{
    [Theory]
    [InlineData(TileType.Plains, false, true)]
    [InlineData(TileType.Plains, true, true)]
    [InlineData(TileType.Forest, false, true)]
    [InlineData(TileType.Forest, true, true)]
    [InlineData(TileType.Mountain, false, true)]   // light passes
    [InlineData(TileType.Mountain, true, false)]   // heavy blocked
    [InlineData(TileType.Water, false, false)]
    [InlineData(TileType.Water, true, false)]
    [InlineData(TileType.Road, false, true)]
    [InlineData(TileType.Bridge, false, true)]
    [InlineData(TileType.City, false, true)]
    [InlineData(TileType.Capital, true, true)]
    public void Passability_MatchesPlanSpec(TileType t, bool heavy, bool expected)
    {
        Assert.Equal(expected, t.IsPassable(heavy));
    }

    [Fact]
    public void RoadGivesSpeedBonus()
    {
        long road = TileType.Road.SpeedFactorRaw(false);
        long bridge = TileType.Bridge.SpeedFactorRaw(false);
        long plains = TileType.Plains.SpeedFactorRaw(false);
        Assert.True(road > plains, "road should be faster than plains");
        Assert.Equal(road, bridge);
    }

    [Fact]
    public void HeavyInForestIsSlowerThanLight()
    {
        long heavy = TileType.Forest.SpeedFactorRaw(true);
        long light = TileType.Forest.SpeedFactorRaw(false);
        Assert.True(heavy < light, "heavy should be slower than light in forest");
    }

    [Fact]
    public void HeavyOnMountainIsZero()
    {
        Assert.Equal(0, TileType.Mountain.SpeedFactorRaw(true));
    }
}
