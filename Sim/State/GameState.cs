using System.Collections.Generic;
using WarGame.Sim.Math;

namespace WarGame.Sim.State;

// Snapshot of the entire simulation at a given tick. This is the *only* state
// that gets fed to GameSim.Step — anything not serializable here cannot
// influence sim outcomes. That guarantee is what enables lockstep netcode and
// replay: a (seed + state + command-stream) tuple fully determines the future.
//
// Bump CurrentVersion whenever the schema changes so we can reject loading
// stale replays/saves.
//
// Schema versions:
//   1 — Phase 0: rng + dot pos/vel only.
//   2 — Phase 1 step 2: + map, units, cities, players (dot retained).
//   3 — Phase 1 step 4: dot removed; movement state added on Unit.
//   4 — Phase 1 step 5: + City.ProductionOrder.
public struct GameState
{
    public const int CurrentVersion = 4;

    public int Version;
    public int Tick;
    public SimRng Rng;

    public MapState Map;
    public List<Unit> Units;     // index in list == Unit.Id; never reordered
    public List<City> Cities;    // index in list == City.Id; never reordered
    public Player[] Players;     // length 3: indices [None, Player1, Player2]

    public static GameState Initial(ulong seed)
    {
        return new GameState
        {
            Version = CurrentVersion,
            Tick = 0,
            Rng = new SimRng(seed),
            // Default empty map. Test maps and Phase 2 procgen produce real
            // maps; tests that don't need terrain leave this 1x1 placeholder.
            Map = new MapState.Builder(1, 1).Build(),
            Units = new List<Unit>(),
            Cities = new List<City>(),
            Players = new Player[]
            {
                new() { Id = PlayerId.None,    Eco = FP.Zero, DoctrineId = 0 },
                new() { Id = PlayerId.Player1, Eco = FP.Zero, DoctrineId = 0 },
                new() { Id = PlayerId.Player2, Eco = FP.Zero, DoctrineId = 0 },
            },
        };
    }
}
