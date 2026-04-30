using System.Collections.Generic;
using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Per-tick combat resolution. A unit "engages" any enemy unit on the same
// tile or 4-connected adjacent tile (Phase 1 grid-RTS convention; Phase 3
// might revisit ranged units but they are out of v1 scope per PLAN.md §8).
//
// Each living unit picks one engaged enemy and deals DamagePerTick to it.
// Tie-break: enemy with the lowest Id. This is deterministic without any
// extra sorting because units are stored in Id order in the list.
//
// Damage is applied in two passes (read-only scan, then mutating apply) so
// that all damage in a given tick is computed against the *start-of-tick*
// state. Without this, the order of iteration would influence outcomes
// (unit 0 kills unit 1 before unit 1 gets to swing back), which is a real
// gameplay decision that PLAN.md doesn't pin down. Two-pass is the simpler,
// fairer default.
public static class Combat
{
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
                // First match wins because the list is iterated in Id order
                // and we never break — but in case of multi-engage the
                // *lowest Id* enemy is selected by virtue of j ascending.
                targetId = j;
                break;
            }
            targets[i] = targetId;
        }

        // Pass 2: apply damage. We mutate via a span so dead-this-tick
        // units stay readable to the next loop iteration (the IsAlive check
        // is on the post-mutation view, so a unit can deal a final blow
        // before dying — matches the standard "simultaneous swing" feel).
        Span<Unit> units = CollectionsMarshal.AsSpan(s.Units);
        for (int i = 0; i < units.Length; i++)
        {
            int targetId = targets[i];
            if (targetId < 0) continue;
            Unit attacker = units[i];
            // Re-check liveness in case a prior attacker in this tick
            // already killed this attacker (the swap would not have
            // happened in pass 1 since pass 1 read the start-of-tick state,
            // but two-pass also means *every* swing lands as long as the
            // attacker was alive at start-of-tick). We allow one final
            // swing from the dying unit by skipping this guard.
            FP dmg = UnitStats.DamagePerTick(attacker.Type);
            ref Unit target = ref units[targetId];
            target.Hp -= dmg;
            // Dead units retain their slot (preserves Id stability) but
            // stop participating in subsequent ticks.
        }

        // Pass 3: clean up dead units' transient state so they don't
        // continue showing as "moving" or holding a stale path.
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
