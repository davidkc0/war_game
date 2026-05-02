using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Encirclement detection. A unit is "encircled" when:
//   - the tile under it is NOT owned by its own player, AND
//   - there is no 4-connected path through friendly-or-own tiles from the
//     unit's tile back to any city owned by its player.
//
// Implementation: per player, BFS from each owned city through friendly
// tiles. Any unit whose tile is not reached AND whose tile isn't already
// friendly is destroyed.
//
// Determinism notes:
//   - reachability per tile is a bool[], indexed by flat tile index — no
//     Dictionary, no HashSet,
//   - BFS uses a Queue<int>, which is FIFO and order-stable,
//   - cities and units are iterated in Id order.
//
// Phase 3a's full supply-line system extends this with starvation; for now
// the rule is binary: in pocket = die. (Phase 3a will likely make pocket
// units suffer HP loss instead, giving the encircling player time to mop
// up vs. the encircled player time to rescue. PLAN.md §3.)
public static class Encirclement
{
    public static void Tick(ref GameState s)
    {
        if (s.Units.Count == 0) return;
        if (s.Map.Width == 0 || s.Map.Height == 0) return;

        int w = s.Map.Width, h = s.Map.Height, n = w * h;

        // Per-player reachability buffers. Phase 1 has 2 real players + None
        // sentinel. We allocate a 2D layout (player slot * n) so the BFS for
        // each player writes into its own slice.
        int playerCount = s.Players.Length;
        bool[] reachable = new bool[playerCount * n];
        var queue = new Queue<int>();

        for (int pid = 0; pid < playerCount; pid++)
        {
            if (pid == (int)PlayerId.None) continue;
            queue.Clear();
            // Seed from every city owned by this player.
            for (int i = 0; i < s.Cities.Count; i++)
            {
                City c = s.Cities[i];
                if ((int)c.Owner != pid) continue;
                int idx = c.TileY * w + c.TileX;
                int slot = pid * n + idx;
                if (reachable[slot]) continue;
                reachable[slot] = true;
                queue.Enqueue(idx);
            }

            // Standard 4-connected BFS through tiles whose TileOwner is
            // either this player OR none (contested/no-man's-land does not
            // by itself break a supply path; PLAN.md §3 only lists *cut*
            // ownership as the breaking condition. Phase 3a will likely
            // tighten this to "must be your own territory only").
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % w, cy = cur / w;
                Span<int> dx = stackalloc int[] { 0, 1, 0, -1 };
                Span<int> dy = stackalloc int[] { -1, 0, 1, 0 };
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + dx[k], ny = cy + dy[k];
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                    int nIdx = ny * w + nx;
                    int nSlot = pid * n + nIdx;
                    if (reachable[nSlot]) continue;
                    int tileOwner = s.TileOwner[nIdx];
                    if (tileOwner != pid && tileOwner != (int)PlayerId.None) continue;
                    reachable[nSlot] = true;
                    queue.Enqueue(nIdx);
                }
            }
        }

        // Apply: any living unit is encircled (and destroyed) only when
        // BOTH its own tile AND every 4-connected neighbor are unreachable
        // from one of its cities. This is intentionally more lenient than
        // "your tile must be friendly" — a unit pushed one square into
        // enemy lines but with friendly ground at its back survives. To
        // kill it, you must fully surround.
        Span<Unit> units = CollectionsMarshal.AsSpan(s.Units);
        Span<int> dxK = stackalloc int[] { 0, 1, 0, -1 };
        Span<int> dyK = stackalloc int[] { -1, 0, 1, 0 };

        for (int i = 0; i < units.Length; i++)
        {
            ref Unit u = ref units[i];
            if (!u.IsAlive) continue;
            if (u.Owner == PlayerId.None) continue;

            int playerSlotBase = (int)u.Owner * n;
            int idx = u.TileY * w + u.TileX;
            bool anyReachable = reachable[playerSlotBase + idx];
            if (!anyReachable)
            {
                for (int k = 0; k < 4 && !anyReachable; k++)
                {
                    int nx = u.TileX + dxK[k], ny = u.TileY + dyK[k];
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                    if (reachable[playerSlotBase + ny * w + nx]) anyReachable = true;
                }
            }
            if (anyReachable) continue;

            u.Hp = FP.Zero;
            if (u.Path is not null) u.Path.Clear();
            u.ProgressRaw = 0;
        }
    }
}
