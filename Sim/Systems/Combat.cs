using System;
using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Per-tick combat resolution. A unit "engages" any enemy unit on the same
// tile or 4-connected adjacent tile (Phase 1 grid-RTS convention).
//
// Each living unit picks one engaged enemy and deals DamagePerTick to it.
// Tie-break: enemy with the lowest Id. This is deterministic without any
// extra sorting because units are stored in Id order in the list.
//
// Three damage modifiers stack multiplicatively:
//   1. CONCENTRATION-OF-FORCE: +15% per additional friendly unit engaged
//      with the same target.
//   2. TERRAIN DEFENSE: the *defender's* tile type applies a damage
//      reduction (e.g., forest = 0.70×, fort = 0.45×).
//   3. ATTACKER PENALTY: a unit that is actively moving (has a non-empty
//      Path) deals 15% less damage. Rewards holding position.
//
// Damage is applied in two passes (read-only scan, then mutating apply) so
// that all damage in a given tick is computed against the *start-of-tick*
// state. Without this, the order of iteration would influence outcomes
// (unit 0 kills unit 1 before unit 1 gets to swing back). Two-pass is the
// simpler, fairer default.
public static class Combat
{
    // +15% damage per additional friendly unit attacking the same target.
    private static readonly FP ConcentrationBonusPerAlly = FP.FromInt(15) / FP.FromInt(100);
    // Moving attackers deal 15% less damage.
    private static readonly FP MovingAttackerPenalty = FP.FromInt(85) / FP.FromInt(100);

    public static void Tick(ref GameState s)
    {
        // Pass 1: each unit chooses a target and notes the damage it will
        // inflict. Indices into s.Units; -1 means "no target".
        int n = s.Units.Count;
        if (n == 0) return;

        Span<int> targets = n <= 256 ? stackalloc int[n] : new int[n];
        for (int i = 0; i < n; i++) targets[i] = -1;

        for (int i = 0; i < n; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive) continue;
            int targetId = -1;
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                Unit e = s.Units[j];
                if (!e.IsAlive) continue;
                if (e.Owner == u.Owner) continue;
                if (!IsAdjacentOrSame(u.TileX, u.TileY, e.TileX, e.TileY)) continue;
                targetId = j;
                break;
            }
            targets[i] = targetId;
        }

        // Pass 1b: count how many friendly units share the same target.
        Span<int> targetCount = n <= 256 ? stackalloc int[n] : new int[n];
        for (int i = 0; i < n; i++)
        {
            int tid = targets[i];
            if (tid >= 0) targetCount[tid]++;
        }

        // Pass 2: apply damage with all three modifiers.
        Span<Unit> units = CollectionsMarshal.AsSpan(s.Units);
        for (int i = 0; i < units.Length; i++)
        {
            int targetId = targets[i];
            if (targetId < 0) continue;
            ref Unit attacker = ref units[i];
            ref Unit target = ref units[targetId];

            FP baseDmg = UnitStats.DamagePerTick(attacker.Type);

            // 1) Concentration-of-force bonus.
            int allies = targetCount[targetId];
            FP multiplier = FP.One + ConcentrationBonusPerAlly * FP.FromInt(allies - 1);
            FP dmg = baseDmg * multiplier;

            // 2) Attacker penalty: moving units deal less damage.
            if (attacker.IsMoving)
                dmg = dmg * MovingAttackerPenalty;

            // 3) Terrain defense: the defender's tile reduces incoming damage.
            TileType defenderTile = s.Map.GetTileUnchecked(target.TileX, target.TileY);
            FP defenseMul = FP.FromRaw(defenderTile.DefenseMultiplierRaw());
            dmg = dmg * defenseMul;

            target.Hp -= dmg;
        }

        // Pass 3: clean up dead units' transient state.
        for (int i = 0; i < units.Length; i++)
        {
            ref Unit u = ref units[i];
            if (u.IsAlive) continue;
            if (u.Path is not null && u.Path.Count > 0) u.Path.Clear();
            u.ProgressRaw = 0;
        }
    }

    private static bool IsAdjacentOrSame(int ax, int ay, int bx, int by)
    {
        int dx = ax - bx; if (dx < 0) dx = -dx;
        int dy = ay - by; if (dy < 0) dy = -dy;
        return (dx + dy) <= 1;
    }
}
