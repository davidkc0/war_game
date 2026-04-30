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
    // Phase 3 introduces Fortification as a *built* overlay rather than a
    // permanent terrain type, so it is intentionally not in this enum.
}

public static class TileTypeExtensions
{
    // Phase 1 movement uses these predicates instead of switch statements
    // scattered across systems. Keep all terrain rules localized here.
    public static bool IsPassable(this TileType t, bool isHeavyUnit) => t switch
    {
        TileType.Water => false,
        TileType.Mountain => !isHeavyUnit,
        _ => true,
    };

    // Returns the speed multiplier as a Q32.32 raw value. Phase 1 uses simple
    // multipliers; Phase 3 may layer doctrine bonuses on top of these.
    //   1.00 -> raw = OneRaw
    //   0.50 -> raw = OneRaw / 2
    public static long SpeedFactorRaw(this TileType t, bool isHeavyUnit) => t switch
    {
        TileType.Road => Math.FP.OneRaw + (Math.FP.OneRaw / 2),       // 1.5x
        TileType.Forest => isHeavyUnit ? Math.FP.OneRaw / 2 : Math.FP.OneRaw, // heavy 0.5x, light unaffected
        TileType.Mountain => isHeavyUnit ? 0 : Math.FP.OneRaw / 4,    // heavy impassable, light 0.25x
        _ => Math.FP.OneRaw,                                          // 1.0x
    };

    public static bool IsCityTile(this TileType t) => t is TileType.City or TileType.Capital;
}
