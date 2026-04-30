using WarGame.Sim.Math;

namespace WarGame.Sim.State;

// Per-player resource state. Held as a small array on GameState (length is
// the number of players + 1 — index 0 is reserved for PlayerId.None and is
// always default-initialized so accidental "owner=None" reads do not throw).
//
// Doctrine slot is a Phase 3 field; for Phase 1 it stays at the default
// (None) and doctrine effects are not applied.
public struct Player
{
    public PlayerId Id;
    public FP Eco;
    // Phase 3 will introduce a Doctrine enum and stat-modifier resolution.
    // Reserving the slot now means the schema bump happens once, here.
    public byte DoctrineId;
}
