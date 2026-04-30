namespace WarGame.Sim.State;

// Two-player Phase 1 PvP. None is reserved for unowned cities/tiles. Order
// is fixed for serialization stability.
public enum PlayerId : byte
{
    None = 0,
    Player1 = 1,
    Player2 = 2,
}
