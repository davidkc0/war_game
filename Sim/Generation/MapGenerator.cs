using System;
using System.Collections.Generic;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;

namespace WarGame.Sim.Generation;

// Procedural map generator. Takes a seed and produces a MapState + city list
// with realistic topography: elevation → mountains → foothills → water →
// forests → roads. All integer math; no floats; fully deterministic.
//
// Layout per user spec (Phase 2, 2026-05-01):
//   - Each team gets 1 capital + 2 cities = 6 total structures
//   - Capitals at opposing map extremes
//   - Balance is NOT enforced (asymmetry is intentional per Freedman)
//   - Map size: 60×60 default, scalable later
//
// Geological rules:
//   - Mountains are never adjacent to water (foothills buffer)
//   - Forests cluster in bands (moisture proximity to water)
//   - Roads follow valleys and passes (A* cheapest path)
//   - At least one path between all cities (connectivity guarantee)
//   - Mountain passes at saddle points
public static class MapGenerator
{
    public const int DefaultWidth = 60;
    public const int DefaultHeight = 60;

    // Terrain percentages in permille. Thresholds are computed from the
    // elevation distribution per-seed (sort + percentile lookup) rather
    // than as absolute noise values, so the mix stays balanced regardless
    // of how a particular seed's noise happens to distribute.
    //
    // Keep these as integers: this file lives in Sim/ and must remain
    // float-free for cross-platform determinism.
    private const int PlainsPermille     = 390;
    private const int ForestMixPermille  = 200;
    private const int FoothillPermille   = 160;
    private const int MajorWaterPermille = 85;
    private const int InlandLakePermille = 12;
    // Moisture threshold (still on the fixed [0, 65535] scale because
    // moisture is a separate channel, not a terrain elevation).
    private const int ForestMoistureThreshold = 22000;

    // City placement.
    private const int MinCityDistance       = 10;     // Minimum tiles between any two cities
    private const int CapitalEdgeMargin     = 5;      // Capitals placed within this many tiles of corners
    private const int CitiesPerPlayer       = 2;      // Non-capital cities per player

    public struct GeneratorResult
    {
        public MapState Map;
        public List<City> Cities;
        public ulong AcceptedSeed;
        public int AttemptsUsed;
        public BalanceValidator.Result LastScore;
    }

    /// <summary>
    /// Maximum number of seed perturbations to try before giving up and
    /// returning the last (rejected) map. 10 attempts is generous given
    /// our balance threshold; in practice most seeds land on the first try.
    /// </summary>
    public const int MaxRetries = 10;

    /// <summary>
    /// Generate a complete map from a seed. Internally runs the BalanceValidator
    /// and retries with derived seeds until acceptance or MaxRetries. The first
    /// attempt uses `seed` directly; later attempts hash the seed forward, so a
    /// caller passing a known-good seed gets the same map every time.
    /// </summary>
    public static GeneratorResult Generate(ulong seed, int width = DefaultWidth, int height = DefaultHeight)
    {
        ulong currentSeed = seed;
        GeneratorResult last = default;
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            last = GenerateOnce(currentSeed, width, height);
            last.AttemptsUsed = attempt + 1;
            last.AcceptedSeed = currentSeed;
            last.LastScore = BalanceValidator.Score(last.Map, last.Cities);
            if (last.LastScore.Accepted) return last;
            // Derive next seed deterministically (xorshift on the current seed).
            currentSeed = NextSeed(currentSeed);
        }
        // Out of retries — return the last map even if it's below threshold.
        // Callers can read LastScore.Accepted to detect this case.
        return last;
    }

    /// <summary>
    /// Single-attempt generation without the validator loop. Public so the
    /// validator tests can hand-build candidate maps without budget overhead.
    /// </summary>
    public static GeneratorResult GenerateOnce(ulong seed, int width = DefaultWidth, int height = DefaultHeight)
    {
        var rng = new SimRng(seed);
        var noise = new IntegerNoise(ref rng);
        // Pass by ref so the moisture noise gets a *different* permutation
        // table — independent channels, not correlated.
        var moistureNoise = new IntegerNoise(ref rng);
        var coastNoise = new IntegerNoise(ref rng);

        // Step 1: Generate elevation map.
        int[] elevation = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                elevation[y * width + x] = noise.OctaveNoise(x, y, 16, 3);

        // Step 2: Generate moisture map (influences forest placement).
        int[] moisture = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                moisture[y * width + x] = moistureNoise.OctaveNoise(x, y, 12, 2);

        // Step 3: Assign the land terrain bands. Water is carved by a
        // dedicated coastline/basin pass below; assigning "lowest N%" as
        // water made ruler-straight seas whenever the low-noise band was
        // broad near an edge.
        var builder = new MapState.Builder(width, height);
        AssignBaseTerrain(builder, elevation, moisture, width, height);

        // Step 4: Carve water after landforms exist: irregular ocean
        // coastlines from one or two tectonic margins, plus small inland
        // basins in true lowlands.
        CarveWaterBodies(builder, elevation, coastNoise, width, height, ref rng);

        // Step 5: Enforce geological rules (buffer mountains from water, etc.)
        EnforceGeologicalRules(builder, elevation, width, height);

        // Step 6: Final cleanup. Any of the prior steps that converted
        // water → plains (most often PunchPath) can leave a previously-paired
        // water tile isolated from a real basin, which the test/playtest
        // reads as a visual artifact.
        CleanupIsolatedWater(builder, width, height);

        // Step 7: Place capitals and cities after water exists so candidate
        // scoring can prefer coherent coastal settlement sites.
        var cities = PlaceCities(builder, elevation, width, height, ref rng);

        // Step 8: Ensure all cities are land-reachable. If not, punch a
        // plain trail as a last-resort connectivity repair; this does *not*
        // create a road between territories.
        EnsureConnectivity(builder, cities, width, height);
        CleanupIsolatedWater(builder, width, height);

        // Step 9: Rivers start in mountain ranges and flow downhill toward
        // existing water bodies. They are generated after connectivity
        // repair so later path punching cannot sever their mouths.
        GenerateRivers(builder, elevation, width, height);
        StabilizeAllCitySites(builder, cities, width, height);

        // Step 10: Generate intra-territory road networks only after all
        // terrain repair passes are done. Earlier versions laid roads first,
        // then punched connectivity paths later, which left some accepted
        // maps with no visible roads at all.
        GenerateRoads(builder, elevation, cities, width, height);

        // Step 11: Peaks are rebuilt as the final terrain pass so they
        // describe the mountain ranges that actually survived rivers,
        // city-site cleanup, connectivity repairs, and generated roads.
        RebuildMountainPeakSpines(builder, elevation, width, height);

        var map = builder.Build();

        return new GeneratorResult
        {
            Map = map,
            Cities = cities,
        };
    }

    /// <summary>
    /// Deterministic seed perturbation. xorshift64* is sufficient — we just
    /// need each retry to land on a different terrain layout.
    /// </summary>
    private static ulong NextSeed(ulong s)
    {
        if (s == 0) s = 0x9E3779B97F4A7C15UL;
        s ^= s >> 12;
        s ^= s << 25;
        s ^= s >> 27;
        return s * 0x2545F4914F6CDD1DUL;
    }

    private static void AssignBaseTerrain(MapState.Builder b, int[] elev, int[] moist,
                                           int w, int h)
    {
        // Rank tiles by elevation instead of comparing against raw
        // threshold values. Integer value-noise often has tied plateaus;
        // threshold comparisons can therefore collapse an entire band
        // (water was disappearing this way). Rank assignment guarantees a
        // visible lowland-water layer for every seed.
        int n = w * h;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, c) =>
        {
            int cmp = elev[a].CompareTo(elev[c]);
            return cmp != 0 ? cmp : a.CompareTo(c);
        });

        int plainsEnd = n * PlainsPermille / 1000;
        int forestMixEnd = plainsEnd + n * ForestMixPermille / 1000;
        int foothillEnd = forestMixEnd + n * FoothillPermille / 1000;

        int forestMixSpan = System.Math.Max(1, forestMixEnd - plainsEnd);
        for (int rank = 0; rank < n; rank++)
        {
            int idx = order[rank];
            int x = idx % w, y = idx / w;
            TileType tile;
            if (rank < plainsEnd)
            {
                tile = TileType.Plains;
            }
            else if (rank < forestMixEnd)
            {
                int bandRank = rank - plainsEnd;
                bool upperHalfOfBand = bandRank * 2 >= forestMixSpan;
                tile = (moist[idx] > ForestMoistureThreshold || upperHalfOfBand)
                    ? TileType.Forest
                    : TileType.Plains;
            }
            else if (rank < foothillEnd)
            {
                tile = TileType.Forest;
            }
            else
            {
                tile = TileType.Mountain;
            }
            b.Set(x, y, tile);
        }
    }

    private static void CarveWaterBodies(MapState.Builder b, int[] elev, IntegerNoise coastNoise,
                                          int w, int h, ref SimRng rng)
    {
        CarveMajorBasins(b, elev, coastNoise, w, h, ref rng);
        CarveInlandBasins(b, elev, coastNoise, w, h);
    }

    private static void CarveMajorBasins(MapState.Builder b, int[] elev, IntegerNoise coastNoise,
                                         int w, int h, ref SimRng rng)
    {
        int n = w * h;
        int targetWater = System.Math.Max(120, n * MajorWaterPermille / 1000);
        int maxBasins = n < 3000 ? 1 : 2;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, c) =>
        {
            int cmp = elev[a].CompareTo(elev[c]);
            return cmp != 0 ? cmp : a.CompareTo(c);
        });

        var unavailable = new bool[n];
        var snapshot = b.Build();
        int carved = 0;
        int basins = 0;
        int minSpacing = System.Math.Max(12, System.Math.Min(w, h) / 3);

        for (int rank = 0; rank < n && carved < targetWater && basins < maxBasins; rank++)
        {
            int seed = order[rank];
            int sx = seed % w, sy = seed / w;
            if (unavailable[seed]) continue;
            if (DistanceToNearestEdge(sx, sy, w, h) < 3) continue;
            if (IsMountainLike(snapshot.GetTileUnchecked(sx, sy))) continue;
            if (NearestWaterDistance(b.Build(), sx, sy, w, h) < minSpacing) continue;

            int remaining = targetWater - carved;
            int requested = System.Math.Min(remaining, 90 + rng.NextInt(0, 100));
            int added = CarveIrregularWaterBody(b, b.Build(), elev, coastNoise, seed, w, h, requested, unavailable);
            if (added >= 40)
            {
                carved += added;
                basins++;
            }
        }
    }

    private static void CarveInlandBasins(MapState.Builder b, int[] elev, IntegerNoise coastNoise, int w, int h)
    {
        int n = w * h;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, c) =>
        {
            int cmp = elev[a].CompareTo(elev[c]);
            return cmp != 0 ? cmp : a.CompareTo(c);
        });

        int targetWater = System.Math.Max(8, n * InlandLakePermille / 1000);
        int carved = 0;
        int maxBasins = System.Math.Max(1, n / 1800);
        int basins = 0;
        var snapshot = b.Build();
        var alreadyUsed = new bool[n];

        for (int rank = 0; rank < n && carved < targetWater && basins < maxBasins; rank++)
        {
            int seed = order[rank];
            int sx = seed % w, sy = seed / w;
            if (alreadyUsed[seed]) continue;
            if (DistanceToNearestEdge(sx, sy, w, h) < System.Math.Min(w, h) / 6) continue;
            TileType seedTile = snapshot.GetTileUnchecked(sx, sy);
            if (seedTile == TileType.Water || IsMountainLike(seedTile)) continue;

            int added = CarveIrregularWaterBody(b, snapshot, elev, coastNoise, seed, w, h,
                6 + coastNoise.Sample(sx, sy, 5) % 18, alreadyUsed);
            if (added >= 4)
            {
                carved += added;
                basins++;
            }
        }
    }

    private static int CarveIrregularWaterBody(MapState.Builder b, MapState snapshot, int[] elev, IntegerNoise coastNoise,
                                               int seed, int w, int h, int targetSize, bool[] alreadyUsed)
    {
        int seedElev = elev[seed];
        int ceiling = seedElev + (targetSize >= 40 ? 9500 : 5500);
        var queue = new PriorityQueue<int, int>();
        var basin = new List<int>();
        var localVisited = new bool[w * h];
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };

        localVisited[seed] = true;
        queue.Enqueue(seed, 0);
        while (queue.Count > 0 && basin.Count < targetSize)
        {
            int idx = queue.Dequeue();
            int x = idx % w, y = idx / w;
            TileType t = snapshot.GetTileUnchecked(x, y);
            if (t == TileType.Water || IsMountainLike(t) || t == TileType.City || t == TileType.Capital || t == TileType.Fort)
                continue;
            if (elev[idx] > ceiling) continue;
            if (targetSize < 40 && coastNoise.Sample(x + 31, y + 47, 4) < 9000 && basin.Count > 0) continue;

            basin.Add(idx);
            alreadyUsed[idx] = true;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (localVisited[nIdx]) continue;
                localVisited[nIdx] = true;
                int edgeNoise = coastNoise.Sample(nx + 31, ny + 47, targetSize >= 40 ? 8 : 4);
                int priority = elev[nIdx] + edgeNoise / 3 + nIdx % 17;
                queue.Enqueue(nIdx, priority);
            }
        }

        if (basin.Count < 4) return 0;
        for (int i = 0; i < basin.Count; i++)
        {
            int idx = basin[i];
            b.Set(idx % w, idx / w, TileType.Water);
        }
        return basin.Count;
    }

    private static int NearestWaterDistance(MapState map, int sx, int sy, int w, int h)
    {
        int best = int.MaxValue;
        for (int i = 0; i < w * h; i++)
        {
            if ((TileType)map.RawTiles[i] != TileType.Water) continue;
            int x = i % w, y = i / w;
            int d = System.Math.Abs(sx - x) + System.Math.Abs(sy - y);
            if (d < best) best = d;
        }
        return best;
    }

    private static int DistanceToNearestEdge(int x, int y, int w, int h)
    {
        int d = x;
        if (y < d) d = y;
        int right = w - 1 - x;
        if (right < d) d = right;
        int bottom = h - 1 - y;
        if (bottom < d) d = bottom;
        return d;
    }

    private static void EnforceGeologicalRules(MapState.Builder b, int[] elev, int w, int h)
    {
        // Rule 1: Mountains must not be adjacent to water. Convert to Forest
        // (foothills) if they are.
        // We need to read the current state and write corrections, so do
        // multiple passes until stable.
        var temp = b.Build();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                TileType t = temp.GetTileUnchecked(x, y);
                if (t != TileType.Mountain) continue;

                // Check 4-connected neighbors for water.
                bool nearWater = false;
                if (x > 0     && temp.GetTileUnchecked(x-1, y) == TileType.Water) nearWater = true;
                if (x < w - 1 && temp.GetTileUnchecked(x+1, y) == TileType.Water) nearWater = true;
                if (y > 0     && temp.GetTileUnchecked(x, y-1) == TileType.Water) nearWater = true;
                if (y < h - 1 && temp.GetTileUnchecked(x, y+1) == TileType.Water) nearWater = true;

                if (nearWater) b.Set(x, y, TileType.Forest); // Downgrade to foothills
            }
        }

        // Rule 2: Create mountain passes (saddle points). Find mountain
        // tiles that are local elevation minima along ridgelines and
        // convert them to Plains to create natural chokepoints.
        temp = b.Build();
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                TileType t = temp.GetTileUnchecked(x, y);
                if (t != TileType.Mountain) continue;

                int e = elev[y * w + x];
                int eN = elev[(y-1) * w + x];
                int eS = elev[(y+1) * w + x];
                int eW = elev[y * w + (x-1)];
                int eE = elev[y * w + (x+1)];

                // Saddle point: lower than N+S neighbors but higher than E+W
                // (or vice versa). This creates gaps in mountain ridges.
                bool saddleNS = (e < eN && e < eS && e > eW && e > eE);
                bool saddleEW = (e < eW && e < eE && e > eN && e > eS);

                if (saddleNS || saddleEW)
                    b.Set(x, y, TileType.Plains);
            }
        }
    }

    private static List<City> PlaceCities(MapState.Builder b, int[] elev,
                                           int w, int h, ref SimRng rng)
    {
        var cities = new List<City>();
        var temp = b.Build();

        // Capital 1: northwest area.
        var (c1x, c1y) = FindSuitableTile(temp, elev, 1, 1,
            CapitalEdgeMargin + 8, CapitalEdgeMargin + 8, w, h, ref rng);
        b.Set(c1x, c1y, TileType.Capital);
        StabilizeCitySite(b, c1x, c1y, w, h);
        cities.Add(City.Create(0, c1x, c1y, PlayerId.Player1, isCapital: true));

        // Capital 2: southeast area.
        var (c2x, c2y) = FindSuitableTile(temp, elev,
            w - CapitalEdgeMargin - 8, h - CapitalEdgeMargin - 8,
            w - 1, h - 1, w, h, ref rng);
        b.Set(c2x, c2y, TileType.Capital);
        StabilizeCitySite(b, c2x, c2y, w, h);
        cities.Add(City.Create(1, c2x, c2y, PlayerId.Player2, isCapital: true));

        // Player 1 cities: clustered around capital 1.
        var placed = new List<(int x, int y)> { (c1x, c1y), (c2x, c2y) };
        int cityId = 2;

        for (int i = 0; i < CitiesPerPlayer; i++)
        {
            var (cx, cy) = FindCitySpot(temp, elev, placed, c1x, c1y, 
                c1x - 16, c1y - 16, c1x + 16, c1y + 16, w, h, ref rng);
            b.Set(cx, cy, TileType.City);
            StabilizeCitySite(b, cx, cy, w, h);
            cities.Add(City.Create(cityId++, cx, cy, PlayerId.Player1, isCapital: false));
            placed.Add((cx, cy));
        }

        // Player 2 cities: clustered around capital 2.
        for (int i = 0; i < CitiesPerPlayer; i++)
        {
            var (cx, cy) = FindCitySpot(temp, elev, placed, c2x, c2y, 
                c2x - 16, c2y - 16, c2x + 16, c2y + 16, w, h, ref rng);
            b.Set(cx, cy, TileType.City);
            StabilizeCitySite(b, cx, cy, w, h);
            cities.Add(City.Create(cityId++, cx, cy, PlayerId.Player2, isCapital: false));
            placed.Add((cx, cy));
        }

        return cities;
    }

    private static void StabilizeCitySite(MapState.Builder b, int cx, int cy, int w, int h)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            var map = b.Build();
            if (TerrainRules.HasStableCityFootprint(map, cx, cy)) return;

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int x = cx + ox, y = cy + oy;
                    if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                    TileType t = map.GetTileUnchecked(x, y);
                    if (t is TileType.Water or TileType.River or TileType.Mountain or TileType.MountainPeak)
                        b.Set(x, y, TileType.Plains);
                }
            }
        }
    }

    private static void StabilizeAllCitySites(MapState.Builder b, List<City> cities, int w, int h)
    {
        for (int i = 0; i < cities.Count; i++)
            StabilizeCitySite(b, cities[i].TileX, cities[i].TileY, w, h);
    }

    /// <summary>
    /// Find a tile suitable for a city/capital within the given bounding box.
    /// Prefers Plains tiles at mid-elevation. Falls back to Forest, then any passable.
    /// </summary>
    private static (int x, int y) FindSuitableTile(MapState map, int[] elev,
        int x0, int y0, int x1, int y1, int w, int h, ref SimRng rng)
    {
        x0 = System.Math.Max(1, x0);
        y0 = System.Math.Max(1, y0);
        x1 = System.Math.Min(w - 2, x1);
        y1 = System.Math.Min(h - 2, y1);

        // Collect candidates. Prefer sites beside navigable water: those
        // are legible city locations and later become bridge/road decisions.
        var waterfrontPlains = new List<(int x, int y)>();
        var waterfrontForest = new List<(int x, int y)>();
        var plains = new List<(int x, int y)>();
        var forest = new List<(int x, int y)>();
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                TileType t = map.GetTileUnchecked(x, y);
                if (t != TileType.Plains && t != TileType.Forest) continue;
                if (!TerrainRules.HasStableCityFootprint(map, x, y)) continue;
                bool waterfront = IsAdjacentToWaterOrRiver(map, x, y);
                if (t == TileType.Plains)
                {
                    if (waterfront) waterfrontPlains.Add((x, y));
                    else plains.Add((x, y));
                }
                else
                {
                    if (waterfront) waterfrontForest.Add((x, y));
                    else forest.Add((x, y));
                }
            }
        }

        if (waterfrontPlains.Count > 0) return waterfrontPlains[rng.NextInt(0, waterfrontPlains.Count)];
        if (waterfrontForest.Count > 0) return waterfrontForest[rng.NextInt(0, waterfrontForest.Count)];
        if (plains.Count > 0) return plains[rng.NextInt(0, plains.Count)];
        if (forest.Count > 0) return forest[rng.NextInt(0, forest.Count)];

        // Fallback: nearest stable land outside the preferred box. Returning
        // the geometric center can place a capital on water or a one-tile
        // isthmus, which then forces ugly connectivity scars.
        return FindNearestStableCityTile(map, (x0 + x1) / 2, (y0 + y1) / 2, w, h);
    }

    /// <summary>
    /// Find a spot for a regular city within bounds, enforcing minimum
    /// distance from all previously placed cities.
    /// </summary>
    private static (int x, int y) FindCitySpot(MapState map, int[] elev,
        List<(int x, int y)> existing, int capX, int capY, int x0, int y0, int x1, int y1,
        int w, int h, ref SimRng rng)
    {
        x0 = System.Math.Max(2, x0);
        y0 = System.Math.Max(2, y0);
        x1 = System.Math.Min(w - 3, x1);
        y1 = System.Math.Min(h - 3, y1);

        var preferred = new List<(int x, int y)>();
        var candidates = new List<(int x, int y)>();
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                TileType t = map.GetTileUnchecked(x, y);
                if (t != TileType.Plains && t != TileType.Forest) continue;
                if (!TerrainRules.HasStableCityFootprint(map, x, y)) continue;

                // Enforce maximum Manhattan distance from the capital to guarantee contiguous territory.
                int distToCap = System.Math.Abs(x - capX) + System.Math.Abs(y - capY);
                if (distToCap > 14) continue;

                // Check minimum distance from all placed cities.
                bool tooClose = false;
                foreach (var (ex, ey) in existing)
                {
                    int dx = x - ex; if (dx < 0) dx = -dx;
                    int dy = y - ey; if (dy < 0) dy = -dy;
                    if (dx + dy < MinCityDistance) { tooClose = true; break; }
                }
                if (!tooClose)
                {
                    if (IsAdjacentToWaterOrRiver(map, x, y)) preferred.Add((x, y));
                    else candidates.Add((x, y));
                }
            }
        }

        if (preferred.Count > 0)
            return ClosestToCapital(preferred, capX, capY);
        if (candidates.Count > 0)
            return ClosestToCapital(candidates, capX, capY);

        // Fallback: relax only city-to-city spacing, not the capital
        // cluster. A far fallback creates disconnected startup ownership
        // blobs, which is worse than two nearby same-owner cities.
        var relaxed = new List<(int x, int y)>();
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                TileType t = map.GetTileUnchecked(x, y);
                if (t != TileType.Plains && t != TileType.Forest) continue;
                if (!TerrainRules.HasStableCityFootprint(map, x, y)) continue;
                int distToCap = System.Math.Abs(x - capX) + System.Math.Abs(y - capY);
                if (distToCap > 14) continue;
                relaxed.Add((x, y));
            }
        }
        if (relaxed.Count > 0)
            return ClosestToCapital(relaxed, capX, capY);

        // Last resort: any suitable tile in the local box.
        return FindSuitableTile(map, elev, x0, y0, x1, y1, w, h, ref rng);
    }

    private static (int x, int y) ClosestToCapital(List<(int x, int y)> candidates, int capX, int capY)
    {
        int best = 0;
        int bestScore = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            var (x, y) = candidates[i];
            int score = (System.Math.Abs(x - capX) + System.Math.Abs(y - capY)) * 100 + y * 10 + x;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return candidates[best];
    }

    private static (int x, int y) FindNearestStableCityTile(MapState map, int centerX, int centerY, int w, int h)
    {
        int bestX = -1, bestY = -1, bestScore = int.MaxValue;
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                TileType t = map.GetTileUnchecked(x, y);
                if (t != TileType.Plains && t != TileType.Forest) continue;
                if (!TerrainRules.HasStableCityFootprint(map, x, y)) continue;

                int score = (System.Math.Abs(x - centerX) + System.Math.Abs(y - centerY)) * 100 + y * 10 + x;
                if (score >= bestScore) continue;
                bestScore = score;
                bestX = x;
                bestY = y;
            }
        }

        if (bestX >= 0) return (bestX, bestY);

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                TileType t = map.GetTileUnchecked(x, y);
                if (t is not (TileType.Plains or TileType.Forest)) continue;
                int score = (System.Math.Abs(x - centerX) + System.Math.Abs(y - centerY)) * 100 + y * 10 + x;
                if (score >= bestScore) continue;
                bestScore = score;
                bestX = x;
                bestY = y;
            }
        }

        return bestX >= 0 ? (bestX, bestY) : (System.Math.Clamp(centerX, 1, w - 2), System.Math.Clamp(centerY, 1, h - 2));
    }

    private static bool IsAdjacentToWaterOrRiver(MapState map, int x, int y)
    {
        if (x > 0 && IsWaterOrRiver(map.GetTileUnchecked(x - 1, y))) return true;
        if (x + 1 < map.Width && IsWaterOrRiver(map.GetTileUnchecked(x + 1, y))) return true;
        if (y > 0 && IsWaterOrRiver(map.GetTileUnchecked(x, y - 1))) return true;
        if (y + 1 < map.Height && IsWaterOrRiver(map.GetTileUnchecked(x, y + 1))) return true;
        return false;
    }

    private static bool IsWaterOrRiver(TileType t) => t is TileType.Water or TileType.River;

    /// <summary>
    /// Generate roads between cities owned by the same player only. Roads
    /// are internal logistics; they should not hand both sides a free paved
    /// invasion corridor through the center of the map. Later phases add
    /// unit-built roads as an intentional engineering action.
    /// </summary>
    private static void GenerateRoads(MapState.Builder b, int[] elev,
                                       List<City> cities, int w, int h)
    {
        if (cities.Count < 2) return;

        GeneratePlayerRoads(b, elev, cities, PlayerId.Player1, w, h);
        GeneratePlayerRoads(b, elev, cities, PlayerId.Player2, w, h);
    }

    private static void GeneratePlayerRoads(MapState.Builder b, int[] elev,
                                             List<City> cities, PlayerId owner,
                                             int w, int h)
    {
        int capitalIndex = -1;
        for (int i = 0; i < cities.Count; i++)
        {
            City c = cities[i];
            if (c.Owner == owner && c.IsCapital)
            {
                capitalIndex = i;
                break;
            }
        }
        if (capitalIndex < 0) return;

        City capital = cities[capitalIndex];
        for (int i = 0; i < cities.Count; i++)
        {
            if (i == capitalIndex) continue;
            City city = cities[i];
            if (city.Owner != owner) continue;
            LayRoad(b, elev, capital.TileX, capital.TileY, city.TileX, city.TileY, w, h);
        }
    }

    private static void LayRoad(MapState.Builder b, int[] elev,
                                 int x0, int y0, int x1, int y1, int w, int h)
    {
        var tempMap = b.Build();
        
        int[] dist = new int[w * h];
        int[] prev = new int[w * h];
        for (int i = 0; i < w * h; i++) { dist[i] = int.MaxValue; prev[i] = -1; }
        
        var pq = new PriorityQueue<int, int>(); // NodeIdx, Score
        
        int startIdx = y0 * w + x0;
        int targetIdx = y1 * w + x1;
        
        dist[startIdx] = 0;
        pq.Enqueue(startIdx, 0);
        
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };
        
        while (pq.Count > 0)
        {
            int curr = pq.Dequeue();
            if (curr == targetIdx) break;
            
            int cx = curr % w, cy = curr / w;
            
            for (int i = 0; i < 4; i++)
            {
                int nx = cx + dx[i], ny = cy + dy[i];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                
                int nIdx = ny * w + nx;
                TileType t = tempMap.GetTileUnchecked(nx, ny);
                bool endpoint = nIdx == startIdx || nIdx == targetIdx;
                if (!IsGeneratedRoadPassable(tempMap, nx, ny, t, endpoint)) continue;
                
                // Base cost is 10. Roads follow valleys/passes and never
                // climb over mountains or peaks.
                if (t == TileType.Mountain || t == TileType.MountainPeak) continue;
                int elevCost = elev[nIdx] / 3276;
                int cost = 10 + elevCost;
                if (t == TileType.River) cost += 35;
                
                // Existing roads are cheap to reuse
                if (t == TileType.Road) cost = 2;
                
                if (dist[curr] + cost < dist[nIdx])
                {
                    dist[nIdx] = dist[curr] + cost;
                    prev[nIdx] = curr;
                    // For A*, add heuristic (Manhattan distance to target)
                    int hCost = (System.Math.Abs(nx - x1) + System.Math.Abs(ny - y1)) * 10;
                    pq.Enqueue(nIdx, dist[nIdx] + hCost);
                }
            }
        }
        
        // Trace back and draw road
        if (prev[targetIdx] == -1) return; // No path found (isolated by water)
        
        int currPath = targetIdx;
        while (currPath != startIdx)
        {
            int cx = currPath % w, cy = currPath / w;
            TileType t = tempMap.GetTileUnchecked(cx, cy);
            if (t != TileType.City && t != TileType.Capital && t != TileType.River
                && IsGeneratedRoadPassable(tempMap, cx, cy, t, endpoint: false))
            {
                b.Set(cx, cy, TileType.Road);
            }
            currPath = prev[currPath];
        }
    }

    private static bool IsGeneratedRoadPassable(MapState map, int x, int y, TileType t, bool endpoint)
    {
        if (t is TileType.Water or TileType.Mountain or TileType.MountainPeak) return false;
        if (endpoint || t is TileType.City or TileType.Capital or TileType.Road or TileType.River) return true;
        if (TerrainRules.IsNarrowLandCauseway(map, x, y)) return false;
        return TerrainRules.HasTwoByTwoLandFootprint(map, x, y);
    }

    /// <summary>
    /// Ensure all cities are reachable from each other. If any city is
    /// isolated, punch a path through blocking terrain.
    /// </summary>
    private static void EnsureConnectivity(MapState.Builder b, List<City> cities,
                                            int w, int h)
    {
        if (cities.Count < 2) return;

        var map = b.Build();

        // BFS from capital 1 to find all reachable tiles.
        var visited = new bool[w * h];
        var queue = new Queue<int>();
        int startIdx = cities[0].TileY * w + cities[0].TileX;
        visited[startIdx] = true;
        queue.Enqueue(startIdx);

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w, y = idx / w;

            int[][] dirs = { new[]{1,0}, new[]{-1,0}, new[]{0,1}, new[]{0,-1} };
            foreach (var d in dirs)
            {
                int nx = x + d[0], ny = y + d[1];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                int nIdx = ny * w + nx;
                if (visited[nIdx]) continue;

                TileType t = map.GetTileUnchecked(nx, ny);
                if (t == TileType.Water || t == TileType.Mountain || t == TileType.MountainPeak) continue;

                visited[nIdx] = true;
                queue.Enqueue(nIdx);
            }
        }

        // Check if all cities are reachable. If not, punch a path.
        for (int i = 1; i < cities.Count; i++)
        {
            City c = cities[i];
            int cIdx = c.TileY * w + c.TileX;
            if (visited[cIdx]) continue;

            // City is isolated. Punch a straight-line path from capital 1.
            PunchPath(b, cities[0].TileX, cities[0].TileY, c.TileX, c.TileY, w, h);
        }
    }

    /// <summary>
    /// Convert tiny water specks to plains. This is component-based on
    /// purpose: the earlier iterative "zero-neighbor" cleanup cascaded
    /// through thin lowland basins and erased all water on many seeds.
    /// A component of 1 tile is visual noise; 2+ connected tiles are kept.
    /// </summary>
    private static void CleanupIsolatedWater(MapState.Builder b, int w, int h)
    {
        var snapshot = b.Build();
        var visited = new bool[w * h];
        var queue = new Queue<int>();
        var component = new List<int>();
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int start = y * w + x;
                if (visited[start]) continue;
                if (snapshot.GetTileUnchecked(x, y) != TileType.Water) continue;

                component.Clear();
                visited[start] = true;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    component.Add(idx);
                    int cx = idx % w, cy = idx / w;

                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + dx[k], ny = cy + dy[k];
                        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                        int nIdx = ny * w + nx;
                        if (visited[nIdx]) continue;
                        if (snapshot.GetTileUnchecked(nx, ny) != TileType.Water) continue;
                        visited[nIdx] = true;
                        queue.Enqueue(nIdx);
                    }
                }

                if (component.Count <= 1)
                {
                    for (int i = 0; i < component.Count; i++)
                    {
                        int idx = component[i];
                        b.Set(idx % w, idx / w, TileType.Plains);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Final-pass peak rebuild. Peaks are not raw-height dots; they are the
    /// connected high spine of each surviving mountain range. Running this
    /// after roads/rivers/city cleanup prevents later terrain surgery from
    /// leaving peak patches stranded on range edges.
    /// </summary>
    private static void RebuildMountainPeakSpines(MapState.Builder b, int[] elev, int w, int h)
    {
        ClearExistingPeaks(b, w, h);
        var snapshot = b.Build();
        var visited = new bool[w * h];
        var component = new List<int>();
        var queue = new Queue<int>();
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int start = y * w + x;
                if (visited[start]) continue;
                if (snapshot.GetTileUnchecked(x, y) != TileType.Mountain) continue;

                component.Clear();
                visited[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    component.Add(idx);
                    int cx = idx % w, cy = idx / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + dx[k], ny = cy + dy[k];
                        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                        int nIdx = ny * w + nx;
                        if (visited[nIdx]) continue;
                        if (snapshot.GetTileUnchecked(nx, ny) != TileType.Mountain) continue;
                        visited[nIdx] = true;
                        queue.Enqueue(nIdx);
                    }
                }

                if (component.Count < 18) continue;
                PromoteConnectedPeakSpine(b, snapshot, component, elev, w, h);
            }
        }
    }

    private static void ClearExistingPeaks(MapState.Builder b, int w, int h)
    {
        var map = b.Build();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (map.GetTileUnchecked(x, y) == TileType.MountainPeak)
                    b.Set(x, y, TileType.Mountain);
    }

    private static void PromoteConnectedPeakSpine(MapState.Builder b, MapState map, List<int> component, int[] elev, int w, int h)
    {
        BuildMountainInteriorDistances(component, w, h, out bool[] inComponent, out int[] dist, out int maxDist);
        if (maxDist < 1) return;

        int minRidgeDist = maxDist <= 2 ? 1 : System.Math.Max(2, maxDist * 55 / 100);
        var candidates = CollectPeakCandidates(map, component, dist, minRidgeDist);
        if (candidates.Count < 2 && minRidgeDist > 1)
        {
            minRidgeDist--;
            candidates = CollectPeakCandidates(map, component, dist, minRidgeDist);
        }
        if (candidates.Count < 2) return;

        int seed = BestPeakSeed(candidates, dist, elev);
        int a = BestPeakEndpoint(candidates, seed, dist, elev, w);
        int c = BestPeakEndpoint(candidates, a, dist, elev, w);
        if (a == c) return;

        var path = FindPeakSpinePath(inComponent, dist, elev, minRidgeDist, a, c, w, h);
        if (path.Count < 2 && minRidgeDist > 1)
            path = FindPeakSpinePath(inComponent, dist, elev, minRidgeDist - 1, a, c, w, h);
        if (path.Count < 2) return;

        for (int i = 0; i < path.Count; i++)
        {
            int idx = path[i];
            int x = idx % w, y = idx / w;
            if (CountMountainLikeNeighbors8(map, x, y) < 4) continue;
            b.Set(x, y, TileType.MountainPeak);
        }
    }

    private static void BuildMountainInteriorDistances(List<int> component, int w, int h,
        out bool[] inComponent, out int[] dist, out int maxDist)
    {
        inComponent = new bool[w * h];
        dist = new int[w * h];
        Array.Fill(dist, -1);
        var queue = new Queue<int>();
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };

        for (int i = 0; i < component.Count; i++)
            inComponent[component[i]] = true;

        for (int i = 0; i < component.Count; i++)
        {
            int idx = component[i];
            int x = idx % w, y = idx / w;
            bool edge = false;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h || !inComponent[ny * w + nx])
                {
                    edge = true;
                    break;
                }
            }
            if (!edge) continue;
            dist[idx] = 0;
            queue.Enqueue(idx);
        }

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w, y = idx / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (!inComponent[nIdx] || dist[nIdx] >= 0) continue;
                dist[nIdx] = dist[idx] + 1;
                queue.Enqueue(nIdx);
            }
        }

        maxDist = 0;
        for (int i = 0; i < component.Count; i++)
            if (dist[component[i]] > maxDist) maxDist = dist[component[i]];
    }

    private static List<int> CollectPeakCandidates(MapState map, List<int> component, int[] dist, int minRidgeDist)
    {
        var candidates = new List<int>();
        for (int i = 0; i < component.Count; i++)
        {
            int idx = component[i];
            if (dist[idx] < minRidgeDist) continue;
            int x = idx % map.Width, y = idx / map.Width;
            if (CountMountainLikeNeighbors8(map, x, y) < 4) continue;
            candidates.Add(idx);
        }
        return candidates;
    }

    private static int BestPeakSeed(List<int> candidates, int[] dist, int[] elev)
    {
        int best = candidates[0];
        int bestScore = int.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            int idx = candidates[i];
            int score = dist[idx] * 1_000_000 + elev[idx];
            if (score <= bestScore) continue;
            bestScore = score;
            best = idx;
        }
        return best;
    }

    private static int BestPeakEndpoint(List<int> candidates, int from, int[] dist, int[] elev, int w)
    {
        int fx = from % w, fy = from / w;
        int best = from;
        int bestScore = int.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            int idx = candidates[i];
            int x = idx % w, y = idx / w;
            int manhattan = System.Math.Abs(x - fx) + System.Math.Abs(y - fy);
            int score = manhattan * 120_000 + dist[idx] * 80_000 + elev[idx];
            if (score <= bestScore) continue;
            bestScore = score;
            best = idx;
        }
        return best;
    }

    private static List<int> FindPeakSpinePath(bool[] inComponent, int[] dist, int[] elev,
        int minRidgeDist, int start, int target, int w, int h)
    {
        var result = new List<int>();
        int n = w * h;
        int[] cost = new int[n];
        int[] prev = new int[n];
        bool[] closed = new bool[n];
        Array.Fill(cost, int.MaxValue);
        Array.Fill(prev, -1);

        var pq = new PriorityQueue<int, int>();
        cost[start] = 0;
        pq.Enqueue(start, 0);
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };

        while (pq.Count > 0)
        {
            int cur = pq.Dequeue();
            if (closed[cur]) continue;
            if (cur == target) break;
            closed[cur] = true;
            int cx = cur % w, cy = cur / w;

            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k], ny = cy + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (closed[nIdx] || !inComponent[nIdx] || dist[nIdx] < minRidgeDist) continue;

                int step = PeakSpineStepCost(nIdx, dist, elev);
                int nextCost = cost[cur] + step;
                if (nextCost >= cost[nIdx]) continue;
                cost[nIdx] = nextCost;
                prev[nIdx] = cur;
                int heuristic = (System.Math.Abs(nx - target % w) + System.Math.Abs(ny - target / w)) * 200;
                pq.Enqueue(nIdx, nextCost + heuristic);
            }
        }

        if (prev[target] < 0) return result;
        int cursor = target;
        while (cursor != start)
        {
            result.Add(cursor);
            cursor = prev[cursor];
        }
        result.Add(start);
        result.Reverse();
        return result;
    }

    private static int PeakSpineStepCost(int idx, int[] dist, int[] elev)
    {
        int interiorBonus = System.Math.Min(3600, dist[idx] * 700);
        int heightCost = (65535 - elev[idx]) / 256;
        int cost = 5000 - interiorBonus + heightCost;
        return cost < 50 ? 50 : cost;
    }

    private static int CountMountainLikeNeighbors8(MapState map, int x, int y)
    {
        int count = 0;
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                int nx = x + ox, ny = y + oy;
                if ((uint)nx >= (uint)map.Width || (uint)ny >= (uint)map.Height) continue;
                if (IsMountainLike(map.GetTileUnchecked(nx, ny))) count++;
            }
        }
        return count;
    }

    private static void GenerateRivers(MapState.Builder b, int[] elev, int w, int h)
    {
        int desired = w * h < 6400 ? 1 : 2;
        int minLength = MinimumRiverLength(w, h);
        var snapshot = b.Build();
        int[] waterDistance = DistanceToWater(snapshot, w, h);

        var sources = new List<int>();
        for (int i = 0; i < w * h; i++)
        {
            TileType t = (TileType)snapshot.RawTiles[i];
            if ((t == TileType.MountainPeak || t == TileType.Mountain)
                && TryFindRiverSpring(snapshot, elev, i, w, h, out _))
                sources.Add(i);
        }
        sources.Sort((a, c) =>
        {
            int cmp = elev[c].CompareTo(elev[a]);
            return cmp != 0 ? cmp : a.CompareTo(c);
        });

        var usedSources = new List<int>();
        for (int i = 0; i < sources.Count && usedSources.Count < desired; i++)
        {
            int source = sources[i];
            if (!TryFindRiverSpring(snapshot, elev, source, w, h, out int spring)) continue;
            if (waterDistance[spring] < minLength || waterDistance[spring] == int.MaxValue) continue;
            bool tooClose = false;
            for (int j = 0; j < usedSources.Count; j++)
            {
                int sx = spring % w, sy = spring / w;
                int ox = usedSources[j] % w, oy = usedSources[j] / w;
                if (System.Math.Abs(sx - ox) + System.Math.Abs(sy - oy) < System.Math.Min(w, h) / 3)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            int length = CarveRiver(b, snapshot, elev, waterDistance, spring, w, h);
            if (length >= minLength)
            {
                usedSources.Add(spring);
                snapshot = b.Build();
            }
        }

        if (CountTiles(b.Build(), TileType.River) < 10)
        {
            snapshot = b.Build();
            for (int i = 0; i < sources.Count; i++)
            {
                int source = sources[i];
                if (!TryFindRiverSpring(snapshot, elev, source, w, h, out int spring)) continue;
                if (waterDistance[spring] < 8 || waterDistance[spring] == int.MaxValue) continue;
                int length = CarveRiver(b, snapshot, elev, waterDistance, spring, w, h);
                if (length >= 10) break;
            }
        }
    }

    private static int MinimumRiverLength(int w, int h)
        => System.Math.Max(10, System.Math.Min(w, h) / 5);

    private static bool TryFindRiverSpring(MapState map, int[] elev, int source, int w, int h, out int spring)
    {
        spring = -1;
        int x = source % w, y = source / w;
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        int bestScore = int.MaxValue;

        for (int k = 0; k < 4; k++)
        {
            int nx = x + dx[k], ny = y + dy[k];
            if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
            int nIdx = ny * w + nx;
            TileType t = map.GetTileUnchecked(nx, ny);
            if (t is TileType.Water or TileType.River or TileType.Mountain or TileType.MountainPeak
                or TileType.City or TileType.Capital or TileType.Fort)
                continue;
            if (HasNeighbor(map, nx, ny, TileType.River)) continue;
            if (CutsBetweenOpposingMountains(map, nx, ny)) continue;

            // Prefer lower neighboring foothills/lowlands as the spring
            // outlet. The mountain tile remains mountain/peak; the river
            // begins beside it, which reads as "source in the range" without
            // cutting a blue canal through the range itself.
            int score = elev[nIdx] + nIdx;
            if (score < bestScore)
            {
                bestScore = score;
                spring = nIdx;
            }
        }
        return spring >= 0;
    }

    private static int[] DistanceToWater(MapState map, int w, int h)
    {
        int[] dist = new int[w * h];
        Array.Fill(dist, int.MaxValue);
        var queue = new Queue<int>();
        for (int i = 0; i < w * h; i++)
        {
            if ((TileType)map.RawTiles[i] != TileType.Water) continue;
            dist[i] = 0;
            queue.Enqueue(i);
        }

        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w, y = idx / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (dist[nIdx] <= dist[idx] + 1) continue;
                dist[nIdx] = dist[idx] + 1;
                queue.Enqueue(nIdx);
            }
        }
        return dist;
    }

    private static int CarveRiver(MapState.Builder b, MapState snapshot, int[] elev, int[] waterDistance, int source, int w, int h)
    {
        int current = source;
        var path = new List<int>();
        var visited = new bool[w * h];
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        int previousDirection = -1;
        int straightRun = 0;
        bool reachedWater = false;

        for (int step = 0; step < w + h; step++)
        {
            int x = current % w, y = current / w;
            TileType currentTile = snapshot.GetTileUnchecked(x, y);
            if (currentTile == TileType.Water)
            {
                reachedWater = true;
                break;
            }
            if (CutsBetweenOpposingMountains(snapshot, x, y)) return 0;
            if (currentTile != TileType.Capital && currentTile != TileType.City
                && currentTile != TileType.Fort && currentTile != TileType.MountainPeak)
            {
                path.Add(current);
            }
            visited[current] = true;

            int best = -1;
            int bestScore = int.MaxValue;
            int bestDirection = -1;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (visited[nIdx]) continue;
                TileType t = snapshot.GetTileUnchecked(nx, ny);
                if (t is TileType.River or TileType.City or TileType.Capital or TileType.Fort
                    or TileType.Mountain or TileType.MountainPeak)
                    continue;
                if (HasAdjacentVisitedRiver(visited, nx, ny, current, w, h)) continue;
                if (CutsBetweenOpposingMountains(snapshot, nx, ny)) continue;
                if (t == TileType.Water)
                {
                    reachedWater = true;
                    best = -1;
                    break;
                }

                int rise = elev[nIdx] - elev[current];
                int uphillPenalty = rise <= 0 ? 0 : 12000 + rise * 3;
                int downhillBonus = rise <= 0 ? -3000 : 0;
                int sameDirectionPenalty = previousDirection == k
                    ? straightRun >= 3 ? 2_000_000
                    : straightRun >= 2 ? 90_000
                    : 0
                    : 0;
                int reversePenalty = previousDirection >= 0 && ((previousDirection + 2) & 3) == k ? 9000 : 0;
                int inertiaBonus = previousDirection == k && straightRun < 3 ? -900 : 0;
                int deterministicWobble = ((nIdx * 1103515245 + source * 12345 + step * 97) & 2047);
                int mountainAdjacencyPenalty = step > 3 && HasAdjacentMountainLike(snapshot, nx, ny) ? 8000 : 0;
                int valleyBonus = CountHigherNeighbors(elev, nIdx, w, h) * -700;

                // Distance-to-water is a gentle basin bias, not the driver.
                // Elevation and local direction dominate, which produces
                // meanders through lowlands instead of straight blue lasers.
                int score =
                    waterDistance[nIdx] * 120
                    + elev[nIdx] / 3
                    + uphillPenalty
                    + downhillBonus
                    + sameDirectionPenalty
                    + reversePenalty
                    + inertiaBonus
                    + mountainAdjacencyPenalty
                    + valleyBonus
                    + deterministicWobble;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = nIdx;
                    bestDirection = k;
                }
            }
            if (reachedWater) break;
            if (best < 0) break;
            if (bestDirection == previousDirection) straightRun++;
            else straightRun = 1;
            previousDirection = bestDirection;
            current = best;
        }

        if (!reachedWater) return 0;
        if (LongestStraightRun(path, w) > 12) return 0;
        for (int i = 0; i < path.Count; i++)
        {
            int idx = path[i];
            int x = idx % w, y = idx / w;
            b.Set(x, y, TileType.River);
        }
        return path.Count;
    }

    private static int LongestStraightRun(List<int> path, int w)
    {
        int longest = 0;
        int horizontal = 0;
        int vertical = 0;
        int previous = -1;
        for (int i = 0; i < path.Count; i++)
        {
            int idx = path[i];
            if (previous >= 0)
            {
                int delta = idx - previous;
                if (delta == 1 || delta == -1)
                {
                    horizontal++;
                    vertical = 1;
                }
                else if (delta == w || delta == -w)
                {
                    vertical++;
                    horizontal = 1;
                }
                else
                {
                    horizontal = 1;
                    vertical = 1;
                }
            }
            else
            {
                horizontal = 1;
                vertical = 1;
            }
            if (horizontal > longest) longest = horizontal;
            if (vertical > longest) longest = vertical;
            previous = idx;
        }
        return longest;
    }

    private static int CountTiles(MapState map, TileType tile)
    {
        int count = 0;
        for (int i = 0; i < map.TileCount; i++)
            if ((TileType)map.RawTiles[i] == tile) count++;
        return count;
    }

    private static void EnsureRiverTouchesMountainSource(MapState.Builder b, int w, int h)
    {
        var map = b.Build();
        bool hasRiver = false;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (map.GetTileUnchecked(x, y) != TileType.River) continue;
                hasRiver = true;
                if (HasAdjacentMountainLike(map, x, y)) return;
            }
        }
        if (!hasRiver) return;

        if (TryDirectMountainSourceConnector(b, map, w, h)) return;

        int[] prev = new int[w * h];
        Array.Fill(prev, -1);
        var queue = new Queue<int>();
        for (int i = 0; i < w * h; i++)
        {
            if ((TileType)map.RawTiles[i] != TileType.River) continue;
            prev[i] = i;
            queue.Enqueue(i);
        }

        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        int found = -1;
        int maxSearch = System.Math.Max(8, System.Math.Min(w, h) / 3);
        while (queue.Count > 0 && found < 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w, y = idx / w;
            int depth = ConnectorDepth(prev, idx);
            if (depth > maxSearch) continue;
            if (idx != prev[idx] && HasAdjacentMountainLike(map, x, y))
            {
                found = idx;
                break;
            }

            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int nIdx = ny * w + nx;
                if (prev[nIdx] >= 0) continue;
                TileType t = map.GetTileUnchecked(nx, ny);
                if (t is TileType.Water or TileType.Mountain or TileType.MountainPeak
                    or TileType.City or TileType.Capital or TileType.Fort or TileType.Road)
                    continue;
                prev[nIdx] = idx;
                queue.Enqueue(nIdx);
            }
        }

        if (found < 0) return;
        int cursor = found;
        while (prev[cursor] != cursor)
        {
            int x = cursor % w, y = cursor / w;
            b.Set(x, y, TileType.River);
            cursor = prev[cursor];
        }
    }

    private static bool TryDirectMountainSourceConnector(MapState.Builder b, MapState map, int w, int h)
    {
        int bestRiver = -1;
        int bestMountain = -1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < w * h; i++)
        {
            if ((TileType)map.RawTiles[i] != TileType.River) continue;
            int rx = i % w, ry = i / w;
            for (int m = 0; m < w * h; m++)
            {
                if (!IsMountainLike((TileType)map.RawTiles[m])) continue;
                int mx = m % w, my = m / w;
                int d = System.Math.Abs(rx - mx) + System.Math.Abs(ry - my);
                if (d >= bestDistance) continue;
                bestDistance = d;
                bestRiver = i;
                bestMountain = m;
            }
        }

        if (bestRiver < 0 || bestMountain < 0 || bestDistance > 4) return false;

        int x = bestRiver % w, y = bestRiver / w;
        int targetX = bestMountain % w, targetY = bestMountain / w;
        while (System.Math.Abs(x - targetX) + System.Math.Abs(y - targetY) > 1)
        {
            int nx = x;
            int ny = y;
            if (System.Math.Abs(targetX - x) >= System.Math.Abs(targetY - y))
                nx += targetX > x ? 1 : -1;
            else
                ny += targetY > y ? 1 : -1;

            TileType t = map.GetTileUnchecked(nx, ny);
            if (t is TileType.City or TileType.Capital or TileType.Fort or TileType.Road
                or TileType.Mountain or TileType.MountainPeak)
                return false;
            b.Set(nx, ny, TileType.River);
            x = nx;
            y = ny;
        }
        return true;
    }

    private static int ConnectorDepth(int[] prev, int idx)
    {
        int depth = 0;
        int cursor = idx;
        while (prev[cursor] != cursor)
        {
            depth++;
            cursor = prev[cursor];
            if (depth > 128) break;
        }
        return depth;
    }

    private static void EnsureRiverTouchesWaterSink(MapState.Builder b, int w, int h)
    {
        var map = b.Build();
        bool hasRiver = false;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (map.GetTileUnchecked(x, y) != TileType.River) continue;
                hasRiver = true;
                if (HasAdjacentWater(map, x, y)) return;
            }
        }
        if (!hasRiver) return;

        int bestRiver = -1;
        int bestWater = -1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < w * h; i++)
        {
            if ((TileType)map.RawTiles[i] != TileType.River) continue;
            int rx = i % w, ry = i / w;
            for (int target = 0; target < w * h; target++)
            {
                if ((TileType)map.RawTiles[target] != TileType.Water) continue;
                int wx = target % w, wy = target / w;
                int d = System.Math.Abs(rx - wx) + System.Math.Abs(ry - wy);
                if (d >= bestDistance) continue;
                bestDistance = d;
                bestRiver = i;
                bestWater = target;
            }
        }

        if (bestRiver < 0 || bestWater < 0 || bestDistance > 5) return;
        int cursorX = bestRiver % w, cursorY = bestRiver / w;
        int targetX = bestWater % w, targetY = bestWater / w;
        while (System.Math.Abs(cursorX - targetX) + System.Math.Abs(cursorY - targetY) > 1)
        {
            int nx = cursorX;
            int ny = cursorY;
            if (System.Math.Abs(targetX - cursorX) >= System.Math.Abs(targetY - cursorY))
                nx += targetX > cursorX ? 1 : -1;
            else
                ny += targetY > cursorY ? 1 : -1;

            TileType t = map.GetTileUnchecked(nx, ny);
            if (t is TileType.City or TileType.Capital or TileType.Fort or TileType.Road
                or TileType.Mountain or TileType.MountainPeak)
                return;
            b.Set(nx, ny, TileType.River);
            cursorX = nx;
            cursorY = ny;
        }
    }

    private static bool HasAdjacentWater(MapState map, int x, int y)
    {
        if (x > 0 && map.GetTileUnchecked(x - 1, y) == TileType.Water) return true;
        if (x + 1 < map.Width && map.GetTileUnchecked(x + 1, y) == TileType.Water) return true;
        if (y > 0 && map.GetTileUnchecked(x, y - 1) == TileType.Water) return true;
        if (y + 1 < map.Height && map.GetTileUnchecked(x, y + 1) == TileType.Water) return true;
        return false;
    }

    private static void BreakLongStraightRiverRuns(MapState.Builder b, int w, int h)
    {
        bool changed;
        int guard = 0;
        do
        {
            changed = false;
            var map = b.Build();
            for (int y = 0; y < h && !changed; y++)
            {
                int runStart = -1;
                for (int x = 0; x <= w; x++)
                {
                    bool river = x < w && map.GetTileUnchecked(x, y) == TileType.River;
                    if (river)
                    {
                        if (runStart < 0) runStart = x;
                    }
                    else if (runStart >= 0)
                    {
                        int length = x - runStart;
                        if (length > 12 && TryDoglegHorizontalRiver(b, map, runStart, x - 1, y, w, h))
                            changed = true;
                        runStart = -1;
                    }
                }
            }

            map = b.Build();
            for (int x = 0; x < w && !changed; x++)
            {
                int runStart = -1;
                for (int y = 0; y <= h; y++)
                {
                    bool river = y < h && map.GetTileUnchecked(x, y) == TileType.River;
                    if (river)
                    {
                        if (runStart < 0) runStart = y;
                    }
                    else if (runStart >= 0)
                    {
                        int length = y - runStart;
                        if (length > 12 && TryDoglegVerticalRiver(b, map, x, runStart, y - 1, w, h))
                            changed = true;
                        runStart = -1;
                    }
                }
            }
            guard++;
        }
        while (changed && guard < 16);
    }

    private static bool TryDoglegVerticalRiver(MapState.Builder b, MapState map, int x, int y0, int y1, int w, int h)
    {
        int y = (y0 + y1) / 2;
        if (y <= y0 || y >= y1) return false;
        for (int sideTry = 0; sideTry < 2; sideTry++)
        {
            int side = sideTry == 0 ? 1 : -1;
            int sx = x + side;
            if ((uint)sx >= (uint)w) continue;
            if (!CanDoglegRiverTile(map, sx, y - 1)
                || !CanDoglegRiverTile(map, sx, y)
                || !CanDoglegRiverTile(map, sx, y + 1))
                continue;
            b.Set(x, y, TileType.Plains);
            b.Set(sx, y - 1, TileType.River);
            b.Set(sx, y, TileType.River);
            b.Set(sx, y + 1, TileType.River);
            return true;
        }
        return false;
    }

    private static bool TryDoglegHorizontalRiver(MapState.Builder b, MapState map, int x0, int x1, int y, int w, int h)
    {
        int x = (x0 + x1) / 2;
        if (x <= x0 || x >= x1) return false;
        for (int sideTry = 0; sideTry < 2; sideTry++)
        {
            int side = sideTry == 0 ? 1 : -1;
            int sy = y + side;
            if ((uint)sy >= (uint)h) continue;
            if (!CanDoglegRiverTile(map, x - 1, sy)
                || !CanDoglegRiverTile(map, x, sy)
                || !CanDoglegRiverTile(map, x + 1, sy))
                continue;
            b.Set(x, y, TileType.Plains);
            b.Set(x - 1, sy, TileType.River);
            b.Set(x, sy, TileType.River);
            b.Set(x + 1, sy, TileType.River);
            return true;
        }
        return false;
    }

    private static bool CanDoglegRiverTile(MapState map, int x, int y)
    {
        if ((uint)x >= (uint)map.Width || (uint)y >= (uint)map.Height) return false;
        TileType t = map.GetTileUnchecked(x, y);
        return t is TileType.Plains or TileType.Forest or TileType.River;
    }

    private static bool CutsBetweenOpposingMountains(MapState map, int x, int y)
    {
        bool west = x > 0 && IsMountainLike(map.GetTileUnchecked(x - 1, y));
        bool east = x + 1 < map.Width && IsMountainLike(map.GetTileUnchecked(x + 1, y));
        bool north = y > 0 && IsMountainLike(map.GetTileUnchecked(x, y - 1));
        bool south = y + 1 < map.Height && IsMountainLike(map.GetTileUnchecked(x, y + 1));
        return (west && east) || (north && south);
    }

    private static bool IsMountainLike(TileType t) => t is TileType.Mountain or TileType.MountainPeak;

    private static bool HasNeighbor(MapState map, int x, int y, TileType tile)
    {
        if (x > 0 && map.GetTileUnchecked(x - 1, y) == tile) return true;
        if (x + 1 < map.Width && map.GetTileUnchecked(x + 1, y) == tile) return true;
        if (y > 0 && map.GetTileUnchecked(x, y - 1) == tile) return true;
        if (y + 1 < map.Height && map.GetTileUnchecked(x, y + 1) == tile) return true;
        return false;
    }

    private static bool HasAdjacentVisitedRiver(bool[] visited, int x, int y, int current, int w, int h)
    {
        int left = y * w + x - 1;
        int right = y * w + x + 1;
        int up = (y - 1) * w + x;
        int down = (y + 1) * w + x;
        if (x > 0 && left != current && visited[left]) return true;
        if (x + 1 < w && right != current && visited[right]) return true;
        if (y > 0 && up != current && visited[up]) return true;
        if (y + 1 < h && down != current && visited[down]) return true;
        return false;
    }

    private static bool HasAdjacentMountainLike(MapState map, int x, int y)
    {
        if (x > 0 && IsMountainLike(map.GetTileUnchecked(x - 1, y))) return true;
        if (x + 1 < map.Width && IsMountainLike(map.GetTileUnchecked(x + 1, y))) return true;
        if (y > 0 && IsMountainLike(map.GetTileUnchecked(x, y - 1))) return true;
        if (y + 1 < map.Height && IsMountainLike(map.GetTileUnchecked(x, y + 1))) return true;
        return false;
    }

    private static int CountHigherNeighbors(int[] elev, int idx, int w, int h)
    {
        int x = idx % w, y = idx / w;
        int e = elev[idx];
        int count = 0;
        if (x > 0 && elev[idx - 1] >= e) count++;
        if (x + 1 < w && elev[idx + 1] >= e) count++;
        if (y > 0 && elev[idx - w] >= e) count++;
        if (y + 1 < h && elev[idx + w] >= e) count++;
        return count;
    }

    /// <summary>
    /// Punch a straight-line path through any blocking terrain (Water/Mountain → Plains).
    /// Used as a last resort to guarantee connectivity.
    /// </summary>
    private static void PunchPath(MapState.Builder b, int x0, int y0, int x1, int y1, int w, int h)
    {
        int cx = x0, cy = y0;
        int maxSteps = w + h + 50;

        for (int step = 0; step < maxSteps; step++)
        {
            if (cx == x1 && cy == y1) break;

            var temp = b.Build();
            TileType t = temp.GetTileUnchecked(cx, cy);
            if (t == TileType.Water || t == TileType.Mountain || t == TileType.MountainPeak)
                PunchLandPatch(b, temp, cx, cy, w, h);

            // Move toward target.
            int dx = x1 - cx, dy = y1 - cy;
            if (System.Math.Abs(dx) >= System.Math.Abs(dy))
                cx += dx > 0 ? 1 : -1;
            else
                cy += dy > 0 ? 1 : -1;
        }
    }

    private static void PunchLandPatch(MapState.Builder b, MapState map, int cx, int cy, int w, int h)
    {
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                int x = cx + ox, y = cy + oy;
                if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                TileType t = map.GetTileUnchecked(x, y);
                if (t is TileType.Water or TileType.Mountain or TileType.MountainPeak)
                    b.Set(x, y, TileType.Plains);
            }
        }
    }
}
