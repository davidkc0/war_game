using System;
using System.Collections.Generic;
using WarGame.Sim.State;

namespace WarGame.Sim.Generation;

// Scores a generated map on four orthogonal axes. The MapGenerator's
// reject-and-retry loop calls this; maps below the threshold are discarded
// and a fresh seed is tried.
//
// Each axis is in [0, 100]. The total cap of 400 is rarely achieved; the
// acceptance threshold (default 250) lets a map have weak spots on one or
// two axes as long as the others compensate.
//
// All math is integer / pure C#; the validator runs inside the Sim layer
// and respects determinism rules (no Dictionary iteration influencing
// output, no LINQ-by-reference, no FP).
public static class BalanceValidator
{
    public const int DefaultAcceptanceThreshold = 250;

    public readonly struct Result
    {
        public readonly int PathSymmetry;
        public readonly int TerrainParity;
        public readonly int ChokePoints;
        public readonly int Connectivity;
        public readonly int Total;
        public readonly bool Accepted;
        public readonly string? RejectReason;

        public Result(int pathSym, int terrain, int choke, int conn, int threshold, string? rejectReason = null)
        {
            PathSymmetry = pathSym;
            TerrainParity = terrain;
            ChokePoints = choke;
            Connectivity = conn;
            Total = pathSym + terrain + choke + conn;
            Accepted = rejectReason is null && Total >= threshold;
            RejectReason = rejectReason;
        }

        public override string ToString()
            => $"path={PathSymmetry} terrain={TerrainParity} choke={ChokePoints} conn={Connectivity} total={Total} {(Accepted ? "ACCEPT" : "REJECT" + (RejectReason is not null ? " (" + RejectReason + ")" : ""))}";
    }

    public static Result Score(MapState map, IReadOnlyList<City> cities,
                                int threshold = DefaultAcceptanceThreshold)
    {
        // ---- Connectivity (instant-fail axis) ---------------------------
        int connScore = ScoreConnectivity(map, cities, out bool fullyConnected);
        if (!fullyConnected)
            return new Result(0, 0, 0, 0, threshold, "city unreachable");

        // ---- The other three axes ---------------------------------------
        int pathScore  = ScorePathSymmetry(map, cities);
        int terrScore  = ScoreTerrainParity(map, cities);
        int chokeScore = ScoreChokePoints(map, cities);

        return new Result(pathScore, terrScore, chokeScore, connScore, threshold);
    }

    // -------- 1) Path symmetry ----------------------------------------
    // For each capital, find the shortest 4-connected passable distance
    // to the nearest *neutral* city (or to the enemy capital if there are
    // no neutrals). Compare distances; >20% imbalance → score drops.
    private static int ScorePathSymmetry(MapState map, IReadOnlyList<City> cities)
    {
        if (cities.Count < 2) return 50;

        City cap1 = FindCapital(cities, PlayerId.Player1);
        City cap2 = FindCapital(cities, PlayerId.Player2);
        if (cap1.Id == cap2.Id) return 0;

        int d1 = ShortestPathThroughLand(map, cap1.TileX, cap1.TileY,
                    NearestOtherCity(cities, cap1));
        int d2 = ShortestPathThroughLand(map, cap2.TileX, cap2.TileY,
                    NearestOtherCity(cities, cap2));

        if (d1 < 0 || d2 < 0) return 0;     // one capital is isolated

        int min = System.Math.Min(d1, d2);
        int max = System.Math.Max(d1, d2);
        if (max == 0) return 100;
        // Penalty: difference / max. ≤ 20% → full score; ≥ 80% → 0.
        int diffPct = (max - min) * 100 / max;
        return diffPct <= 20 ? 100
             : diffPct >= 80 ? 0
             : 100 - (diffPct - 20) * 100 / 60;
    }

    // -------- 2) Terrain parity ---------------------------------------
    // Count of Plains/Forest/Mountain tiles within Manhattan radius
    // TerrainSampleRadius of each capital. Compare per terrain type.
    private const int TerrainSampleRadius = 10;
    private static int ScoreTerrainParity(MapState map, IReadOnlyList<City> cities)
    {
        City cap1 = FindCapital(cities, PlayerId.Player1);
        City cap2 = FindCapital(cities, PlayerId.Player2);

        int p1Plains = 0, p1Forest = 0, p1Mountain = 0;
        int p2Plains = 0, p2Forest = 0, p2Mountain = 0;

        SampleTerrainNear(map, cap1.TileX, cap1.TileY, ref p1Plains, ref p1Forest, ref p1Mountain);
        SampleTerrainNear(map, cap2.TileX, cap2.TileY, ref p2Plains, ref p2Forest, ref p2Mountain);

        // Score per type: 100 - 100 * |a - b| / (a + b + 1).
        int sP = 100 - 100 * System.Math.Abs(p1Plains   - p2Plains)   / (p1Plains   + p2Plains   + 1);
        int sF = 100 - 100 * System.Math.Abs(p1Forest   - p2Forest)   / (p1Forest   + p2Forest   + 1);
        int sM = 100 - 100 * System.Math.Abs(p1Mountain - p2Mountain) / (p1Mountain + p2Mountain + 1);
        return (sP + sF + sM) / 3;
    }

    private static void SampleTerrainNear(MapState map, int cx, int cy,
        ref int plains, ref int forest, ref int mountain)
    {
        for (int dy = -TerrainSampleRadius; dy <= TerrainSampleRadius; dy++)
        {
            int y = cy + dy;
            if ((uint)y >= (uint)map.Height) continue;
            int rangeX = TerrainSampleRadius - System.Math.Abs(dy);
            for (int dx = -rangeX; dx <= rangeX; dx++)
            {
                int x = cx + dx;
                if ((uint)x >= (uint)map.Width) continue;
                switch (map.GetTileUnchecked(x, y))
                {
                    case TileType.Plains: plains++; break;
                    case TileType.Forest: forest++; break;
                    case TileType.Mountain: mountain++; break;
                }
            }
        }
    }

    // -------- 3) Choke-point count ------------------------------------
    // A "choke" is a passable tile column or row across the central seam
    // of the map where the passable width drops to 1–2 tiles. Counted as
    // the number of distinct narrow runs along the central axis (vertical
    // for a wide map, horizontal otherwise).
    //
    // Target: 2–6. <2 → too open / no fronts; >6 → maze. Both reduce score.
    private static int ScoreChokePoints(MapState map, IReadOnlyList<City> cities)
    {
        int chokes = CountChokesAcrossCenter(map);
        if (chokes >= 2 && chokes <= 6) return 100;
        if (chokes < 2) return 100 - (2 - chokes) * 40;
        return System.Math.Max(0, 100 - (chokes - 6) * 15);
    }

    private static int CountChokesAcrossCenter(MapState map)
    {
        // Sample three vertical seams across the map (a quarter, half, and
        // three-quarters width). At each seam, count contiguous "narrow"
        // runs — segments where the passable strip is ≤ 2 tiles wide.
        int w = map.Width, h = map.Height;
        int total = 0;
        int[] seams = { w / 4, w / 2, 3 * w / 4 };
        foreach (int seamX in seams)
        {
            int runStart = -1, runWidth = 0;
            for (int y = 0; y < h; y++)
            {
                bool passable = IsPassableTile(map.GetTileUnchecked(seamX, y));
                if (passable)
                {
                    if (runStart < 0) { runStart = y; runWidth = 1; }
                    else runWidth++;
                }
                else
                {
                    if (runStart >= 0)
                    {
                        if (runWidth <= 2) total++;
                        runStart = -1;
                        runWidth = 0;
                    }
                }
            }
            if (runStart >= 0 && runWidth <= 2) total++;
        }
        return total;
    }

    // -------- 4) Connectivity -----------------------------------------
    // Hard requirement: every city must be reachable from every other city
    // via 4-connected non-water non-mountain tiles. A failure here is an
    // instant reject regardless of other axes.
    private static int ScoreConnectivity(MapState map, IReadOnlyList<City> cities,
                                          out bool fullyConnected)
    {
        fullyConnected = false;
        if (cities.Count < 2) { fullyConnected = true; return 100; }

        int w = map.Width, h = map.Height;
        var visited = new bool[w * h];
        var queue = new Queue<int>();
        int startIdx = cities[0].TileY * w + cities[0].TileX;
        visited[startIdx] = true;
        queue.Enqueue(startIdx);

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w, y = idx / w;
            int[] dx = { 0, 1, 0, -1 };
            int[] dy = { -1, 0, 1, 0 };
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (visited[nIdx]) continue;
                if (!IsPassableTile(map.GetTileUnchecked(nx, ny))) continue;
                visited[nIdx] = true;
                queue.Enqueue(nIdx);
            }
        }

        for (int i = 1; i < cities.Count; i++)
        {
            City c = cities[i];
            int idx = c.TileY * w + c.TileX;
            if (!visited[idx]) return 0;
        }
        fullyConnected = true;
        return 100;
    }

    // -------- helpers -----------------------------------------------------

    private static bool IsPassableTile(TileType t)
        => t != TileType.Water && t != TileType.Mountain && t != TileType.MountainPeak;

    private static City FindCapital(IReadOnlyList<City> cities, PlayerId p)
    {
        for (int i = 0; i < cities.Count; i++)
        {
            City c = cities[i];
            if (c.IsCapital && c.OriginalOwner == p) return c;
        }
        // Fallback: first city of that owner.
        for (int i = 0; i < cities.Count; i++)
            if (cities[i].OriginalOwner == p) return cities[i];
        return cities[0];
    }

    private static (int x, int y) NearestOtherCity(IReadOnlyList<City> cities, City self)
    {
        int bestI = -1, bestD = int.MaxValue;
        for (int i = 0; i < cities.Count; i++)
        {
            if (cities[i].Id == self.Id) continue;
            int d = System.Math.Abs(cities[i].TileX - self.TileX)
                  + System.Math.Abs(cities[i].TileY - self.TileY);
            if (d < bestD) { bestD = d; bestI = i; }
        }
        if (bestI < 0) return (self.TileX, self.TileY);
        return (cities[bestI].TileX, cities[bestI].TileY);
    }

    /// <summary>
    /// 4-connected BFS shortest-path step count through Plains/Forest/Road/
    /// City/Capital/Fort tiles. Returns -1 if unreachable.
    /// </summary>
    private static int ShortestPathThroughLand(MapState map, int sx, int sy, (int x, int y) goal)
    {
        int w = map.Width, h = map.Height;
        if (sx == goal.x && sy == goal.y) return 0;
        var dist = new int[w * h];
        Array.Fill(dist, -1);
        dist[sy * w + sx] = 0;
        var q = new Queue<int>();
        q.Enqueue(sy * w + sx);

        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        while (q.Count > 0)
        {
            int idx = q.Dequeue();
            int x = idx % w, y = idx / w;
            int d = dist[idx];
            if (x == goal.x && y == goal.y) return d;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (dist[nIdx] >= 0) continue;
                if (!IsPassableTile(map.GetTileUnchecked(nx, ny))) continue;
                dist[nIdx] = d + 1;
                q.Enqueue(nIdx);
            }
        }
        return -1;
    }
}
