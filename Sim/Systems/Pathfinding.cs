using System;
using System.Collections.Generic;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Deterministic A* on the tile grid. Returns a list of flat tile indices
// from (exclusive of) start to (inclusive of) goal. Empty list = no path.
//
// Determinism rules followed here:
//   1. Priority queue tiebreaker: when two open nodes have equal f-cost, we
//      break by their flat tile index. Every (f, index) pair is unique, so
//      .NET's PriorityQueue produces the same pop order on every platform.
//   2. Per-node arrays (cameFrom, gScore, closed) are int- and bool-arrays
//      keyed by flat tile index — random access only, never iterated. No
//      Dictionary, no HashSet (their iteration order is not defined).
//   3. Costs are integers in units of "twelfths of a tile". The plain-tile
//      cost is 12; road is 8 (1.5x faster); forest-for-heavy is 24 (half
//      speed). Mountain-for-heavy is impassable, not high-cost. Using
//      integers avoids any FP arithmetic inside the inner loop.
public static class Pathfinding
{
    // Cost units. Keep small — A*'s priority queue holds (cost, index)
    // pairs; if costs explode, the priority encoding below could overflow.
    public const int CostPlains   = 12;
    public const int CostRoad     = 8;       // 12 / 1.5
    public const int CostWater    = 48;      // broad water = quarter speed
    public const int CostRiver    = 36;      // 1/3 speed crossing
    public const int CostForestH  = 24;      // heavy in forest = half speed
    public const int CostMountainL = 48;     // light in mountain = quarter speed
    public const int CostBuildRoad = 1;
    public const int CostBuildPlains = 10;
    public const int CostBuildForest = 18;
    public const int CostBuildMountain = 45;
    public const int CostBuildBridge = 70;

    /// <summary>
    /// Find a 4-connected path. Returns flat tile indices in walking order,
    /// excluding start, including goal. Empty list if unreachable or if
    /// start == goal.
    /// </summary>
    public static List<int> FindPath(MapState map, int startX, int startY, int goalX, int goalY, bool isHeavyUnit)
    {
        var result = new List<int>();
        if (!map.InBounds(startX, startY) || !map.InBounds(goalX, goalY)) return result;
        if (startX == goalX && startY == goalY) return result;

        TileType goalTile = map.GetTileUnchecked(goalX, goalY);
        if (!goalTile.IsPassable(isHeavyUnit)) return result;

        int w = map.Width, h = map.Height;
        int n = w * h;
        int startIdx = startY * w + startX;
        int goalIdx  = goalY  * w + goalX;

        // Per-node state. -1 = unset; the closed bitmap doubles as "have
        // we already extracted this node from the open set."
        int[] gScore = new int[n];
        int[] cameFrom = new int[n];
        bool[] closed = new bool[n];
        Array.Fill(gScore, int.MaxValue);
        Array.Fill(cameFrom, -1);

        gScore[startIdx] = 0;

        // Priority queue with stable tiebreaker. Encode the priority as a
        // long: high 32 bits = f-cost, low 32 bits = tile index. Since the
        // index is unique per node, no two priority values collide, and the
        // PQ pops in fully-deterministic order.
        var open = new PriorityQueue<int, long>();
        open.Enqueue(startIdx, EncodePriority(Heuristic(startX, startY, goalX, goalY) * CostPlains, startIdx));

        // 4-connected: north, east, south, west, in fixed order. Order
        // matters for determinism — ties resolve to whichever neighbor
        // happens to come first.
        ReadOnlySpan<int> dx = stackalloc int[] { 0,  1, 0, -1 };
        ReadOnlySpan<int> dy = stackalloc int[] { -1, 0, 1,  0 };

        while (open.TryDequeue(out int current, out _))
        {
            if (closed[current]) continue;       // stale entry from re-enqueue
            if (current == goalIdx) break;
            closed[current] = true;

            int cx = current % w;
            int cy = current / w;
            int currentG = gScore[current];

            for (int i = 0; i < 4; i++)
            {
                int nx = cx + dx[i];
                int ny = cy + dy[i];
                if (!map.InBounds(nx, ny)) continue;

                int nIdx = ny * w + nx;
                if (closed[nIdx]) continue;

                TileType t = map.GetTileUnchecked(nx, ny);
                if (!t.IsPassable(isHeavyUnit)) continue;

                int stepCost = TileEnterCost(t, isHeavyUnit);
                int tentativeG = currentG + stepCost;
                if (tentativeG >= gScore[nIdx]) continue;

                cameFrom[nIdx] = current;
                gScore[nIdx] = tentativeG;
                int f = tentativeG + Heuristic(nx, ny, goalX, goalY) * CostPlains;
                open.Enqueue(nIdx, EncodePriority(f, nIdx));
            }
        }

        if (cameFrom[goalIdx] < 0) return result;

        // Walk back from goal to start, then reverse.
        int idx = goalIdx;
        while (idx != startIdx)
        {
            result.Add(idx);
            idx = cameFrom[idx];
        }
        result.Reverse();
        return result;
    }

    private static int Heuristic(int ax, int ay, int bx, int by)
        => System.Math.Abs(ax - bx) + System.Math.Abs(ay - by);

    private static long EncodePriority(int fCost, int tileIndex)
        => ((long)fCost << 32) | (uint)tileIndex;

    private static int TileEnterCost(TileType t, bool isHeavyUnit) => t switch
    {
        TileType.Road => CostRoad,
        TileType.Bridge => CostRoad,
        TileType.Water => CostWater,
        TileType.River => CostRiver,
        TileType.Forest => isHeavyUnit ? CostForestH : CostPlains,
        TileType.Mountain => isHeavyUnit ? int.MaxValue : CostMountainL, // heavy filtered earlier
        _ => CostPlains,
    };

    /// <summary>
    /// Deterministic engineering A*. Used by road preview and BuildRoadCommand.
    /// Returns a path excluding start, including goal. Mountain peaks, cities,
    /// capitals, and forts are blocked. Rivers and 1-tile-wide water channels
    /// are bridgeable; broad water bodies and skinny land causeways squeezed
    /// between water are blocked so roads do not snake along lake edges.
    /// </summary>
    public static List<int> FindRoadBuildPath(MapState map, int startX, int startY, int goalX, int goalY)
    {
        var result = new List<int>();
        if (!map.InBounds(startX, startY) || !map.InBounds(goalX, goalY)) return result;
        if (!IsEngineeringTileCandidate(map, goalX, goalY)) return result;
        if (startX == goalX && startY == goalY)
        {
            result.Add(goalY * map.Width + goalX);
            return result;
        }

        int w = map.Width, h = map.Height, n = w * h;
        int startIdx = startY * w + startX;
        int goalIdx = goalY * w + goalX;

        int[] gScore = new int[n];
        int[] cameFrom = new int[n];
        bool[] closed = new bool[n];
        Array.Fill(gScore, int.MaxValue);
        Array.Fill(cameFrom, -1);

        gScore[startIdx] = 0;
        var open = new PriorityQueue<int, long>();
        open.Enqueue(startIdx, EncodePriority(Heuristic(startX, startY, goalX, goalY) * CostBuildPlains, startIdx));

        ReadOnlySpan<int> dx = stackalloc int[] { 0, 1, 0, -1 };
        ReadOnlySpan<int> dy = stackalloc int[] { -1, 0, 1, 0 };

        while (open.TryDequeue(out int current, out _))
        {
            if (closed[current]) continue;
            if (current == goalIdx) break;
            closed[current] = true;

            int cx = current % w, cy = current / w;
            int currentG = gScore[current];

            for (int i = 0; i < 4; i++)
            {
                int nx = cx + dx[i], ny = cy + dy[i];
                if (!map.InBounds(nx, ny)) continue;

                int nIdx = ny * w + nx;
                if (closed[nIdx]) continue;

                TileType t = map.GetTileUnchecked(nx, ny);
                if (!CanEngineerEnter(map, cx, cy, nx, ny)) continue;

                int tentativeG = currentG + EngineeringEnterCost(t);
                if (tentativeG >= gScore[nIdx]) continue;

                cameFrom[nIdx] = current;
                gScore[nIdx] = tentativeG;
                int f = tentativeG + Heuristic(nx, ny, goalX, goalY) * CostBuildPlains;
                open.Enqueue(nIdx, EncodePriority(f, nIdx));
            }
        }

        if (cameFrom[goalIdx] < 0) return result;

        int idx = goalIdx;
        while (idx != startIdx)
        {
            result.Add(idx);
            idx = cameFrom[idx];
        }
        result.Reverse();
        return result;
    }

    public static bool IsEngineeringPassable(TileType t) => t switch
    {
        TileType.MountainPeak => false,
        TileType.City => false,
        TileType.Capital => false,
        TileType.Fort => false,
        _ => true,
    };

    public static int EngineeringEnterCost(TileType t) => t switch
    {
        TileType.Road => CostBuildRoad,
        TileType.Bridge => CostBuildRoad,
        TileType.Plains => CostBuildPlains,
        TileType.Forest => CostBuildForest,
        TileType.Mountain => CostBuildMountain,
        TileType.River => CostBuildBridge,
        TileType.Water => CostBuildBridge,
        _ => CostBuildPlains,
    };

    public static bool CanEngineerEnter(MapState map, int fromX, int fromY, int toX, int toY)
    {
        if (!map.InBounds(fromX, fromY) || !map.InBounds(toX, toY)) return false;
        TileType from = map.GetTileUnchecked(fromX, fromY);
        TileType to = map.GetTileUnchecked(toX, toY);

        if (!IsEngineeringTileCandidate(map, toX, toY)) return false;
        if (TerrainRules.IsWaterway(from) && TerrainRules.IsWaterway(to)) return false;

        if (TerrainRules.IsWaterway(to))
        {
            int dx = toX - fromX, dy = toY - fromY;
            int farX = toX + dx, farY = toY + dy;
            if (!map.InBounds(farX, farY)) return false;
            TileType far = map.GetTileUnchecked(farX, farY);
            if (TerrainRules.IsWaterway(far)) return false;
            if (!IsEngineeringTileCandidate(map, farX, farY)) return false;
            return !IsNewRoadCauseway(map, farX, farY, far);
        }

        return !IsNewRoadCauseway(map, toX, toY, to);
    }

    public static bool IsBridgeTerrain(TileType t) => t is TileType.River or TileType.Water;

    public static bool IsEngineeringTileCandidate(MapState map, int x, int y)
    {
        TileType t = map.GetTileUnchecked(x, y);
        if (!IsEngineeringPassable(t)) return false;
        if (TerrainRules.IsWaterway(t))
            return TerrainRules.IsOneTileWaterway(map, x, y);
        return !IsNewRoadCauseway(map, x, y, t);
    }

    private static bool IsNewRoadCauseway(MapState map, int x, int y, TileType t)
    {
        if (t is TileType.Road or TileType.Bridge) return false;
        if (TerrainRules.IsWaterway(t)) return false;
        return TerrainRules.IsNarrowLandCauseway(map, x, y);
    }
}
