using System.Collections.Generic;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class PathfindingTests
{
    private static (int x, int y) Coord(int idx, int w) => (idx % w, idx / w);

    [Fact]
    public void StraightLineOnPlains()
    {
        var map = new MapState.Builder(10, 1).Build();
        var path = Pathfinding.FindPath(map, 0, 0, 5, 0, isHeavyUnit: false);
        Assert.Equal(5, path.Count);
        Assert.Equal(5, Coord(path[^1], 10).x);
    }

    [Fact]
    public void StartEqualsGoal_ReturnsEmpty()
    {
        var map = new MapState.Builder(10, 10).Build();
        var path = Pathfinding.FindPath(map, 3, 3, 3, 3, false);
        Assert.Empty(path);
    }

    [Fact]
    public void MountainPeak_BlocksPath()
    {
        // Build a wall of peaks at x==5 across the whole map. With no gap,
        // there is no path.
        var b = new MapState.Builder(10, 10);
        for (int y = 0; y < 10; y++) b.Set(5, y, TileType.MountainPeak);
        var map = b.Build();
        var path = Pathfinding.FindPath(map, 0, 5, 9, 5, false);
        Assert.Empty(path);
    }

    [Fact]
    public void BroadWater_IsPassableButAStarPrefersCheaperLandDetour()
    {
        // A thick water wall with a land gap at y=0. Water is passable now,
        // but expensive enough that A* should choose the dry gap.
        var b = new MapState.Builder(10, 10);
        for (int x = 4; x <= 6; x++)
            for (int y = 1; y < 10; y++)
                b.Set(x, y, TileType.Water);
        var map = b.Build();
        var path = Pathfinding.FindPath(map, 0, 2, 9, 2, false);
        Assert.NotEmpty(path);
        Assert.Contains(path, p => Coord(p, 10) == (5, 0));
        Assert.DoesNotContain(path, p =>
        {
            var (x, y) = Coord(p, 10);
            return map.GetTileUnchecked(x, y) == TileType.Water;
        });
    }

    [Fact]
    public void Mountain_BlocksHeavy_AllowsLight()
    {
        var b = new MapState.Builder(5, 1);
        b.Set(2, 0, TileType.Mountain);
        var map = b.Build();

        var heavyPath = Pathfinding.FindPath(map, 0, 0, 4, 0, isHeavyUnit: true);
        var lightPath = Pathfinding.FindPath(map, 0, 0, 4, 0, isHeavyUnit: false);

        Assert.Empty(heavyPath);            // heavy cannot cross
        Assert.NotEmpty(lightPath);         // light can (slowly)
    }

    [Fact]
    public void Road_PreferredOverPlains()
    {
        // Two-row map. Top row: road across. Bottom row: plains.
        // The path from (0,0) to (4,0) should hug the road, not detour.
        var b = new MapState.Builder(5, 2);
        for (int x = 0; x < 5; x++) b.Set(x, 0, TileType.Road);
        var map = b.Build();
        var path = Pathfinding.FindPath(map, 0, 0, 4, 0, false);
        Assert.Equal(4, path.Count);
        foreach (int idx in path)
            Assert.Equal(TileType.Road, map.GetTile(Coord(idx, 5).x, Coord(idx, 5).y));
    }

    [Fact]
    public void Determinism_SameMapSameQuery_SamePath()
    {
        // Build a non-trivial map and assert two A* invocations produce
        // identical sequences. Failure here = something in the inner loop
        // depends on iteration order of an unordered structure.
        var b = new MapState.Builder(20, 20);
        for (int y = 5; y < 15; y++) b.Set(10, y, TileType.Forest);
        b.Set(10, 0, TileType.Mountain);
        b.Set(10, 19, TileType.Mountain);
        var map = b.Build();

        var a = Pathfinding.FindPath(map, 0, 10, 19, 10, false);
        var c = Pathfinding.FindPath(map, 0, 10, 19, 10, false);
        Assert.Equal(a, c);
    }

    [Fact]
    public void OutOfBoundsStartOrGoal_ReturnsEmpty()
    {
        var map = new MapState.Builder(4, 4).Build();
        Assert.Empty(Pathfinding.FindPath(map, -1, 0, 2, 2, false));
        Assert.Empty(Pathfinding.FindPath(map, 0, 0, 4, 4, false));
    }

    [Fact]
    public void PathHasNoStartSquare_HasGoalSquare()
    {
        var map = new MapState.Builder(5, 1).Build();
        var path = Pathfinding.FindPath(map, 0, 0, 4, 0, false);
        // First step is (1,0), last step is (4,0). Start is excluded.
        Assert.Equal((1, 0), Coord(path[0], 5));
        Assert.Equal((4, 0), Coord(path[^1], 5));
        Assert.DoesNotContain(0, path);  // flat index 0 == start tile
    }

    [Fact]
    public void StableTiebreak_PrefersFixedNeighborOrder()
    {
        // On an empty grid, paths from corner to corner have many equal-cost
        // options. The tiebreak rule (priority encoding by tile index)
        // should produce one specific deterministic path.
        var map = new MapState.Builder(5, 5).Build();
        var p1 = Pathfinding.FindPath(map, 0, 0, 4, 4, false);
        var p2 = Pathfinding.FindPath(map, 0, 0, 4, 4, false);
        Assert.Equal(p1, p2);
        // Length is Manhattan distance: 8 moves.
        Assert.Equal(8, p1.Count);
    }
}
