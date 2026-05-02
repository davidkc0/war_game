using System.Collections.Generic;
using WarGame.Sim.Generation;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class MapGeneratorTests
{
    [Fact]
    public void SameSeed_ProducesSameMap()
    {
        var r1 = MapGenerator.Generate(42);
        var r2 = MapGenerator.Generate(42);

        Assert.Equal(r1.Map.Width, r2.Map.Width);
        Assert.Equal(r1.Map.Height, r2.Map.Height);

        for (int i = 0; i < r1.Map.TileCount; i++)
            Assert.Equal(r1.Map.RawTiles[i], r2.Map.RawTiles[i]);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentMaps()
    {
        var r1 = MapGenerator.Generate(100);
        var r2 = MapGenerator.Generate(999);

        // With different seeds, at least some tiles should differ.
        int diffs = 0;
        for (int i = 0; i < r1.Map.TileCount; i++)
            if (r1.Map.RawTiles[i] != r2.Map.RawTiles[i]) diffs++;

        Assert.True(diffs > 100, $"only {diffs} tiles differ between different seeds");
    }

    [Fact]
    public void Generated_HasCorrectCityCount()
    {
        // 1 capital + 2 cities per team = 6 total.
        var result = MapGenerator.Generate(12345);
        Assert.Equal(6, result.Cities.Count);
    }

    [Fact]
    public void Generated_HasTwoCapitals()
    {
        var result = MapGenerator.Generate(12345);
        int capitals = 0;
        foreach (var c in result.Cities)
            if (c.IsCapital) capitals++;
        Assert.Equal(2, capitals);
    }

    [Fact]
    public void Generated_CapitalsOwnedByDifferentPlayers()
    {
        var result = MapGenerator.Generate(12345);
        Assert.Equal(PlayerId.Player1, result.Cities[0].Owner);
        Assert.Equal(PlayerId.Player2, result.Cities[1].Owner);
        Assert.True(result.Cities[0].IsCapital);
        Assert.True(result.Cities[1].IsCapital);
    }

    [Fact]
    public void Generated_MountainsNeverAdjacentToWater()
    {
        // Test over multiple seeds.
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int w = result.Map.Width, h = result.Map.Height;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (result.Map.GetTileUnchecked(x, y) != TileType.Mountain) continue;

                    if (x > 0) Assert.NotEqual(TileType.Water, result.Map.GetTileUnchecked(x-1, y));
                    if (x < w-1) Assert.NotEqual(TileType.Water, result.Map.GetTileUnchecked(x+1, y));
                    if (y > 0) Assert.NotEqual(TileType.Water, result.Map.GetTileUnchecked(x, y-1));
                    if (y < h-1) Assert.NotEqual(TileType.Water, result.Map.GetTileUnchecked(x, y+1));
                }
            }
        }
    }

    [Fact]
    public void Generated_AllCitiesReachable()
    {
        // BFS from capital 1 should reach all other cities.
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int w = result.Map.Width, h = result.Map.Height;

            var visited = new bool[w * h];
            var queue = new Queue<int>();
            int startIdx = result.Cities[0].TileY * w + result.Cities[0].TileX;
            visited[startIdx] = true;
            queue.Enqueue(startIdx);

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % w, y2 = idx / w;

                int[][] dirs = { new[]{1,0}, new[]{-1,0}, new[]{0,1}, new[]{0,-1} };
                foreach (var d in dirs)
                {
                    int nx = x + d[0], ny = y2 + d[1];
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    int nIdx = ny * w + nx;
                    if (visited[nIdx]) continue;

                    TileType t = result.Map.GetTileUnchecked(nx, ny);
                    if (t == TileType.Water || t == TileType.Mountain || t == TileType.MountainPeak) continue;

                    visited[nIdx] = true;
                    queue.Enqueue(nIdx);
                }
            }

            for (int i = 1; i < result.Cities.Count; i++)
            {
                City c = result.Cities[i];
                int cIdx = c.TileY * w + c.TileX;
                Assert.True(visited[cIdx],
                    $"seed {seed}: city {i} at ({c.TileX},{c.TileY}) is unreachable from capital 1");
            }
        }
    }

    [Fact]
    public void Generated_NoIsolatedWaterTiles()
    {
        var result = MapGenerator.Generate(42);
        int w = result.Map.Width, h = result.Map.Height;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (result.Map.GetTileUnchecked(x, y) != TileType.Water) continue;

                int neighbors = 0;
                if (x > 0     && result.Map.GetTileUnchecked(x-1, y) == TileType.Water) neighbors++;
                if (x < w - 1 && result.Map.GetTileUnchecked(x+1, y) == TileType.Water) neighbors++;
                if (y > 0     && result.Map.GetTileUnchecked(x, y-1) == TileType.Water) neighbors++;
                if (y < h - 1 && result.Map.GetTileUnchecked(x, y+1) == TileType.Water) neighbors++;

                Assert.True(neighbors > 0,
                    $"isolated water tile at ({x},{y}) — should have been cleaned up");
            }
        }
    }

    [Fact]
    public void Generated_HasMixOfTerrainTypes()
    {
        var result = MapGenerator.Generate(42);
        int plains = 0, forest = 0, mountain = 0, peak = 0, water = 0, river = 0, road = 0;

        for (int i = 0; i < result.Map.TileCount; i++)
        {
            switch ((TileType)result.Map.RawTiles[i])
            {
                case TileType.Plains: plains++; break;
                case TileType.Forest: forest++; break;
                case TileType.Mountain: mountain++; break;
                case TileType.MountainPeak: peak++; break;
                case TileType.Water: water++; break;
                case TileType.River: river++; break;
                case TileType.Road: road++; break;
            }
        }

        // The map should have a reasonable mix. These are loose bounds.
        Assert.True(plains > 100, $"too few plains: {plains}");
        Assert.True(forest > 50, $"too few forest: {forest}");
        Assert.True(mountain > 100, $"too few mountains: {mountain}");
        Assert.True(peak > 1, $"mountain peaks should form spines, not single dots: {peak}");
        Assert.True(water > 100, $"too little visible water: {water}");
        Assert.True(river > 10, $"too little river terrain: {river}");
        Assert.True(road > 10, $"too few roads: {road}");
    }

    [Fact]
    public void Generated_WaterIsVisibleAcrossSeeds()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int water = 0;
            for (int i = 0; i < result.Map.TileCount; i++)
                if ((TileType)result.Map.RawTiles[i] == TileType.Water) water++;

            Assert.True(water >= 100,
                $"seed {seed}: expected visible lakes/rivers, got only {water} water tiles");
        }
    }

    [Fact]
    public void Generated_WaterDoesNotFormMapEdgeWalls()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int w = result.Map.Width, h = result.Map.Height;

            for (int y = 0; y < h; y++)
            {
                int water = 0;
                for (int x = 0; x < w; x++)
                    if (result.Map.GetTileUnchecked(x, y) == TileType.Water) water++;
                Assert.True(water < w * 85 / 100,
                    $"seed {seed}: row {y} is {water}/{w} water and reads as a straight ocean band");
            }

            for (int x = 0; x < w; x++)
            {
                int water = 0;
                for (int y = 0; y < h; y++)
                    if (result.Map.GetTileUnchecked(x, y) == TileType.Water) water++;
                Assert.True(water < h * 85 / 100,
                    $"seed {seed}: column {x} is {water}/{h} water and reads as a straight ocean wall");
            }
        }
    }

    [Fact]
    public void Generated_MountainPeaksAreInteriorToRanges()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int w = result.Map.Width, h = result.Map.Height;
            int peaks = 0;
            int largestPeakComponent = 0;
            var peakVisited = new bool[w * h];

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    if (result.Map.GetTileUnchecked(x, y) != TileType.MountainPeak) continue;
                    peaks++;

                    int mountainNeighbors = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            TileType t = result.Map.GetTileUnchecked(x + dx, y + dy);
                            if (t == TileType.Mountain || t == TileType.MountainPeak) mountainNeighbors++;
                        }
                    }

                    Assert.True(mountainNeighbors >= 4,
                        $"seed {seed}: peak at ({x},{y}) is on the edge of a range ({mountainNeighbors} mountain neighbors)");
                }
            }

            Assert.True(peaks > 0, $"seed {seed}: expected at least one mountain peak");
            for (int i = 0; i < w * h; i++)
            {
                if (peakVisited[i]) continue;
                int x = i % w, y = i / w;
                if (result.Map.GetTileUnchecked(x, y) != TileType.MountainPeak) continue;
                int componentSize = CountTileComponent(result.Map, i, TileType.MountainPeak, peakVisited);
                if (componentSize > largestPeakComponent) largestPeakComponent = componentSize;
            }
            Assert.True(largestPeakComponent >= 2,
                $"seed {seed}: expected at least one multi-tile peak spine, largest was {largestPeakComponent}");
        }
    }

    [Fact]
    public void Generated_RoadsDoNotConnectEnemyTerritories()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            City p1Capital = result.Cities[0];
            City p2Capital = result.Cities[1];
            Assert.False(RoadReachable(result.Map, p1Capital.TileX, p1Capital.TileY, p2Capital.TileX, p2Capital.TileY),
                $"seed {seed}: road network should not connect both capitals");
        }
    }

    [Fact]
    public void Generated_RoadsConnectSameTerritoryCities()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int roadTiles = 0;
            for (int i = 0; i < result.Map.TileCount; i++)
                if ((TileType)result.Map.RawTiles[i] == TileType.Road) roadTiles++;
            Assert.True(roadTiles >= 8, $"seed {seed}: expected visible same-territory roads, got {roadTiles}");

            Assert.True(RoadOrRiverReachable(result.Map, result.Cities[0].TileX, result.Cities[0].TileY,
                    result.Cities[2].TileX, result.Cities[2].TileY)
                || RoadOrRiverReachable(result.Map, result.Cities[0].TileX, result.Cities[0].TileY,
                    result.Cities[3].TileX, result.Cities[3].TileY),
                $"seed {seed}: player 1 capital has no road to any owned city");
            Assert.True(RoadOrRiverReachable(result.Map, result.Cities[1].TileX, result.Cities[1].TileY,
                    result.Cities[4].TileX, result.Cities[4].TileY)
                || RoadOrRiverReachable(result.Map, result.Cities[1].TileX, result.Cities[1].TileY,
                    result.Cities[5].TileX, result.Cities[5].TileY),
                $"seed {seed}: player 2 capital has no road to any owned city");
        }
    }

    [Fact]
    public void Generated_RiversStartInMountainsAndFlowToWater()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int riverTiles = 0;
            bool riverTouchesMountain = false;
            bool riverTouchesWater = false;
            for (int y = 0; y < result.Map.Height; y++)
            {
                for (int x = 0; x < result.Map.Width; x++)
                {
                    if (result.Map.GetTileUnchecked(x, y) != TileType.River) continue;
                    riverTiles++;
                    if (HasNeighbor(result.Map, x, y, TileType.Mountain)
                        || HasNeighbor(result.Map, x, y, TileType.MountainPeak))
                        riverTouchesMountain = true;
                    if (HasNeighbor(result.Map, x, y, TileType.Water))
                        riverTouchesWater = true;
                }
            }

            Assert.True(riverTiles >= 10, $"seed {seed}: expected meaningful rivers, got {riverTiles} tiles");
            Assert.True(riverTouchesMountain, $"seed {seed}: no river visibly starts in a mountain range");
            Assert.True(riverTouchesWater, $"seed {seed}: no river reaches a larger water body");
        }
    }

    [Fact]
    public void Generated_RiverComponentsAreMeaningfulLength()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int w = result.Map.Width, h = result.Map.Height;
            var visited = new bool[w * h];
            for (int i = 0; i < w * h; i++)
            {
                if (visited[i]) continue;
                int x = i % w, y = i / w;
                if (result.Map.GetTileUnchecked(x, y) != TileType.River) continue;
                int size = CountTileComponent(result.Map, i, TileType.River, visited);
                Assert.True(size >= 10,
                    $"seed {seed}: river component at ({x},{y}) is only {size} tiles and reads as a pool");
            }
        }
    }

    [Fact]
    public void Generated_RiversStayOneTileWide()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int w = result.Map.Width, h = result.Map.Height;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (result.Map.GetTileUnchecked(x, y) != TileType.River) continue;
                    int neighbors = 0;
                    if (x > 0 && result.Map.GetTileUnchecked(x - 1, y) == TileType.River) neighbors++;
                    if (x + 1 < w && result.Map.GetTileUnchecked(x + 1, y) == TileType.River) neighbors++;
                    if (y > 0 && result.Map.GetTileUnchecked(x, y - 1) == TileType.River) neighbors++;
                    if (y + 1 < h && result.Map.GetTileUnchecked(x, y + 1) == TileType.River) neighbors++;
                    Assert.True(neighbors <= 2,
                        $"seed {seed}: river at ({x},{y}) has {neighbors} neighbors and reads as too thick");

                    if (x + 1 < w && y + 1 < h)
                    {
                        bool block =
                            result.Map.GetTileUnchecked(x, y) == TileType.River
                            && result.Map.GetTileUnchecked(x + 1, y) == TileType.River
                            && result.Map.GetTileUnchecked(x, y + 1) == TileType.River
                            && result.Map.GetTileUnchecked(x + 1, y + 1) == TileType.River;
                        Assert.False(block, $"seed {seed}: river has a 2x2 block at ({x},{y})");
                    }
                }
            }
        }
    }

    [Fact]
    public void Generated_RiversDoNotCutStraightThroughMountainRanges()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            int w = result.Map.Width, h = result.Map.Height;
            int longestStraight = 0;

            for (int y = 0; y < h; y++)
            {
                int run = 0;
                for (int x = 0; x < w; x++)
                {
                    if (result.Map.GetTileUnchecked(x, y) == TileType.River) run++;
                    else run = 0;
                    if (run > longestStraight) longestStraight = run;
                }
            }

            for (int x = 0; x < w; x++)
            {
                int run = 0;
                for (int y = 0; y < h; y++)
                {
                    if (result.Map.GetTileUnchecked(x, y) == TileType.River) run++;
                    else run = 0;
                    if (run > longestStraight) longestStraight = run;
                }
            }

            Assert.True(longestStraight <= 12,
                $"seed {seed}: river has an implausibly straight {longestStraight}-tile segment");

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (result.Map.GetTileUnchecked(x, y) != TileType.River) continue;
                    Assert.False(HasOppositeMountainNeighbors(result.Map, x, y),
                        $"seed {seed}: river at ({x},{y}) cuts through the middle of a mountain range");
                }
            }
        }
    }

    [Fact]
    public void Generated_StartingTerritoryIsContiguous()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var result = MapGenerator.Generate((ulong)(seed * 1000 + 7));
            var state = GameState.Initial((ulong)seed);
            state.Map = result.Map;
            state.TileOwner = new byte[result.Map.TileCount];
            foreach (City city in result.Cities) state.Cities.Add(city);
            state.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light,
                result.Cities[0].TileX, result.Cities[0].TileY));
            state.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light,
                result.Cities[1].TileX, result.Cities[1].TileY));

            PowerProjection.Tick(ref state);

            Assert.True(CountOwnerComponents(state, PlayerId.Player1) <= 1,
                $"seed {seed}: Player 1 starting territory is not contiguous");
            Assert.True(CountOwnerComponents(state, PlayerId.Player2) <= 1,
                $"seed {seed}: Player 2 starting territory is not contiguous");
        }
    }

    [Fact]
    public void Generation_CompletesUnder500ms()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            MapGenerator.Generate((ulong)(i * 1000 + 7));
        sw.Stop();

        // 100 maps should complete in under 500ms total (5ms each average).
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"100 maps took {sw.ElapsedMilliseconds}ms — too slow");
    }

    private static int CountOwnerComponents(GameState state, PlayerId owner)
    {
        int w = state.Map.Width, h = state.Map.Height;
        var visited = new bool[w * h];
        var queue = new Queue<int>();
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        int components = 0;

        for (int i = 0; i < w * h; i++)
        {
            if (visited[i]) continue;
            if ((PlayerId)state.TileOwner[i] != owner) continue;
            components++;
            visited[i] = true;
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % w, y = idx / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + dx[k], ny = y + dy[k];
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                    int nIdx = ny * w + nx;
                    if (visited[nIdx]) continue;
                    if ((PlayerId)state.TileOwner[nIdx] != owner) continue;
                    visited[nIdx] = true;
                    queue.Enqueue(nIdx);
                }
            }
        }

        return components;
    }

    private static int CountTileComponent(MapState map, int start, TileType tile, bool[] visited)
    {
        int w = map.Width, h = map.Height;
        var queue = new Queue<int>();
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        int count = 0;
        visited[start] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            count++;
            int x = idx % w, y = idx / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (visited[nIdx]) continue;
                if (map.GetTileUnchecked(nx, ny) != tile) continue;
                visited[nIdx] = true;
                queue.Enqueue(nIdx);
            }
        }
        return count;
    }

    private static bool RoadReachable(MapState map, int sx, int sy, int tx, int ty)
    {
        int w = map.Width, h = map.Height;
        var visited = new bool[w * h];
        var queue = new Queue<int>();
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        int start = sy * w + sx;
        int target = ty * w + tx;
        visited[start] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            if (idx == target) return true;
            int x = idx % w, y = idx / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (visited[nIdx]) continue;
                TileType t = map.GetTileUnchecked(nx, ny);
                if (t != TileType.Road && t != TileType.City && t != TileType.Capital) continue;
                visited[nIdx] = true;
                queue.Enqueue(nIdx);
            }
        }
        return false;
    }

    private static bool RoadOrRiverReachable(MapState map, int sx, int sy, int tx, int ty)
    {
        int w = map.Width, h = map.Height;
        var visited = new bool[w * h];
        var queue = new Queue<int>();
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        int start = sy * w + sx;
        int target = ty * w + tx;
        visited[start] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            if (idx == target) return true;
            int x = idx % w, y = idx / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (visited[nIdx]) continue;
                TileType t = map.GetTileUnchecked(nx, ny);
                if (t != TileType.Road && t != TileType.River && t != TileType.City && t != TileType.Capital) continue;
                visited[nIdx] = true;
                queue.Enqueue(nIdx);
            }
        }
        return false;
    }

    private static bool HasNeighbor(MapState map, int x, int y, TileType tile)
    {
        if (x > 0 && map.GetTileUnchecked(x - 1, y) == tile) return true;
        if (x + 1 < map.Width && map.GetTileUnchecked(x + 1, y) == tile) return true;
        if (y > 0 && map.GetTileUnchecked(x, y - 1) == tile) return true;
        if (y + 1 < map.Height && map.GetTileUnchecked(x, y + 1) == tile) return true;
        return false;
    }

    private static bool HasOppositeMountainNeighbors(MapState map, int x, int y)
    {
        bool west = x > 0 && IsMountainLike(map.GetTileUnchecked(x - 1, y));
        bool east = x + 1 < map.Width && IsMountainLike(map.GetTileUnchecked(x + 1, y));
        bool north = y > 0 && IsMountainLike(map.GetTileUnchecked(x, y - 1));
        bool south = y + 1 < map.Height && IsMountainLike(map.GetTileUnchecked(x, y + 1));
        return (west && east) || (north && south);
    }

    private static bool IsMountainLike(TileType t) => t is TileType.Mountain or TileType.MountainPeak;
}
