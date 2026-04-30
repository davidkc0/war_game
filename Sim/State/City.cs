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
public struct City
{
    public int Id;
    public int TileX;
    public int TileY;
    public PlayerId Owner;
    public bool IsCapital;
    public int SupplyCapacity;       // 5 by default; doctrine bonuses in Phase 3
    public FP ProductionProgress;    // accumulator [0..EcoCost(typeBeingBuilt)] for current order
}
