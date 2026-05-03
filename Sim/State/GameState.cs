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
//   5 — Phase 1 step 6: + TileOwner (derived; cached for renderer + encirclement).
//   7 — Phase 1 step 11: + City.CaptureHp (Advance Wars-style capture).
//   8 — Phase 1.5: + PendingForts, Fort tile type, terrain defense, maintenance.
//   9 — Phase 3a: + supply state, Bridge tile type, PendingRoads.
//   10 — Phase 3b: + per-player fog of war visibility and last-seen memory.
public struct GameState
{
    public const int CurrentVersion = 10;

    public int Version;
    public int Tick;
    public SimRng Rng;

    public MapState Map;
    public List<Unit> Units;     // index in list == Unit.Id; never reordered
    public List<City> Cities;    // index in list == City.Id; never reordered
    public Player[] Players;     // length 3: indices [None, Player1, Player2]

    // Per-tile owner derived from the power-projection field. Length == w*h.
    // This is *technically* derived state (recomputable from Units+Cities),
    // but storing it lets renderers and encirclement detection read directly
    // without redoing the field math. Always rewritten by PowerProjection
    // each tick — never authored elsewhere.
    public byte[] TileOwner;

    // Per-tile supply ownership derived by SupplyLines. TileSupplyOwner
    // marks normal friendly-territory supply. TileRoadSupplyOwner marks
    // road/bridge-assisted supply, including routes outside friendly
    // territory when not physically blocked by enemy units.
    public byte[] TileSupplyOwner;
    public byte[] TileRoadSupplyOwner;

    // Per-unit supply status, indexed by Unit.Id.
    public byte[] UnitSupplyStatus;

    // Per-player fog-of-war state, flattened as playerIndex * tileCount + tileIndex.
    // Visibility is recomputed each tick, while LastSeen* is updated only when
    // the tile is visible to that player.
    public byte[] TileVisibility;
    public byte[] LastSeenTileType;
    public byte[] LastSeenTileOwner;

    // Once a player wins, this is set to that PlayerId and the sim freezes
    // (commands and system ticks are skipped). PlayerId.None = game in
    // progress. Set exclusively by WinConditions system.
    public PlayerId Winner;

    // Per-player counter of consecutive ticks at >= 80% city ownership.
    // Resets to zero whenever the ratio drops below threshold. Length 3:
    // indices [None (unused), Player1, Player2]. Triggers victory when
    // any slot reaches 30 * TicksPerSecond.
    public int[] CityHoldTicks;

    // Fort construction orders in progress. Each entry tracks a tile being
    // converted to a Fort. Removed when complete or cancelled (territory lost).
    public List<FortOrder> PendingForts;

    // Road/bridge construction orders in progress. One active order per
    // unit; cancelled by manual movement, death, or explicit command.
    public List<RoadOrder> PendingRoads;

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
            // 1x1 default; PowerProjection.Tick allocates the right size on
            // first tick when the map width/height changes.
            TileOwner = new byte[1],
            TileSupplyOwner = new byte[1],
            TileRoadSupplyOwner = new byte[1],
            UnitSupplyStatus = System.Array.Empty<byte>(),
            TileVisibility = new byte[3],
            LastSeenTileType = new byte[3],
            LastSeenTileOwner = new byte[3],
            Winner = PlayerId.None,
            CityHoldTicks = new int[3],
            PendingForts = new List<FortOrder>(),
            PendingRoads = new List<RoadOrder>(),
        };
    }
}
