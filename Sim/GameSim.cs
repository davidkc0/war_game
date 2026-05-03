using System.Collections.Generic;
using System.Runtime.InteropServices;
using WarGame.Sim.Commands;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;

namespace WarGame.Sim;

// Pure-function tick advancer. The single most important contract in the
// codebase: same input -> same output, byte-identical, on every platform we
// support. Every system added in later phases must extend this function (or
// a sub-system it calls) without breaking that contract.
//
// Hard rules (PLAN.md §1):
//   - no floats anywhere in this call graph,
//   - no DateTime.Now / Environment / unseeded Random,
//   - no Godot types — this assembly does not reference Godot.NET.Sdk,
//   - no Dictionary/HashSet iteration as a control-flow input (their order
//     is undefined),
//   - no LINQ ordering by reference equality.
public static class GameSim
{
    public const int TicksPerSecond = 30;

    public static GameState Step(GameState s, IReadOnlyList<Command>? commands)
    {
        s.Tick += 1;

        // Once a winner has been declared, freeze the sim. We still tick
        // the clock so replay timestamps remain stable, but commands and
        // physical systems are skipped — the final state is the artifact
        // the user / replay viewer wants to inspect.
        if (s.Winner != State.PlayerId.None) return s;

        // 1) Apply commands. Order is the order the network/replay delivered
        //    them — the caller is responsible for stable ordering.
        if (commands is not null)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                Command cmd = commands[i];
                switch (cmd)
                {
                    case MoveUnitCommand m:
                        ApplyMoveUnit(ref s, m);
                        break;
                    case BuildUnitCommand b:
                        ApplyBuildUnit(ref s, b);
                        break;
                    case BuildFortCommand f:
                        ApplyBuildFort(ref s, f);
                        break;
                    case RazeFortCommand r:
                        ApplyRazeFort(ref s, r);
                        break;
                    case BuildRoadCommand br:
                        ApplyBuildRoad(ref s, br);
                        break;
                    case CancelRoadCommand cr:
                        ApplyCancelRoad(ref s, cr);
                        break;
                    case NoOpCommand:
                        break;
                    default: break;
                }
            }
        }

        // 2) Tick systems in fixed order.
        //    Movement → Combat → CityCapture → FortConstruction →
        //    RoadConstruction → Production → PowerProjection →
        //    SupplyLines → Healing → Maintenance → WinConditions
        Movement.Tick(ref s);
        Combat.Tick(ref s);
        CityCapture.Tick(ref s);
        FortConstruction.Tick(ref s);
        RoadConstruction.Tick(ref s);
        Production.Tick(ref s);
        PowerProjection.Tick(ref s);
        SupplyLines.Tick(ref s);
        Healing.Tick(ref s);
        Maintenance.Tick(ref s);
        WinConditions.Tick(ref s);

        return s;
    }

    public static GameState StepN(GameState s, int n, IReadOnlyList<Command>? commands = null)
    {
        for (int i = 0; i < n; i++)
        {
            s = Step(s, commands);
        }
        return s;
    }

    private static void ApplyMoveUnit(ref GameState s, MoveUnitCommand cmd)
    {
        if ((uint)cmd.UnitId >= (uint)s.Units.Count) return;

        Span<Unit> units = CollectionsMarshal.AsSpan(s.Units);
        ref Unit u = ref units[cmd.UnitId];

        if (!u.IsAlive) return;
        if ((int)u.Owner != cmd.PlayerId) return;
        if (!s.Map.InBounds(cmd.TargetX, cmd.TargetY)) return;

        bool isHeavy = u.Type == UnitType.Heavy;
        var path = Pathfinding.FindPath(s.Map, u.TileX, u.TileY, cmd.TargetX, cmd.TargetY, isHeavy);

        RoadConstruction.CancelForUnit(ref s, cmd.UnitId);
        u.Path.Clear();
        for (int i = 0; i < path.Count; i++) u.Path.Add(path[i]);
        u.ProgressRaw = 0;
    }

    private static void ApplyBuildUnit(ref GameState s, BuildUnitCommand cmd)
    {
        if ((uint)cmd.CityId >= (uint)s.Cities.Count) return;

        Span<City> cities = CollectionsMarshal.AsSpan(s.Cities);
        ref City c = ref cities[cmd.CityId];

        if (c.Owner == PlayerId.None) return;
        if ((int)c.Owner != cmd.PlayerId) return;
        if (s.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile()) return;

        if (cmd.Type is null)
        {
            c.ProductionOrder = 0;
            c.ProductionProgress = FP.Zero;
            return;
        }

        byte want = (byte)((byte)cmd.Type.Value + 1);
        if (c.ProductionOrder != want)
        {
            c.ProductionOrder = want;
            c.ProductionProgress = FP.Zero;
        }
    }

    private static void ApplyBuildFort(ref GameState s, BuildFortCommand cmd)
    {
        var owner = (PlayerId)cmd.PlayerId;
        if (owner == PlayerId.None) return;
        if (!s.Map.InBounds(cmd.TargetX, cmd.TargetY)) return;

        // Tile must be Plains.
        TileType tile = s.Map.GetTileUnchecked(cmd.TargetX, cmd.TargetY);
        if (tile != TileType.Plains) return;

        // Tile must be in player's territory.
        int tileIdx = cmd.TargetY * s.Map.Width + cmd.TargetX;
        if (s.TileOwner is null || tileIdx >= s.TileOwner.Length) return;
        if ((PlayerId)s.TileOwner[tileIdx] != owner) return;

        // Fort cap: completed + pending must not exceed max.
        int total = FortConstruction.CountPlayerForts(s, owner)
                  + FortConstruction.CountPendingForts(s, owner);
        if (total >= FortConstruction.MaxFortsPerPlayer) return;

        // Check if there's already a pending fort on this tile.
        if (s.PendingForts is not null)
        {
            for (int i = 0; i < s.PendingForts.Count; i++)
            {
                if (s.PendingForts[i].TileX == cmd.TargetX
                    && s.PendingForts[i].TileY == cmd.TargetY)
                    return;
            }
        }

        // Deduct ECO.
        ref Player p = ref s.Players[(int)owner];
        FP cost = FP.FromInt(FortConstruction.FortEcoCost);
        if (p.Eco < cost) return;
        p.Eco -= cost;

        // Queue the fort construction.
        s.PendingForts ??= new List<FortOrder>();
        s.PendingForts.Add(FortOrder.Create(cmd.TargetX, cmd.TargetY, owner,
                                             FortConstruction.FortBuildTicks));
    }

    private static void ApplyRazeFort(ref GameState s, RazeFortCommand cmd)
    {
        var owner = (PlayerId)cmd.PlayerId;
        if (owner == PlayerId.None) return;
        if (!s.Map.InBounds(cmd.TargetX, cmd.TargetY)) return;

        // Tile must be a Fort.
        TileType tile = s.Map.GetTileUnchecked(cmd.TargetX, cmd.TargetY);
        if (tile != TileType.Fort) return;

        // Find the city entry for this fort and verify ownership.
        Span<City> cities = CollectionsMarshal.AsSpan(s.Cities);
        int fortCityIdx = -1;
        for (int i = 0; i < cities.Length; i++)
        {
            ref City c = ref cities[i];
            if (c.TileX == cmd.TargetX && c.TileY == cmd.TargetY && c.Owner == owner)
            {
                fortCityIdx = i;
                break;
            }
        }
        if (fortCityIdx < 0) return;

        // Revert tile to Plains and remove the city entry.
        s.Map.SetTile(cmd.TargetX, cmd.TargetY, TileType.Plains);
        s.Cities.RemoveAt(fortCityIdx);

        // Re-index city Ids to maintain the id == index invariant.
        // This is O(n) but forts are rare (max 3 per player, max 6 total).
        for (int i = fortCityIdx; i < s.Cities.Count; i++)
        {
            var c = s.Cities[i];
            c.Id = i;
            s.Cities[i] = c;
        }
    }

    private static void ApplyBuildRoad(ref GameState s, BuildRoadCommand cmd)
    {
        var owner = (PlayerId)cmd.PlayerId;
        if (owner == PlayerId.None) return;
        if ((uint)cmd.UnitId >= (uint)s.Units.Count) return;
        if (!s.Map.InBounds(cmd.TargetX, cmd.TargetY)) return;

        Unit u = s.Units[cmd.UnitId];
        if (!u.IsAlive) return;
        if (u.Owner != owner) return;
        if (u.Path is { Count: > 0 }) return;

        List<int> path = Pathfinding.FindRoadBuildPath(s.Map, u.TileX, u.TileY, cmd.TargetX, cmd.TargetY);
        if (path.Count == 0) return;

        RoadConstruction.CancelForUnit(ref s, cmd.UnitId);
        s.PendingRoads ??= new List<RoadOrder>();
        s.PendingRoads.Add(RoadOrder.Create(cmd.UnitId, owner, cmd.TargetX, cmd.TargetY, path));
    }

    private static void ApplyCancelRoad(ref GameState s, CancelRoadCommand cmd)
    {
        var owner = (PlayerId)cmd.PlayerId;
        if (owner == PlayerId.None) return;
        if ((uint)cmd.UnitId >= (uint)s.Units.Count) return;
        if (s.Units[cmd.UnitId].Owner != owner) return;
        RoadConstruction.CancelForUnit(ref s, cmd.UnitId);
    }
}
