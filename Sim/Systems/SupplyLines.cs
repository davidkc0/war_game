using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Computes operational supply after the current power-projection field has
// been refreshed. Friendly territory carries normal supply. Roads/bridges
// can carry road-assisted supply outside friendly territory, but enemy units
// standing on those road/bridge tiles interdict the bonus.
public static class SupplyLines
{
    public static void Tick(ref GameState s)
    {
        int w = s.Map.Width, h = s.Map.Height, n = w * h;
        EnsureBuffers(ref s, n);
        Array.Clear(s.TileSupplyOwner, 0, s.TileSupplyOwner.Length);
        Array.Clear(s.TileRoadSupplyOwner, 0, s.TileRoadSupplyOwner.Length);
        EnsureUnitBuffer(ref s);

        for (int pid = 1; pid < s.Players.Length; pid++)
        {
            var owner = (PlayerId)pid;
            ComputeForPlayer(ref s, owner);
        }

        Span<Unit> units = CollectionsMarshal.AsSpan(s.Units);
        for (int i = 0; i < units.Length; i++)
        {
            ref Unit u = ref units[i];
            SupplyStatus status = SupplyStatus.None;
            if (u.IsAlive && u.Owner != PlayerId.None)
            {
                int idx = u.TileY * w + u.TileX;
                TileType tile = s.Map.GetTileUnchecked(u.TileX, u.TileY);
                bool roadBonus = s.TileRoadSupplyOwner[idx] == (byte)u.Owner
                                 && IsRoadSupplyTile(tile)
                                 && !IsEnemyRoadBlocker(s, u.Owner, idx);
                bool normal = s.TileSupplyOwner[idx] == (byte)u.Owner;

                status = roadBonus
                    ? SupplyStatus.RoadSupplied
                    : normal ? SupplyStatus.Supplied : SupplyStatus.CutOff;
            }
            s.UnitSupplyStatus[i] = (byte)status;
        }
    }

    public static SupplyStatus GetUnitStatus(in GameState s, int unitId)
    {
        if (s.UnitSupplyStatus is null || (uint)unitId >= (uint)s.UnitSupplyStatus.Length)
            return SupplyStatus.None;
        return (SupplyStatus)s.UnitSupplyStatus[unitId];
    }

    private static void ComputeForPlayer(ref GameState s, PlayerId owner)
    {
        int w = s.Map.Width, h = s.Map.Height, n = w * h;
        var normalVisited = new bool[n];
        var roadVisited = new bool[n];
        var normalQueue = new Queue<int>();
        var roadQueue = new Queue<int>();
        var sources = new List<int>();

        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != owner) continue;
            int idx = c.TileY * w + c.TileX;
            sources.Add(idx);
            normalVisited[idx] = true;
            s.TileSupplyOwner[idx] = (byte)owner;
            normalQueue.Enqueue(idx);
        }

        Span<int> dx = stackalloc int[] { 0, 1, 0, -1 };
        Span<int> dy = stackalloc int[] { -1, 0, 1, 0 };

        while (normalQueue.Count > 0)
        {
            int cur = normalQueue.Dequeue();
            int cx = cur % w, cy = cur / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k], ny = cy + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (normalVisited[nIdx]) continue;
                if (s.TileOwner is null || (PlayerId)s.TileOwner[nIdx] != owner) continue;
                if (TerrainRules.IsBroadWater(s.Map, nx, ny)) continue;
                normalVisited[nIdx] = true;
                s.TileSupplyOwner[nIdx] = (byte)owner;
                normalQueue.Enqueue(nIdx);
            }
        }

        for (int i = 0; i < sources.Count; i++)
        {
            int idx = sources[i];
            roadVisited[idx] = true;
            roadQueue.Enqueue(idx);
            TileType t = (TileType)s.Map.RawTiles[idx];
            if (IsRoadSupplyTile(t) && !IsEnemyRoadBlocker(s, owner, idx))
                s.TileRoadSupplyOwner[idx] = (byte)owner;
        }

        while (roadQueue.Count > 0)
        {
            int cur = roadQueue.Dequeue();
            int cx = cur % w, cy = cur / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k], ny = cy + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (roadVisited[nIdx]) continue;

                TileType t = s.Map.GetTileUnchecked(nx, ny);
                bool friendly = s.TileOwner is not null && (PlayerId)s.TileOwner[nIdx] == owner;
                if (friendly && TerrainRules.IsBroadWater(s.Map, nx, ny)) friendly = false;
                bool road = IsRoadSupplyTile(t);
                bool roadOpen = road && !IsEnemyRoadBlocker(s, owner, nIdx);

                if (!friendly && !roadOpen) continue;
                if (road && !roadOpen)
                {
                    roadVisited[nIdx] = true;
                    continue;
                }

                roadVisited[nIdx] = true;
                roadQueue.Enqueue(nIdx);

                if (roadOpen)
                    s.TileRoadSupplyOwner[nIdx] = (byte)owner;
            }
        }
    }

    public static bool IsRoadSupplyTile(TileType t) => t is TileType.Road or TileType.Bridge;

    public static bool IsEnemyRoadBlocker(in GameState s, PlayerId owner, int tileIdx)
    {
        if (s.Map.Width <= 0) return false;
        int x = tileIdx % s.Map.Width, y = tileIdx / s.Map.Width;
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive) continue;
            if (u.Owner == PlayerId.None || u.Owner == owner) continue;
            if (u.TileX == x && u.TileY == y) return true;
        }
        return false;
    }

    private static void EnsureBuffers(ref GameState s, int tileCount)
    {
        if (s.TileSupplyOwner is null || s.TileSupplyOwner.Length != tileCount)
            s.TileSupplyOwner = new byte[tileCount];
        if (s.TileRoadSupplyOwner is null || s.TileRoadSupplyOwner.Length != tileCount)
            s.TileRoadSupplyOwner = new byte[tileCount];
    }

    private static void EnsureUnitBuffer(ref GameState s)
    {
        if (s.UnitSupplyStatus is null || s.UnitSupplyStatus.Length != s.Units.Count)
            s.UnitSupplyStatus = new byte[s.Units.Count];
        else
            Array.Clear(s.UnitSupplyStatus, 0, s.UnitSupplyStatus.Length);
    }
}
