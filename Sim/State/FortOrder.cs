using WarGame.Sim.State;

namespace WarGame.Sim.State;

// Tracks an in-progress fort construction. Stored in GameState.PendingForts.
// When TicksRemaining reaches 0, the tile is converted to TileType.Fort and
// the entry is removed from the list.
//
// Forts under construction can be attacked: if enemy units capture the
// territory (TileOwner flips), the construction is cancelled and the ECO
// is lost. This rewards defending your construction sites.
public struct FortOrder
{
    public int TileX;
    public int TileY;
    public PlayerId Owner;
    public int TicksRemaining;

    public static FortOrder Create(int x, int y, PlayerId owner, int buildTicks)
    {
        return new FortOrder
        {
            TileX = x,
            TileY = y,
            Owner = owner,
            TicksRemaining = buildTicks,
        };
    }
}
