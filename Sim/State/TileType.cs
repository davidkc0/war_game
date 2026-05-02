namespace WarGame.Sim.State;

// One byte per tile. Order matters for serialization stability — append new
// tile types to the end and never renumber. If a tile type is retired, leave
// the slot empty rather than reusing the value, so old replays stay valid.
public enum TileType : byte
{
    Plains = 0,
    Forest = 1,
    Mountain = 2,
    Water = 3,
    Road = 4,
    City = 5,
    Capital = 6,
    Fort = 7,       // Built by players via BuildFortCommand. Can be captured
                    // or razed. Strongest defense bonus in the game.
    MountainPeak = 8,
    River = 9,      // Narrow watercourse. Passable, but slow until bridges
                    // / engineered crossings exist in a later phase.
    Bridge = 10,    // Engineered crossing over a River. Moves and supplies
                    // like a road, but keeps its own render color/cost.
}

public static class TileTypeExtensions
{
    // Phase 1 movement uses these predicates instead of switch statements
    // scattered across systems. Keep all terrain rules localized here.
    public static bool IsPassable(this TileType t, bool isHeavyUnit) => t switch
    {
        TileType.Water => false,
        TileType.River => true,
        TileType.Bridge => true,
        TileType.Mountain => !isHeavyUnit,
        TileType.MountainPeak => false, // impassable even to light units!
        _ => true,
    };

    // Returns the speed multiplier as a Q32.32 raw value. Phase 1 uses simple
    // multipliers; Phase 3 may layer doctrine bonuses on top of these.
    //   1.00 -> raw = OneRaw
    //   0.50 -> raw = OneRaw / 2
    public static long SpeedFactorRaw(this TileType t, bool isHeavyUnit) => t switch
    {
        TileType.Road => Math.FP.OneRaw + (Math.FP.OneRaw / 2),       // 1.5x
        TileType.Bridge => Math.FP.OneRaw + (Math.FP.OneRaw / 2),     // 1.5x
        TileType.River => Math.FP.OneRaw / 3,                         // slow crossing
        TileType.Forest => isHeavyUnit ? Math.FP.OneRaw / 2 : Math.FP.OneRaw, // heavy 0.5x, light unaffected
        TileType.Mountain => isHeavyUnit ? 0 : Math.FP.OneRaw / 4,    // heavy impassable, light 0.25x
        TileType.MountainPeak => 0,                                   // completely impassable
        _ => Math.FP.OneRaw,                                          // 1.0x
    };

    // Defense multiplier applied to INCOMING damage when a unit is standing
    // on this tile. Values < 1.0 mean the defender takes less damage.
    public static long DefenseMultiplierRaw(this TileType t) => t switch
    {
        TileType.Forest   => Math.FP.OneRaw * 70 / 100,     // 0.70
        TileType.Mountain => Math.FP.OneRaw * 50 / 100,     // 0.50
        TileType.MountainPeak => Math.FP.OneRaw * 50 / 100, // 0.50 (if they could get there)
        TileType.Road     => Math.FP.OneRaw * 110 / 100,    // 1.10
        TileType.Bridge   => Math.FP.OneRaw * 115 / 100,    // 1.15
        TileType.River    => Math.FP.OneRaw * 120 / 100,    // 1.20 (bad footing)
        TileType.City     => Math.FP.OneRaw * 60 / 100,     // 0.60
        TileType.Capital  => Math.FP.OneRaw * 60 / 100,     // 0.60
        TileType.Fort     => Math.FP.OneRaw * 45 / 100,     // 0.45
        _                 => Math.FP.OneRaw,                 // 1.00
    };

    public static bool IsCityTile(this TileType t) => t is TileType.City or TileType.Capital;
    public static bool IsFortTile(this TileType t) => t is TileType.Fort;
}
