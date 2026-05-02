using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Advance Wars-style city capture. Each tick, enemy units on or adjacent
// to a city deplete its CaptureHp. When CaptureHp reaches 0, the city
// flips to the attacker with the most units nearby. If no enemies are
// present, CaptureHp regenerates slowly toward MaxCaptureHp.
//
// This replaces the old power-projection-based capture that was confusing
// because it relied on abstract influence fields rather than direct unit
// presence. The new system is immediately intuitive: move your army to a
// city, watch the HP bar deplete, city flips.
//
// Tick order: runs after Combat (so dead units don't contribute) and
// before WinConditions (so a freshly-captured capital triggers game over
// in the same tick).
//
// Determinism:
//   - Units iterated in Id order (list index).
//   - Per-city attacker counts use a fixed-size array (PlayerId-indexed).
//   - No dictionaries or hash sets.
public static class CityCapture
{
    public static void Tick(ref GameState s)
    {
        if (s.Cities.Count == 0 || s.Units.Count == 0) return;

        Span<City> cities = CollectionsMarshal.AsSpan(s.Cities);
        // Per-player attacker counts, reused each city iteration.
        Span<int> perPlayer = stackalloc int[3];

        for (int ci = 0; ci < cities.Length; ci++)
        {
            ref City c = ref cities[ci];
            if (c.Owner == PlayerId.None) continue;

            // Count enemy units on-tile and adjacent, tallied per player.
            // Index 0 = None (unused), 1 = Player1, 2 = Player2.
            int enemyOnTile = 0;
            int enemyAdjacent = 0;
            PlayerId dominantAttacker = PlayerId.None;
            int dominantAttackerCount = 0;
            perPlayer.Clear();

            for (int ui = 0; ui < s.Units.Count; ui++)
            {
                Unit u = s.Units[ui];
                if (!u.IsAlive) continue;
                if (u.Owner == c.Owner) continue;   // friendly — skip
                if (u.Owner == PlayerId.None) continue;

                int dx = u.TileX - c.TileX;
                int dy = u.TileY - c.TileY;
                if (dx < 0) dx = -dx;
                if (dy < 0) dy = -dy;
                int dist = dx + dy;

                if (dist == 0)
                {
                    enemyOnTile++;
                    perPlayer[(int)u.Owner]++;
                }
                else if (dist == 1)
                {
                    enemyAdjacent++;
                    perPlayer[(int)u.Owner]++;
                }
            }

            // Determine the dominant attacker (most units near this city).
            for (int p = 1; p <= 2; p++)
            {
                if (perPlayer[p] > dominantAttackerCount)
                {
                    dominantAttackerCount = perPlayer[p];
                    dominantAttacker = (PlayerId)p;
                }
            }

            if (enemyOnTile + enemyAdjacent > 0)
            {
                // Deal capture damage.
                int damage = enemyOnTile * City.CaptureDamageOnTile
                           + enemyAdjacent * City.CaptureDamageAdjacent;
                c.CaptureHp -= damage;

                if (c.CaptureHp <= 0 && dominantAttacker != PlayerId.None)
                {
                    // City flips! Cancel production and reset capture HP.
                    c.Owner = dominantAttacker;
                    c.ProductionOrder = 0;
                    c.ProductionProgress = FP.Zero;
                    c.CaptureHp = c.MaxCaptureHp;
                }
            }
            else
            {
                // No enemies nearby — regenerate capture HP.
                int max = c.MaxCaptureHp;
                if (c.CaptureHp < max)
                {
                    c.CaptureHp += City.CaptureRegenPerTick;
                    if (c.CaptureHp > max) c.CaptureHp = max;
                }
            }
        }
    }
}
