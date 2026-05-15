using System.Collections.Generic;
using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Builds stored road/bridge orders one tile at a time. Completed segments are
// permanent terrain edits; cancelled orders keep completed work and refund
// nothing, matching fort construction's risk profile.
public static class RoadConstruction
{
    public const int RoadEcoCost = 2;
    public const int BridgeEcoCost = 8;
    public const int RoadBuildTicks = 30;
    public const int BridgeBuildTicks = 90;

    public static void Tick(ref GameState s)
    {
        if (s.PendingRoads is null || s.PendingRoads.Count == 0) return;

        for (int i = s.PendingRoads.Count - 1; i >= 0; i--)
        {
            RoadOrder order = s.PendingRoads[i];
            if (!ValidateOrder(ref s, order))
            {
                s.PendingRoads.RemoveAt(i);
                continue;
            }

            Span<Unit> units = CollectionsMarshal.AsSpan(s.Units);
            ref Unit u = ref units[order.UnitId];

            if (order.CurrentPathIndex >= order.Path.Count)
            {
                s.PendingRoads.RemoveAt(i);
                continue;
            }

            int flat = order.Path[order.CurrentPathIndex];
            int tx = flat % s.Map.Width, ty = flat / s.Map.Width;
            bool sameTile = u.TileX == tx && u.TileY == ty;
            if (!sameTile && !IsAdjacent(u.TileX, u.TileY, tx, ty))
            {
                s.PendingRoads.RemoveAt(i);
                continue;
            }

            TileType tile = s.Map.GetTileUnchecked(tx, ty);
            if (!sameTile && !Pathfinding.CanEngineerEnter(s.Map, u.TileX, u.TileY, tx, ty))
            {
                s.PendingRoads.RemoveAt(i);
                continue;
            }
            if (sameTile && !Pathfinding.IsEngineeringTileCandidate(s.Map, tx, ty))
            {
                s.PendingRoads.RemoveAt(i);
                continue;
            }
            if (IsTileOccupiedByOtherUnit(s, order.UnitId, tx, ty))
            {
                s.PendingRoads.RemoveAt(i);
                continue;
            }

            if (tile is TileType.Road or TileType.Bridge)
            {
                MoveBuilderTo(ref u, tx, ty);
                order.CurrentPathIndex++;
                order.TicksRemainingOnTile = 0;
                s.PendingRoads[i] = order;
                continue;
            }

            if (order.TicksRemainingOnTile <= 0)
            {
                int cost = EcoCostFor(tile);
                ref Player p = ref s.Players[(int)order.Owner];
                FP costFp = FP.FromInt(cost);
                if (p.Eco < costFp)
                {
                    s.PendingRoads[i] = order;
                    continue;
                }
                p.Eco -= costFp;
                order.TicksRemainingOnTile = BuildTicksFor(tile);
            }

            order.TicksRemainingOnTile--;
            if (order.TicksRemainingOnTile <= 0)
            {
                s.Map.SetTile(tx, ty, Pathfinding.IsBridgeTerrain(tile) ? TileType.Bridge : TileType.Road);
                MoveBuilderTo(ref u, tx, ty);
                order.CurrentPathIndex++;
            }

            s.PendingRoads[i] = order;
        }
    }

    public static int EcoCostFor(TileType t) => Pathfinding.IsBridgeTerrain(t) ? BridgeEcoCost : RoadEcoCost;
    public static int BuildTicksFor(TileType t) => Pathfinding.IsBridgeTerrain(t) ? BridgeBuildTicks : RoadBuildTicks;

    public static void CancelForUnit(ref GameState s, int unitId)
    {
        if (s.PendingRoads is null) return;
        for (int i = s.PendingRoads.Count - 1; i >= 0; i--)
            if (s.PendingRoads[i].UnitId == unitId)
                s.PendingRoads.RemoveAt(i);
    }

    public static bool HasOrderForUnit(in GameState s, int unitId)
    {
        if (s.PendingRoads is null) return false;
        for (int i = 0; i < s.PendingRoads.Count; i++)
            if (s.PendingRoads[i].UnitId == unitId)
                return true;
        return false;
    }

    private static bool ValidateOrder(ref GameState s, RoadOrder order)
    {
        if ((uint)order.UnitId >= (uint)s.Units.Count) return false;
        if (order.Path is null || order.Path.Count == 0) return false;

        Unit u = s.Units[order.UnitId];
        if (!u.IsAlive) return false;
        if (u.Owner != order.Owner) return false;
        if (u.Path is { Count: > 0 }) return false;
        return true;
    }

    private static bool IsTileOccupiedByOtherUnit(in GameState s, int selfId, int x, int y)
    {
        for (int i = 0; i < s.Units.Count; i++)
        {
            if (i == selfId) continue;
            Unit other = s.Units[i];
            if (!other.IsAlive) continue;
            if (other.TileX == x && other.TileY == y) return true;
        }
        return false;
    }

    private static bool IsAdjacent(int ax, int ay, int bx, int by)
    {
        int dx = ax > bx ? ax - bx : bx - ax;
        int dy = ay > by ? ay - by : by - ay;
        return dx + dy == 1;
    }

    private static void MoveBuilderTo(ref Unit u, int x, int y)
    {
        u.TileX = x;
        u.TileY = y;
        u.ProgressRaw = 0;
        u.Path.Clear();
    }
}
