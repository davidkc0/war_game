namespace WarGame.Sim.Commands;

// Commands are the only way input enters the simulation. Inputs come from
// human players, the AI, or replay files; they are all the same type to the
// sim. This is the foundation of lockstep netcode and replay: a recorded
// (seed + initial state + ordered command list) reproduces a match exactly.
public abstract record Command
{
    // Player who issued the command. Sim layer uses this for ownership
    // validation (Phase 5 lockstep cannot trust clients to only send
    // commands for units they own — validate at the sim).
    public int PlayerId { get; init; }
}

// Sentinel for the empty-tick path. Real commands extend Command directly.
public sealed record NoOpCommand : Command;

// Order a unit to walk to a target tile. The sim recomputes the path
// deterministically; clients send only intent (target), not the path itself.
// If the target is unreachable or the unit is dead/owned by another player,
// the command is silently dropped — *not* an error, because in MP the
// command may have been issued during a stale snapshot.
public sealed record MoveUnitCommand(int UnitId, int TargetX, int TargetY) : Command;

// Set or change a city's production order. Type=null cancels the current
// order (refunds nothing — Phase 1 keeps it simple). Issuing a new order
// while one is in flight resets ProductionProgress to zero.
public sealed record BuildUnitCommand(int CityId, WarGame.Sim.State.UnitType? Type) : Command;

// Toggle repeat production for an owned real city/capital. Type=null turns
// autobuild off; otherwise the city restarts that unit whenever its queue is
// empty.
public sealed record SetAutoBuildCommand(int CityId, WarGame.Sim.State.UnitType? Type) : Command;

// Upgrade an owned real city/capital by one development level. Forts cannot
// upgrade. Upgrade progress drains ECO through Production.Tick.
public sealed record UpgradeCityCommand(int CityId) : Command;

// Cancel the active upgrade order for one owned real city/capital.
public sealed record CancelCityUpgradeCommand(int CityId) : Command;

// Rename an owned real city/capital. The sim sanitizes the text and stores
// only deterministic ASCII-visible names.
public sealed record RenameCityCommand(int CityId, string Name) : Command;

// Build a fort at the specified tile. Validation in GameSim:
//   - Tile must be Plains (can't build on water/mountain/city/existing fort)
//   - Tile must be in player's territory (TileOwner check)
//   - Player must have enough ECO (FortEcoCost)
//   - Player must not exceed max fort count (MaxFortsPerPlayer)
public sealed record BuildFortCommand(int TargetX, int TargetY) : Command;

// Raze (destroy) a completed fort that the player owns. Reverts the tile
// back to Plains and removes the fort from the game. Unlike cities, forts
// are not permanent — they can be torn down strategically to free up the
// fort cap or deny the enemy a captured position.
public sealed record RazeFortCommand(int TargetX, int TargetY) : Command;

// Assign one unit to build a road/bridge route to a target tile. The sim
// computes and stores the engineering path; clients send only intent.
public sealed record BuildRoadCommand(int UnitId, int TargetX, int TargetY) : Command;

// Cancel the active road/bridge construction order for one unit.
public sealed record CancelRoadCommand(int UnitId) : Command;

// Spend one promotion point on a valid perk for a living owned unit.
public sealed record ChoosePromotionCommand(int UnitId, byte PerkId) : Command;
