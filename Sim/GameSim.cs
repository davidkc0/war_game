using System.Collections.Generic;
using WarGame.Sim.Commands;
using WarGame.Sim.State;

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
        // Phase 0: ignore commands, advance the dot. Real command dispatch
        // arrives in Phase 1 once we have unit/city state.
        _ = commands;

        s.Tick += 1;
        s.DotPos += s.DotVel;
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
}
