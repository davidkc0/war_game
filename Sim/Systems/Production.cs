using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// City economy + unit production. Two responsibilities, both sequenced
// after Combat in the tick order so a city captured this tick contributes
// to its *new* owner starting next tick.
//
//   1. Each owned, living city accrues ECO into its owner's player slot.
//      Capital level 1/2/3 -> 3/4/5 ECO/sec
//      City level 1/2/3    -> 1/2/3 ECO/sec
//
//   2. For each city with a production order, drain ECO from the owner's
//      pool into the city's ProductionProgress. When progress reaches the
//      unit's EcoCost, spawn the unit and reset.
//
// Supply ceiling: a city only drains ECO if its owner is not already at or
// above the global supply cap. This is the *capacity* half of supply; the
// *line-routing* half (Phase 3a per PLAN.md §4) is intentionally deferred.
public static class Production
{
    public static void Tick(ref GameState s)
    {
        // Step 1: ECO accrual.
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner == PlayerId.None) continue;
            if (s.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile()) continue;
            s.Players[(int)c.Owner].Eco += UnitStats.EcoRate(c);
        }

        // Step 2: precompute global supply usage per player so we don't
        // recount inside the production loop.
        Span<int> usage = stackalloc int[s.Players.Length];
        Span<int> capacity = stackalloc int[s.Players.Length];
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive) continue;
            usage[(int)u.Owner] += UnitStats.SupplyCost(u.Type);
        }
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner == PlayerId.None) continue;
            capacity[(int)c.Owner] += UnitStats.SupplyCapacity(c);
        }

        // Step 3: upgrade drain, production drain, and unit spawn.
        Span<City> cities = CollectionsMarshal.AsSpan(s.Cities);
        for (int i = 0; i < cities.Length; i++)
        {
            ref City c = ref cities[i];
            if (c.Owner == PlayerId.None) continue;
            if (s.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile())
            {
                c.ProductionOrder = 0;
                c.ProductionProgress = FP.Zero;
                c.AutoBuildOrder = 0;
                c.DevelopmentLevel = 0;
                c.DevelopmentOrder = 0;
                c.DevelopmentProgress = FP.Zero;
                continue;
            }

            if (c.DevelopmentLevel == 0)
                c.DevelopmentLevel = UnitStats.CityDevelopmentMinLevel;
            c.SupplyCapacity = UnitStats.SupplyCapacity(c);

            if (c.IsUpgrading)
            {
                ProcessUpgrade(ref s, ref c);
                continue;
            }

            if (!c.IsProducing && c.AutoBuildOrder != 0)
                c.ProductionOrder = c.AutoBuildOrder;
            if (!c.IsProducing) continue;

            UnitType type = (UnitType)(c.ProductionOrder - 1);
            int costPerUnit = UnitStats.EcoCost(type);
            int supplyCost = UnitStats.SupplyCost(type);

            // Block production if filling this order would exceed supply
            // ceiling. We *do not* refund accumulated progress — the city
            // just stalls until the player either cancels or kills units.
            if (usage[(int)c.Owner] + supplyCost > capacity[(int)c.Owner])
                continue;

            // Drain at most this city's ECO rate — we don't let one city
            // siphon a whole stockpile faster than it can work. Higher-level
            // cities and capitals therefore build/upgrade faster.
            FP rate = UnitStats.EcoRate(c);
            ref Player player = ref s.Players[(int)c.Owner];
            FP drain = FP.Min(rate, player.Eco);
            // Also clamp by remaining cost so we don't overshoot.
            FP remaining = FP.FromInt(costPerUnit) - c.ProductionProgress;
            if (drain > remaining) drain = remaining;

            player.Eco -= drain;
            c.ProductionProgress += drain;

            if (c.ProductionProgress >= FP.FromInt(costPerUnit))
            {
                // No-stacking rule: only spawn if the city tile is clear.
                // If a friendly unit is already parked there, the order
                // stalls (progress capped at full cost) until the player
                // moves the blocker. Visual hint: the menu progress bar
                // shows 100% but the unit hasn't appeared yet.
                if (IsTileOccupied(s, c.TileX, c.TileY))
                {
                    c.ProductionProgress = FP.FromInt(costPerUnit);
                    continue;
                }

                int newId = s.Units.Count;
                s.Units.Add(Unit.Create(newId, c.Owner, type, c.TileX, c.TileY));
                usage[(int)c.Owner] += supplyCost;

                c.ProductionProgress = FP.Zero;
                c.ProductionOrder = c.AutoBuildOrder;
            }
        }
    }

    private static void ProcessUpgrade(ref GameState s, ref City c)
    {
        byte currentLevel = UnitStats.NormalizeDevelopmentLevel(c.DevelopmentLevel);
        byte targetLevel = c.DevelopmentOrder;
        if (targetLevel <= currentLevel || targetLevel > UnitStats.CityDevelopmentMaxLevel)
        {
            c.DevelopmentOrder = 0;
            c.DevelopmentProgress = FP.Zero;
            return;
        }

        int cost = UnitStats.UpgradeCost(currentLevel);
        if (cost <= 0)
        {
            c.DevelopmentOrder = 0;
            c.DevelopmentProgress = FP.Zero;
            return;
        }

        ref Player player = ref s.Players[(int)c.Owner];
        FP drain = FP.Min(UnitStats.EcoRate(c), player.Eco);
        FP remaining = FP.FromInt(cost) - c.DevelopmentProgress;
        if (drain > remaining) drain = remaining;

        player.Eco -= drain;
        c.DevelopmentProgress += drain;

        if (c.DevelopmentProgress >= FP.FromInt(cost))
        {
            c.DevelopmentLevel = targetLevel;
            c.SupplyCapacity = UnitStats.SupplyCapacity(c);
            c.DevelopmentOrder = 0;
            c.DevelopmentProgress = FP.Zero;
        }
    }

    private static bool IsTileOccupied(in GameState s, int x, int y)
    {
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive) continue;
            if (u.TileX == x && u.TileY == y) return true;
        }
        return false;
    }
}
