namespace WarGame.Sim.Commands;

// Commands are the only way input enters the simulation. Inputs come from
// human players, the AI, or replay files; they are all the same type to the
// sim. This is the foundation of lockstep netcode and replay: a recorded
// (seed + initial state + ordered command list) reproduces a match exactly.
//
// Phase 0 has no real commands — the dot moves on its own. We define the
// shape so later phases extend it without restructuring.
public abstract record Command
{
    // Player who issued the command. Phase 0 has no players, but reserving
    // the field now means existing tests do not need rewriting later.
    public int PlayerId { get; init; }
}

// Sentinel — explicitly-empty command list shape used in tests and the empty
// tick path. Real commands (MoveUnits, BuildFortification, etc.) arrive in
// Phase 1+.
public sealed record NoOpCommand : Command;
