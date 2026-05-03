using WarGame.Sim.Math;

namespace WarGame.Sim.State;

// A city sits on one tile of TileType.City or TileType.Capital. Cities are
// permanent (you do not lose the *tile*, only its *ownership*); destroying
// the building / depopulating it is out of v1 scope.
//
// EcoStockpile is the player-side accumulator: a city contributes
// CityEcoPerTick to its owner's running total, and unit production debits
// from that total. We track it on the City rather than on Player to make
// supply-cut consequences easy to express in Phase 3a (a city cut off from
// its owner could pause its contribution).
//
// IsCapital is redundant with the underlying tile type, but storing it on
// the City lets us iterate cities without a tile lookup in hot paths.
//
// CaptureHp: Advance Wars-style capture mechanic. Enemy units on or
// adjacent to a city deal capture damage per tick. When CaptureHp reaches
// zero, the city flips to the attacker. If enemies leave, CaptureHp
// regenerates. Capitals have higher CaptureHp so they take longer to fall.
public struct City
{
    public int Id;
    public int TileX;
    public int TileY;
    public PlayerId Owner;
    public PlayerId OriginalOwner;   // Set at creation, never mutated. Win
                                     // condition: a capital captured by
                                     // the other player triggers victory.
    public bool IsCapital;
    public int SupplyCapacity;       // 5 by default; doctrine bonuses in Phase 3
    public FP ProductionProgress;    // ECO accumulated toward current order
    public byte ProductionOrder;     // (UnitType + 1), 0 = idle. (+1 lets us
                                     //  reserve 0 as the empty sentinel.)
    public string? Name;             // Optional player-authored city name.

    // Capture health. Starts at MaxCaptureHp; enemy units deplete it.
    // When it reaches 0, the city flips ownership and CaptureHp resets.
    public int CaptureHp;

    public bool IsProducing => ProductionOrder != 0;
    public string DefaultName => IsCapital ? "Capital" : $"City {Id + 1}";
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name!;

    // Capture tuning constants.
    public const int CityMaxCaptureHp    = 100;
    public const int CapitalMaxCaptureHp = 200;
    // Damage per tick per enemy unit on the city tile (adjacent units do
    // half). At 30 ticks/sec:
    //   1 unit on city:  100 / (3 per tick) = ~33 ticks ≈ 1.1 sec
    //   1 unit adjacent: 100 / (1 per tick) = ~100 ticks ≈ 3.3 sec
    //   2 units on city: 100 / (6 per tick) = ~17 ticks ≈ 0.6 sec
    // Capital:
    //   2 units on city: 200 / (6 per tick) = ~33 ticks ≈ 1.1 sec
    public const int CaptureDamageOnTile    = 3;
    public const int CaptureDamageAdjacent  = 1;
    // Regen per tick when no enemies are nearby. Full heal in ~3.3 sec.
    public const int CaptureRegenPerTick    = 1;

    public int MaxCaptureHp => IsCapital ? CapitalMaxCaptureHp : CityMaxCaptureHp;

    // Idle = no order queued. Set by ApplyBuildUnit.
    public static City Create(int id, int x, int y, PlayerId owner, bool isCapital)
    {
        return new City
        {
            Id = id,
            TileX = x,
            TileY = y,
            Owner = owner,
            OriginalOwner = owner,
            IsCapital = isCapital,
            SupplyCapacity = UnitStats.CitySupplyCapacity,
            ProductionProgress = FP.Zero,
            ProductionOrder = 0,
            Name = null,
            CaptureHp = isCapital ? CapitalMaxCaptureHp : CityMaxCaptureHp,
        };
    }
}
