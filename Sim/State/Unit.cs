using WarGame.Sim.Math;

namespace WarGame.Sim.State;

// One unit instance. Stored in GameState.Units, indexed by Id (which equals
// the unit's slot in the list). Dead units are NOT removed — Hp <= 0 marks
// them dead and they are skipped during iteration. This keeps Id stable
// across the lifetime of the game, which is critical because:
//   1. Replay command streams refer to units by Id.
//   2. Lockstep clients must agree on Id assignment.
//   3. Iteration order over a List<Unit> is deterministic; over a
//      Dictionary<int, Unit> it is not.
//
// Step 4 (Movement) adds path/destination fields. Phase 0 step 2 keeps the
// struct minimal — production-ready but not yet wired to movement.
public struct Unit
{
    public int Id;
    public PlayerId Owner;
    public UnitType Type;
    public int TileX;
    public int TileY;
    public FP Hp;

    public bool IsAlive => Hp > FP.Zero;
}
