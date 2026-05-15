using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// Fort construction progress. Each tick, decrement TicksRemaining on all
// pending forts. When it reaches 0, convert the tile to TileType.Fort and
// register the fort with the power projection system.
//
// Forts under construction are vulnerable: if the tile's TileOwner flips
// to an enemy, the construction is cancelled (ECO is lost). This rewards
// defending construction sites — per the user's design intent.
//
// Completed forts participate in:
//   - CityCapture (CaptureHp = 80, weaker than cities)
//   - PowerProjection (base=25, radius=6)
//   - DefenseMultiplier (0.45× — strongest in the game)
//   - Supply (+2 capacity via Cities list with SupplyCapacity=2)
//
// Fort stats are defined here as constants for easy tuning.
public static class FortConstruction
{
    public const int FortEcoCost      = 50;   // Most expensive construction
    public const int FortBuildTicks   = 300;  // 10 seconds at 30 Hz
    public const int MaxFortsPerPlayer = 3;   // Prevents turtling
    public const int FortCaptureHp    = 80;   // Less than a city (100)
    public const int FortSupplyCapacity = 2;  // Small supply extension

    public static void Tick(ref GameState s)
    {
        if (s.PendingForts is null || s.PendingForts.Count == 0) return;

        Span<FortOrder> forts = CollectionsMarshal.AsSpan(s.PendingForts);
        int w = s.Map.Width;

        // Iterate backwards so we can remove completed/cancelled entries
        // without shifting indices we haven't visited yet.
        for (int i = forts.Length - 1; i >= 0; i--)
        {
            ref FortOrder f = ref forts[i];

            // Check if the tile is still in the builder's territory.
            // If an enemy captured the territory, cancel the construction.
            int tileIdx = f.TileY * w + f.TileX;
            if (s.TileOwner != null && tileIdx < s.TileOwner.Length)
            {
                var owner = (PlayerId)s.TileOwner[tileIdx];
                if (owner != f.Owner && owner != PlayerId.None)
                {
                    // Territory lost — cancel construction. ECO is lost.
                    s.PendingForts.RemoveAt(i);
                    continue;
                }
            }

            f.TicksRemaining--;
            if (f.TicksRemaining <= 0)
            {
                // Construction complete! Convert tile to Fort.
                s.Map.SetTile(f.TileX, f.TileY, TileType.Fort);

                // Register as a "city" for supply/capture purposes.
                // Fort cities have lower supply capacity and capture HP.
                int cityId = s.Cities.Count;
                var fortCity = new City
                {
                    Id = cityId,
                    TileX = f.TileX,
                    TileY = f.TileY,
                    Owner = f.Owner,
                    OriginalOwner = f.Owner,
                    IsCapital = false,
                    SupplyCapacity = FortSupplyCapacity,
                    ProductionProgress = FP.Zero,
                    ProductionOrder = 0,
                    AutoBuildOrder = 0,
                    DevelopmentLevel = 0,
                    DevelopmentProgress = FP.Zero,
                    DevelopmentOrder = 0,
                    CaptureHp = FortCaptureHp,
                };
                s.Cities.Add(fortCity);

                s.PendingForts.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Count how many completed forts a player currently owns.
    /// Forts are tracked as cities sitting on Fort tiles.
    /// </summary>
    public static int CountPlayerForts(in GameState s, PlayerId owner)
    {
        int count = 0;
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != owner) continue;
            TileType t = s.Map.GetTileUnchecked(c.TileX, c.TileY);
            if (t == TileType.Fort) count++;
        }
        return count;
    }

    /// <summary>
    /// Count pending (under construction) forts for a player.
    /// </summary>
    public static int CountPendingForts(in GameState s, PlayerId owner)
    {
        if (s.PendingForts is null) return 0;
        int count = 0;
        for (int i = 0; i < s.PendingForts.Count; i++)
        {
            if (s.PendingForts[i].Owner == owner) count++;
        }
        return count;
    }
}
