namespace WarGame.Sim.State;

// Per-player tile knowledge. Stored as bytes in GameState.TileVisibility.
public enum VisibilityState : byte
{
    Hidden = 0,
    Explored = 1,
    Visible = 2,
}
