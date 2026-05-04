using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

public static class TerrainRules
{
    public static bool IsWaterway(TileType t) => t is TileType.Water or TileType.River;

    public static bool IsLandLike(TileType t)
        => !IsWaterway(t) && t != TileType.MountainPeak;

    public static bool IsMountainEdge(in MapState map, int x, int y)
    {
        if (!map.InBounds(x, y) || map.GetTileUnchecked(x, y) != TileType.Mountain)
            return false;

        int mountainNeighbors = 0;
        bool cardinalLowland = false;

        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                int nx = x + ox, ny = y + oy;
                if (!map.InBounds(nx, ny)) continue;

                TileType t = map.GetTileUnchecked(nx, ny);
                if (t is TileType.Mountain or TileType.MountainPeak)
                {
                    mountainNeighbors++;
                    continue;
                }

                if ((ox == 0 || oy == 0) && IsLandLike(t))
                    cardinalLowland = true;
            }
        }

        return cardinalLowland && mountainNeighbors <= 6;
    }

    public static bool IsBroadWater(in MapState map, int x, int y)
    {
        if (!map.InBounds(x, y)) return false;
        if (map.GetTileUnchecked(x, y) != TileType.Water) return false;
        return !IsOneTileWaterway(map, x, y);
    }

    public static bool IsOneTileWaterway(in MapState map, int x, int y)
    {
        if (!map.InBounds(x, y)) return false;
        TileType t = map.GetTileUnchecked(x, y);
        if (!IsWaterway(t)) return false;

        bool eastWestLand = IsLandForBridgeEnd(map, x - 1, y) && IsLandForBridgeEnd(map, x + 1, y);
        bool northSouthLand = IsLandForBridgeEnd(map, x, y - 1) && IsLandForBridgeEnd(map, x, y + 1);
        return eastWestLand || northSouthLand;
    }

    public static bool IsLandForBridgeEnd(in MapState map, int x, int y)
    {
        if (!map.InBounds(x, y)) return false;
        TileType t = map.GetTileUnchecked(x, y);
        return IsLandLike(t);
    }

    public static bool IsNarrowLandCauseway(in MapState map, int x, int y)
    {
        if (!map.InBounds(x, y)) return false;
        TileType t = map.GetTileUnchecked(x, y);
        if (!IsLandLike(t)) return false;

        bool waterNorth = IsWaterwayAt(map, x, y - 1);
        bool waterSouth = IsWaterwayAt(map, x, y + 1);
        bool waterWest = IsWaterwayAt(map, x - 1, y);
        bool waterEast = IsWaterwayAt(map, x + 1, y);
        return (waterNorth && waterSouth) || (waterWest && waterEast);
    }

    public static bool HasTwoByTwoLandFootprint(in MapState map, int x, int y)
    {
        for (int oy = -1; oy <= 0; oy++)
        {
            for (int ox = -1; ox <= 0; ox++)
            {
                int x0 = x + ox, y0 = y + oy;
                if (IsLandFootprintTile(map, x0, y0)
                    && IsLandFootprintTile(map, x0 + 1, y0)
                    && IsLandFootprintTile(map, x0, y0 + 1)
                    && IsLandFootprintTile(map, x0 + 1, y0 + 1))
                    return true;
            }
        }
        return false;
    }

    public static bool HasStableCityFootprint(in MapState map, int x, int y)
    {
        if (!map.InBounds(x, y)) return false;
        TileType t = map.GetTileUnchecked(x, y);
        if (t is not (TileType.Plains or TileType.Forest or TileType.Mountain or TileType.City or TileType.Capital)) return false;
        if (t == TileType.Mountain && !IsMountainEdge(map, x, y)) return false;
        if (IsNarrowLandCauseway(map, x, y)) return false;
        if (!HasTwoByTwoLandFootprint(map, x, y)) return false;

        int land3x3 = 0;
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
                if (IsLandFootprintTile(map, x + ox, y + oy)) land3x3++;

        int cardinalLand = 0;
        if (IsLandFootprintTile(map, x - 1, y)) cardinalLand++;
        if (IsLandFootprintTile(map, x + 1, y)) cardinalLand++;
        if (IsLandFootprintTile(map, x, y - 1)) cardinalLand++;
        if (IsLandFootprintTile(map, x, y + 1)) cardinalLand++;

        return land3x3 >= 6 && cardinalLand >= 2;
    }

    private static bool IsLandFootprintTile(in MapState map, int x, int y)
    {
        if (!map.InBounds(x, y)) return false;
        TileType t = map.GetTileUnchecked(x, y);
        return IsLandLike(t);
    }

    private static bool IsWaterwayAt(in MapState map, int x, int y)
        => map.InBounds(x, y) && IsWaterway(map.GetTileUnchecked(x, y));
}
