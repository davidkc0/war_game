namespace WarGame.Sim.State;

// Per-unit supply state written by SupplyLines each tick. Byte values are
// serialized, so append only if more statuses are added later.
public enum SupplyStatus : byte
{
    None = 0,
    Supplied = 1,
    RoadSupplied = 2,
    CutOff = 3,
}
