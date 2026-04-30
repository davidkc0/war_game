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
                    case NoOpCommand:
                        break;
                    // Unrecognized commands are silently ignored. That keeps
                    // forward-compat with future command types when older
                    // clients replay newer logs (caller should validate
                    // schema version separately).
                    default: break;
                }
            }
        }

        // 2) Tick systems in fixed order. The order matters: Movement
        //    happens first (so a unit moving onto an enemy can engage this
        //    tick), Combat second (enemies trade damage), Production last
        //    (newly-captured cities contribute to their captor next tick).
        //    PowerProjection / Supply-routing / Borders / Encirclement
        //    arrive in steps 6 & 7.
        Movement.Tick(ref s);
        Combat.Tick(ref s);
        Production.Tick(ref s);

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
        // Owner check — sim layer trust boundary. PLAN.md §5 will lean on
        // this when lockstep clients receive commands from peers.
        if ((int)u.Owner != cmd.PlayerId) return;
        if (!s.Map.InBounds(cmd.TargetX, cmd.TargetY)) return;

        bool isHeavy = u.Type == UnitType.Heavy;
        var path = Pathfinding.FindPath(s.Map, u.TileX, u.TileY, cmd.TargetX, cmd.TargetY, isHeavy);

        // Re-target: discard current path and partial progress. Visual jitter
        // (unit appears to snap back to tile center) is acceptable in v1; if
        // playtesting flags it, Phase 3 can add the "finish current edge,
        // then re-path from the next tile" smoothing.
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

        // Cancel order: clear progress and stop. (Phase 1 doesn't refund.)
        if (cmd.Type is null)
        {
            c.ProductionOrder = 0;
            c.ProductionProgress = FP.Zero;
            return;
        }

        // Re-issuing the same order is a no-op (don't reset progress);
        // switching types resets accumulated progress.
        byte want = (byte)((byte)cmd.Type.Value + 1);
        if (c.ProductionOrder != want)
        {
            c.ProductionOrder = want;
            c.ProductionProgress = FP.Zero;
        }
    }
}
