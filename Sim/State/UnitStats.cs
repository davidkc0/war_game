using WarGame.Sim.Math;

namespace WarGame.Sim.State;

// Tuning constants for the Phase 1 "Standard RTS feel" baseline. Approved by
// the human operator with the understanding that these will absolutely
// change during Phase 1 playtesting (PLAN.md says "PLAY THE GAME"). All
// values converted to per-tick at GameSim.TicksPerSecond = 30.
//
// Why expose Per-tick *FP* values rather than per-second floats: every
// production rule consults these inside the deterministic sim. We pre-divide
// once here in static init (deterministic, no float in the hot path) and the
// systems read FP directly.
public static class UnitStats
{
    public const int TicksPerSecond = GameSim.TicksPerSecond;

    // ---- Light ------------------------------------------------------------
    public static readonly FP LightMaxHp        = FP.FromInt(60);
    public static readonly FP LightDamagePerTick = FP.FromInt(8) / FP.FromInt(TicksPerSecond);   // 0.267 / tick
    public static readonly FP LightTilesPerTick  = FP.FromInt(4) / FP.FromInt(TicksPerSecond);   // 0.133 tiles / tick
    public const int LightSupplyCost = 1;
    public const int LightVisionRadius = 5;
    public const int LightEcoCost = 10;

    // ---- Heavy ------------------------------------------------------------
    public static readonly FP HeavyMaxHp        = FP.FromInt(150);
    public static readonly FP HeavyDamagePerTick = FP.FromInt(20) / FP.FromInt(TicksPerSecond);  // 0.667 / tick
    // 1.5 tiles/sec / 30 ticks = 0.05 tiles/tick. Express as ratio to keep
    // determinism with no float intermediate: 1500 / 30000 == 1/20.
    public static readonly FP HeavyTilesPerTick  = FP.One / FP.FromInt(20);
    public const int HeavySupplyCost = 2;
    public const int HeavyVisionRadius = 4;
    public const int HeavyEcoCost = 30;

    // ---- City / Capital production ---------------------------------------
    public static readonly FP CityEcoPerTick    = FP.One / FP.FromInt(TicksPerSecond);            // 1 / sec
    public static readonly FP CapitalEcoPerTick = FP.FromInt(3) / FP.FromInt(TicksPerSecond);     // 3 / sec
    public const int CitySupplyCapacity = 5;

    // Out-of-supply units lose HP per tick. Tuned so a starved unit dies in
    // ~10s (light at 60 HP / 6 dmg/sec = 10s). Phase 3a playtests this.
    public static readonly FP StarvationDamagePerTick = FP.FromInt(6) / FP.FromInt(TicksPerSecond);

    // Convenience: lookup by unit type ------------------------------------
    public static FP MaxHp(UnitType t) => t switch
    {
        UnitType.Light => LightMaxHp,
        UnitType.Heavy => HeavyMaxHp,
        _ => FP.Zero,
    };

    public static FP DamagePerTick(UnitType t) => t switch
    {
        UnitType.Light => LightDamagePerTick,
        UnitType.Heavy => HeavyDamagePerTick,
        _ => FP.Zero,
    };

    public static FP TilesPerTick(UnitType t) => t switch
    {
        UnitType.Light => LightTilesPerTick,
        UnitType.Heavy => HeavyTilesPerTick,
        _ => FP.Zero,
    };

    public static int SupplyCost(UnitType t) => t switch
    {
        UnitType.Light => LightSupplyCost,
        UnitType.Heavy => HeavySupplyCost,
        _ => 0,
    };

    public static int EcoCost(UnitType t) => t switch
    {
        UnitType.Light => LightEcoCost,
        UnitType.Heavy => HeavyEcoCost,
        _ => 0,
    };

    public static int VisionRadius(UnitType t) => t switch
    {
        UnitType.Light => LightVisionRadius,
        UnitType.Heavy => HeavyVisionRadius,
        _ => 0,
    };
}
