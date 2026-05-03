using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Deterministic per-player fog state. Visibility is derived from current
// friendly units and owned structures; last-seen memory persists after a tile
// drops out of vision so the renderer can show explored terrain/structures.
public static class FogOfWar
{
    public const int LightVisionRadius = 5;
    public const int HeavyVisionRadius = 4;
    public const int StructureVisionRadius = 8;

    private const int PlayerSlots = 3;

    public static void Tick(ref GameState s)
    {
        int tileCount = s.Map.TileCount;
        EnsureFogArrays(ref s, tileCount);

        for (int player = (int)PlayerId.Player1; player <= (int)PlayerId.Player2; player++)
        {
            int offset = player * tileCount;

            for (int i = 0; i < tileCount; i++)
            {
                if (s.TileVisibility[offset + i] == (byte)VisibilityState.Visible)
                    s.TileVisibility[offset + i] = (byte)VisibilityState.Explored;
            }

            var owner = (PlayerId)player;
            ClearStaleFriendlyOwnership(ref s, owner);
            RevealOwnedStructures(ref s, owner);
            RevealOwnedUnits(ref s, owner);
        }
    }

    public static VisibilityState GetVisibility(in GameState s, PlayerId viewer, int x, int y)
    {
        if (viewer == PlayerId.None) return VisibilityState.Visible;
        if (!s.Map.InBounds(x, y)) return VisibilityState.Hidden;

        int idx = VisibilityIndex(s, viewer, y * s.Map.Width + x);
        if (idx < 0 || s.TileVisibility is null || idx >= s.TileVisibility.Length)
            return VisibilityState.Visible;

        return (VisibilityState)s.TileVisibility[idx];
    }

    public static bool IsVisible(in GameState s, PlayerId viewer, int x, int y)
        => GetVisibility(s, viewer, x, y) == VisibilityState.Visible;

    public static bool IsKnown(in GameState s, PlayerId viewer, int x, int y)
        => GetVisibility(s, viewer, x, y) != VisibilityState.Hidden;

    public static TileType GetKnownTileType(in GameState s, PlayerId viewer, int x, int y)
    {
        if (viewer == PlayerId.None || GetVisibility(s, viewer, x, y) == VisibilityState.Visible)
            return s.Map.GetTileUnchecked(x, y);

        int idx = VisibilityIndex(s, viewer, y * s.Map.Width + x);
        if (idx < 0 || s.LastSeenTileType is null || idx >= s.LastSeenTileType.Length)
            return TileType.Plains;

        return (TileType)s.LastSeenTileType[idx];
    }

    public static PlayerId GetKnownTileOwner(in GameState s, PlayerId viewer, int x, int y)
    {
        if (viewer == PlayerId.None || GetVisibility(s, viewer, x, y) == VisibilityState.Visible)
        {
            int tileIdx = y * s.Map.Width + x;
            if (s.TileOwner is null || tileIdx >= s.TileOwner.Length) return PlayerId.None;
            return (PlayerId)s.TileOwner[tileIdx];
        }

        int idx = VisibilityIndex(s, viewer, y * s.Map.Width + x);
        if (idx < 0 || s.LastSeenTileOwner is null || idx >= s.LastSeenTileOwner.Length)
            return PlayerId.None;

        return (PlayerId)s.LastSeenTileOwner[idx];
    }

    private static void RevealOwnedStructures(ref GameState s, PlayerId owner)
    {
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != owner) continue;
            RevealDiamond(ref s, owner, c.TileX, c.TileY, StructureVisionRadius);
        }
    }

    private static void RevealOwnedUnits(ref GameState s, PlayerId owner)
    {
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != owner) continue;
            int radius = u.Type == UnitType.Heavy ? HeavyVisionRadius : LightVisionRadius;
            RevealDiamond(ref s, owner, u.TileX, u.TileY, radius);
        }
    }

    private static void ClearStaleFriendlyOwnership(ref GameState s, PlayerId viewer)
    {
        if (s.LastSeenTileOwner is null || s.TileOwner is null) return;

        int tileCount = s.Map.TileCount;
        int offset = (int)viewer * tileCount;
        if (offset + tileCount > s.LastSeenTileOwner.Length) return;

        for (int tileIdx = 0; tileIdx < tileCount; tileIdx++)
        {
            int fogIdx = offset + tileIdx;
            if (s.LastSeenTileOwner[fogIdx] != (byte)viewer) continue;
            if (tileIdx < s.TileOwner.Length && s.TileOwner[tileIdx] == (byte)viewer) continue;

            // A player knows when their own unit/city/fort no longer projects
            // control, but fog should not reveal who owns the tile now.
            s.LastSeenTileOwner[fogIdx] = (byte)PlayerId.None;
        }
    }

    private static void RevealDiamond(ref GameState s, PlayerId viewer, int cx, int cy, int radius)
    {
        int w = s.Map.Width;
        int h = s.Map.Height;
        int tileCount = s.Map.TileCount;
        int playerOffset = (int)viewer * tileCount;

        for (int dy = -radius; dy <= radius; dy++)
        {
            int y = cy + dy;
            if ((uint)y >= (uint)h) continue;

            int dxMax = radius - System.Math.Abs(dy);
            for (int dx = -dxMax; dx <= dxMax; dx++)
            {
                int x = cx + dx;
                if ((uint)x >= (uint)w) continue;

                int tileIdx = y * w + x;
                int fogIdx = playerOffset + tileIdx;
                s.TileVisibility[fogIdx] = (byte)VisibilityState.Visible;
                s.LastSeenTileType[fogIdx] = (byte)s.Map.GetTileUnchecked(x, y);
                s.LastSeenTileOwner[fogIdx] =
                    s.TileOwner is not null && tileIdx < s.TileOwner.Length
                        ? s.TileOwner[tileIdx]
                        : (byte)PlayerId.None;
            }
        }
    }

    private static void EnsureFogArrays(ref GameState s, int tileCount)
    {
        int len = tileCount * PlayerSlots;
        if (s.TileVisibility is null || s.TileVisibility.Length != len)
            s.TileVisibility = new byte[len];

        if (s.LastSeenTileType is null || s.LastSeenTileType.Length != len)
            s.LastSeenTileType = new byte[len];

        if (s.LastSeenTileOwner is null || s.LastSeenTileOwner.Length != len)
            s.LastSeenTileOwner = new byte[len];
    }

    private static int VisibilityIndex(in GameState s, PlayerId viewer, int tileIdx)
    {
        if (viewer == PlayerId.None) return -1;
        int player = (int)viewer;
        if ((uint)player >= PlayerSlots) return -1;
        if ((uint)tileIdx >= (uint)s.Map.TileCount) return -1;
        return player * s.Map.TileCount + tileIdx;
    }
}
