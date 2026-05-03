using System.Collections.Generic;
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
// Movement state:
//   - TileX, TileY = the tile the unit is *anchored* in (where it sits when
//     stationary, where its hitbox tests against, where supply lines see it).
//   - Path = upcoming tile indices to traverse. Empty = stationary. The
//     first entry is the unit's *next* tile.
//   - ProgressRaw = FP raw value in [0, OneRaw) representing progress along
//     the edge from the anchor tile to Path[0]. When it crosses OneRaw, the
//     unit's anchor advances to Path[0], the entry is dequeued, and the
//     overflow carries into the next edge (so step sizes > 1 tile per tick
//     work correctly without dropping movement).
public struct Unit
{
    public int Id;
    public PlayerId Owner;
    public UnitType Type;
    public int TileX;
    public int TileY;
    public FP Hp;
    public long XpRaw;
    public byte Rank;
    public byte PromotionPoints;
    public uint PerkMask;
    public List<int> Path;     // never null after Create; empty when idle
    public long ProgressRaw;   // FP raw; reset to 0 when Path becomes empty

    public bool IsAlive => Hp > FP.Zero;
    public bool IsMoving => Path is { Count: > 0 };

    // Factory keeps Path allocation in one place; tests and command handlers
    // both go through it so we never accidentally ship a unit with a null
    // Path (which would NRE in the Movement system).
    public static Unit Create(int id, PlayerId owner, UnitType type, int x, int y)
    {
        return new Unit
        {
            Id = id,
            Owner = owner,
            Type = type,
            TileX = x,
            TileY = y,
            Hp = UnitStats.MaxHp(type),
            XpRaw = 0,
            Rank = 1,
            PromotionPoints = 0,
            PerkMask = 0,
            Path = new List<int>(),
            ProgressRaw = 0,
        };
    }
}
