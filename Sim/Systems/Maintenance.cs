using System;
using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Unit maintenance cost. Every living unit that is NOT currently stationed
// on a friendly city tile drains ECO from its owner each tick. If the
// owner's ECO reaches zero, unsheltered units take starvation damage.
//
// This prevents late-game army snowballing: large armies are expensive to
// maintain, forcing players to choose between army size and economy.
// Retreating wounded units to cities is doubly rewarded: they heal AND
// stop costing maintenance.
//
// Tuning (Phase 1.5, will iterate):
//   Maintenance per tick per unit: 0.02 ECO (0.6 ECO/sec)
//     → A 10-unit army costs 6 ECO/sec. A single city produces 1 ECO/sec,
//       capital produces 3 ECO/sec. So 10 units need ~2 cities to sustain.
//   Starvation damage per tick:    same as UnitStats.StarvationDamagePerTick
//     → A starved light unit dies in ~10 seconds.
//
// Units on a friendly city tile are "sheltered" — no maintenance cost.
// This creates a tactical decision: garrison (free) vs. field (costly).
public static class Maintenance
{
    public static readonly FP MaintenancePerTick = FP.One / FP.FromInt(50);  // 0.02 ECO/tick
    public static readonly FP RoadSupplyMultiplier = FP.Half;                // 50% cost
    public static readonly FP CutOffMultiplier = FP.FromRaw(FP.OneRaw * 3 / 2); // 150% cost

    public static void Tick(ref GameState s)
    {
        if (s.Units.Count == 0) return;

        // Pre-build a set of friendly city tiles per player so we don't
        // O(units * cities) every tick. With max ~20 cities this is fine
        // as a linear scan, but grouping by player avoids redundant checks.
        // We use a flat array: player * maxCities, with -1 sentinel.
        int cityCount = s.Cities.Count;

        Span<Unit> units = CollectionsMarshal.AsSpan(s.Units);
        for (int i = 0; i < units.Length; i++)
        {
            ref Unit u = ref units[i];
            if (!u.IsAlive) continue;
            if (u.Owner == PlayerId.None) continue;

            // Check if unit is sheltered (on a friendly city/fort tile).
            bool sheltered = false;
            for (int j = 0; j < cityCount; j++)
            {
                City c = s.Cities[j];
                if (c.Owner != u.Owner) continue;
                if (c.TileX == u.TileX && c.TileY == u.TileY)
                {
                    int cityIdx = u.TileY * s.Map.Width + u.TileX;
                    sheltered = s.TileOwner != null && cityIdx < s.TileOwner.Length
                                && (PlayerId)s.TileOwner[cityIdx] == u.Owner;
                    break;
                }
            }
            if (sheltered) continue;

            // Also consider forts as shelter — units on a friendly fort
            // are also exempt from maintenance.
            TileType tile = s.Map.GetTileUnchecked(u.TileX, u.TileY);
            if (tile == TileType.Fort)
            {
                // Check if the fort's tile is in friendly territory.
                int tileIdx = u.TileY * s.Map.Width + u.TileX;
                if (s.TileOwner != null && tileIdx < s.TileOwner.Length
                    && (PlayerId)s.TileOwner[tileIdx] == u.Owner)
                {
                    sheltered = true;
                }
            }
            if (sheltered) continue;

            SupplyStatus supply = SupplyLines.GetUnitStatus(s, i);
            FP maintenance = MaintenancePerTick;
            if (supply == SupplyStatus.RoadSupplied)
                maintenance = maintenance * RoadSupplyMultiplier;
            else if (supply is SupplyStatus.CutOff or SupplyStatus.None)
                maintenance = maintenance * CutOffMultiplier;

            // Drain maintenance cost from owner's ECO.
            ref Player p = ref s.Players[(int)u.Owner];
            if (p.Eco >= maintenance)
            {
                p.Eco -= maintenance;
            }
            else
            {
                // No ECO left — unit takes starvation damage.
                p.Eco = FP.Zero;
                u.Hp -= UnitStats.StarvationDamagePerTick;
                // Clean up if dead.
                if (!u.IsAlive)
                {
                    if (u.Path is not null && u.Path.Count > 0) u.Path.Clear();
                    u.ProgressRaw = 0;
                }
            }
        }
    }
}
