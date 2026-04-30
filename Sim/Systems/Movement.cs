using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Per-tick movement integration. For each living unit with a non-empty
// path, advance progress along the current edge by
//   baseSpeed(unitType) * terrainFactor(targetTile, unitType)
// (both in FP). When progress crosses 1.0 the unit's anchor tile advances
// to Path[0], that entry is dequeued, and the overflow carries into the
// next edge — so a move command issued for a faraway target keeps moving
// without stalling at tile boundaries.
//
// Why CollectionsMarshal.AsSpan: List<Unit>.this[i] returns a *copy* of the
// struct; we need ref access to mutate in place without re-writing the
// element back. This is safe as long as the list is not resized during
// iteration (we never add or remove units inside Tick).
public static class Movement
{
    public static void Tick(ref GameState s)
    {
        Span<Unit> units = CollectionsMarshal.AsSpan(s.Units);
        for (int i = 0; i < units.Length; i++)
        {
            ref Unit u = ref units[i];
            if (!u.IsAlive) continue;
            if (u.Path is null || u.Path.Count == 0) continue;

            int nextFlat = u.Path[0];
            int nx = nextFlat % s.Map.Width;
            int ny = nextFlat / s.Map.Width;
            TileType nextTile = s.Map.GetTileUnchecked(nx, ny);
            bool isHeavy = u.Type == UnitType.Heavy;

            // Defense in depth: if the path was queued before the terrain
            // became impassable for this unit, drop the path. (Phase 1
            // terrain is immutable; this guards Phase 3+ where buildings
            // and ownership flips can shift passability.)
            if (!nextTile.IsPassable(isHeavy))
            {
                u.Path.Clear();
                u.ProgressRaw = 0;
                continue;
            }

            FP baseSpeed = UnitStats.TilesPerTick(u.Type);
            FP terrainFactor = FP.FromRaw(nextTile.SpeedFactorRaw(isHeavy));
            FP advance = baseSpeed * terrainFactor;

            FP newProgress = FP.FromRaw(u.ProgressRaw) + advance;

            // Step across as many tile boundaries as `advance` covers (in
            // practice almost always at most one, but a unit with a temp
            // doctrine speed buff could plausibly cross 1.5+ in a tick).
            while (newProgress >= FP.One && u.Path.Count > 0)
            {
                int flat = u.Path[0];
                u.TileX = flat % s.Map.Width;
                u.TileY = flat / s.Map.Width;
                u.Path.RemoveAt(0);
                newProgress = newProgress - FP.One;

                // Re-evaluate next edge's terrain factor only if we're
                // continuing to walk. If we landed at the destination, just
                // park here.
                if (u.Path.Count == 0) break;
                int nf = u.Path[0];
                TileType nt = s.Map.GetTileUnchecked(nf % s.Map.Width, nf / s.Map.Width);
                if (!nt.IsPassable(isHeavy))
                {
                    u.Path.Clear();
                    newProgress = FP.Zero;
                    break;
                }
            }

            u.ProgressRaw = u.Path.Count == 0 ? 0 : newProgress.Raw;
        }
    }
}
