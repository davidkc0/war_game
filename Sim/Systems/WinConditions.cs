using System;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Phase 1 PvP win conditions per PLAN.md §3:
//   1. Capture enemy capital (any city with OriginalOwner != current Owner
//      AND IsCapital triggers victory for the current Owner).
//   2. Hold ≥80% of all cities for 30 consecutive seconds (per-player
//      tick counter on GameState.CityHoldTicks).
//
// Disconnect/surrender is a Phase 5 concern (lockstep MP).
//
// This system runs after PowerProjection (which handles capital flips)
// but before Encirclement (so a player who just captured the enemy
// capital instantly wins, regardless of whether they have units left
// in friendly territory).
public static class WinConditions
{
    public const int CitiesHoldThresholdTicks = 30 * GameSim.TicksPerSecond;

    // 0.8 expressed as a ratio comparison: ownedCount * 5 >= totalCount * 4.
    // Avoids any FP arithmetic in the win condition itself.
    public static bool MeetsCityThreshold(int ownedCount, int totalCount)
        => totalCount > 0 && ownedCount * 5 >= totalCount * 4;

    public static void Tick(ref GameState s)
    {
        // 1) Capital-capture rule. Iterate cities in Id order so the first
        //    captured capital we encounter wins the tie (deterministic).
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (!c.IsCapital) continue;
            if (c.OriginalOwner == c.Owner) continue;
            if (c.Owner == PlayerId.None) continue;
            // Captor wins.
            s.Winner = c.Owner;
            return;
        }

        // 2) 80%-cities-for-30-seconds rule. Count city ownership per
        //    player; bump or reset each player's hold counter.
        Span<int> owned = stackalloc int[s.Players.Length];
        int total = 0;
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (s.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile()) continue;
            if (c.Owner == PlayerId.None) continue;
            owned[(int)c.Owner]++;
            total++;
        }

        for (int pid = 0; pid < s.Players.Length; pid++)
        {
            if (pid == (int)PlayerId.None) continue;
            if (MeetsCityThreshold(owned[pid], total))
            {
                s.CityHoldTicks[pid]++;
                if (s.CityHoldTicks[pid] >= CitiesHoldThresholdTicks)
                {
                    s.Winner = (PlayerId)pid;
                    return;
                }
            }
            else
            {
                s.CityHoldTicks[pid] = 0;
            }
        }
    }
}
