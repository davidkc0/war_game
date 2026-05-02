using System;
using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// WoD-style territory control. Each tick we recompute, for every tile, the
// total influence of each player. Tile ownership = whichever player has the
// strictly higher total. Ties are PlayerId.None (contested no-man's-land).
//
// Influence sources stamp a Manhattan (diamond) falloff onto the field:
//   contribution(d) = base * (radius - d) / radius     (integer math)
//
// Integer arithmetic everywhere — no FP needed for projection. Determinism
// follows from:
//   - sources iterated in deterministic order (Units list, then Cities list,
//     both indexed by Id),
//   - tile iteration is row-major,
//   - additive ints commute and associate, but we add in fixed order anyway
//     to be safe against signed-overflow surprises (none expected at these
//     magnitudes; safety net only).
//
// City capture has a 1-tick lag: a city flips ownership only after the tile
// it sits on is owned by a different player. The flip cancels the in-flight
// production order — the new owner doesn't inherit half-built units.
//
// Phase 3 will likely downsample for 120x120+ maps; PLAN.md §3 calls out
// 8x as the target. Phase 1's 60x60 is small enough to run at full res.
public static class PowerProjection
{
    public const int LightBase   = 10;
    public const int LightRadius = 5;
    public const int HeavyBase   = 15;
    public const int HeavyRadius = 4;
    public const int CityBase    = 40;
    public const int CityRadius  = 8;
    public const int CapitalBase = 80;
    public const int CapitalRadius = 12;
    public const int FortBase    = 25;
    public const int FortRadius  = 6;

    public static void Tick(ref GameState s)
    {
        int w = s.Map.Width, h = s.Map.Height, n = w * h;

        // Per-player influence buffers. Heap allocation is unavoidable for
        // larger maps (>16k tiles can't go on the stack); for Phase 1's
        // 60x60 = 3600 cells the stackalloc path is taken.
        Span<int> p1 = n <= 16384 ? stackalloc int[n] : new int[n];
        Span<int> p2 = n <= 16384 ? stackalloc int[n] : new int[n];
        p1.Clear();
        p2.Clear();

        // Units stamp first (Id order).
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive) continue;
            if (u.Owner == PlayerId.None) continue;
            int baseInf, radius;
            if (u.Type == UnitType.Heavy) { baseInf = HeavyBase; radius = HeavyRadius; }
            else                          { baseInf = LightBase; radius = LightRadius; }
            Span<int> target = u.Owner == PlayerId.Player1 ? p1 : p2;
            Stamp(target, w, h, u.TileX, u.TileY, baseInf, radius);
        }

        // Then cities (and forts, which are tracked as cities on Fort tiles).
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner == PlayerId.None) continue;
            TileType tileTy = s.Map.GetTileUnchecked(c.TileX, c.TileY);
            int baseInf, radius;
            if (c.IsCapital)           { baseInf = CapitalBase; radius = CapitalRadius; }
            else if (tileTy.IsFortTile()) { baseInf = FortBase;    radius = FortRadius; }
            else                       { baseInf = CityBase;    radius = CityRadius; }
            Span<int> target = c.Owner == PlayerId.Player1 ? p1 : p2;
            Stamp(target, w, h, c.TileX, c.TileY, baseInf, radius);
        }

        // Derive per-tile ownership.
        if (s.TileOwner is null || s.TileOwner.Length != n)
            s.TileOwner = new byte[n];
        Span<byte> owner = s.TileOwner.AsSpan();
        for (int i = 0; i < n; i++)
        {
            int a = p1[i], b = p2[i];
            owner[i] = (byte)(a > b ? PlayerId.Player1
                            : b > a ? PlayerId.Player2
                            :         PlayerId.None);
        }

        // City capture is now handled by CityCapture.cs (Advance Wars-style
        // HP depletion) rather than power-projection-based flipping.
        // PowerProjection still computes TileOwner for territory tinting
        // and border rendering — that's render-only, not gameplay.
    }

    /// <summary>
    /// Add a Manhattan-radius diamond stamp of falloff influence to `field`,
    /// centered at (cx, cy). Cells outside the map are clipped.
    /// </summary>
    private static void Stamp(Span<int> field, int w, int h, int cx, int cy, int baseInf, int radius)
    {
        if (radius <= 0) return;
        for (int dy = -radius; dy <= radius; dy++)
        {
            int y = cy + dy;
            if ((uint)y >= (uint)h) continue;
            int absDy = dy < 0 ? -dy : dy;
            int xRange = radius - absDy;
            for (int dx = -xRange; dx <= xRange; dx++)
            {
                int x = cx + dx;
                if ((uint)x >= (uint)w) continue;
                int absDx = dx < 0 ? -dx : dx;
                int d = absDx + absDy;
                // Multiply *before* divide to keep precision. radius>0 above.
                int contribution = baseInf * (radius - d) / radius;
                if (contribution > 0) field[y * w + x] += contribution;
            }
        }
    }
}
