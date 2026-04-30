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
