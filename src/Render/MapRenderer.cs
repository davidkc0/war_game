namespace WarGame.Render;

using Godot;
using System.Collections.Generic;
using WarGame.Sim.State;
using WarGame.Sim.Systems;

// Pure draw helpers — no state of their own. All inputs come from
// GameState (read-only). The renderer never writes to sim state; that
// boundary is what keeps lockstep + replay clean.
//
// Phase 1 visual rules per PLAN.md §1.6:
//   - Geometric primitives (circles for light, hexagons for heavy).
//   - Subtle drop shadow on units (soft, not theatrical).
//   - Border edges crisp; territory fill barely-tinted.
//   - Terrain tiles get a subtle top-to-bottom gradient (10–15% lightness).
//   - No flat-shading bling; restraint over juice.
//
// Tile pixel size is chosen so a 30x20 grid roughly fills a 1280x720
// viewport. Future phases may pan/zoom; for Phase 1 the camera is fixed.
public static class MapRenderer
{
    private enum TerrainFamily
    {
        Plains,
        Forest,
        Mountain,
        Water,
    }

    // Sized so the 30x20 test map fills the 1280x720 viewport while leaving
    // room for the 48-px top HUD and a status line at the bottom.
    //   Max width  : 1280 / 30 = 42.6 px/tile
    //   Max height : (720 - 48 - 24) / 20 = 32.4 px/tile
    // Square tiles → take the smaller bound. Phase 3 introduces a real
    // camera with pan/zoom; until then this fixed scale is sufficient.
    public const float TilePx = 32f;
    public const float UnitRadiusLight = TilePx * 0.32f;
    public const float UnitRadiusHeavy = TilePx * 0.42f;
    public const float CityMarkerHalf  = TilePx * 0.40f;
    public const float CapitalMarkerHalf = TilePx * 0.46f;

    private static readonly string[] CapitalOverlayPaths =
    [
        "res://assets/images/capital.png",
        "res://assets/images/captial.png",
    ];
    private static readonly string[] CityOverlayPaths =
    [
        "res://assets/images/city.png",
    ];
    private static readonly string[] ForestTilePaths =
    [
        "res://assets/images/forest.png",
    ];
    private static readonly string[] TreeBottomPaths =
    [
        "res://assets/images/tree_bottom.png",
    ];
    private static readonly string[] TreeMidPaths =
    [
        "res://assets/images/tree_mid.png",
    ];
    private static readonly string[] TreeTopPaths =
    [
        "res://assets/images/tree_top.png",
    ];
    private static readonly string[] PlainsTilePaths =
    [
        "res://assets/images/plains.png",
    ];
    private static readonly string[] WaterTilePaths =
    [
        "res://assets/images/water.png",
    ];
    private static readonly string[] RiverTilePaths =
    [
        "res://assets/images/river.png",
    ];
    private static readonly string[] WaterShoreTopPaths =
    [
        "res://assets/images/water_shore_top.png",
    ];
    private static readonly string[] WaterShoreCurvePaths =
    [
        "res://assets/images/water_shore_curve.png",
    ];
    private static readonly string[] WaterShoreCurveClosePaths =
    [
        "res://assets/images/water_shore_curve_close.png",
    ];
    private static readonly string[] WaterTipPaths =
    [
        "res://assets/images/water_tip.png",
    ];
    private static readonly string[] BridgeTilePaths =
    [
        "res://assets/images/bridge.png",
    ];
    private static readonly string[] MountainTilePaths =
    [
        "res://assets/images/mountains.png",
        "res://assets/images/mountain.png",
    ];
    private static readonly string[] PeakTilePaths =
    [
        "res://assets/images/peak.png",
        "res://assets/images/peak1.png",
        "res://assets/images/peaj.png",
    ];
    private static readonly string[] FortOverlayPaths =
    [
        "res://assets/images/fort.png",
        "res://assets/images/for.png",
    ];
    private static readonly string[] RoadStraightPaths =
    [
        "res://assets/images/road_straight.png",
    ];
    private static readonly string[] RoadDeadEndPaths =
    [
        "res://assets/images/road_deadend.png",
    ];
    private static readonly string[] RoadTriCrossSouthPaths =
    [
        "res://assets/images/road_tricross_south.png",
    ];
    private static readonly string[] RoadTriCrossNorthPaths =
    [
        "res://assets/images/road_tricross_north.png",
    ];
    private static readonly string[] RoadCrossPaths =
    [
        "res://assets/images/road_cross.png",
    ];
    private static readonly string[] RoadCurveRightPaths =
    [
        "res://assets/images/road_curve_right.png",
    ];
    private static readonly string[] RoadCurveLeftPaths =
    [
        "res://assets/images/road_curve_left.png",
    ];
    private static Texture2D? _capitalOverlay;
    private static bool _capitalOverlayLoadAttempted;
    private static Texture2D? _cityOverlay;
    private static bool _cityOverlayLoadAttempted;
    private static Texture2D? _forestTile;
    private static bool _forestTileLoadAttempted;
    private static Texture2D? _treeBottomTile;
    private static bool _treeBottomTileLoadAttempted;
    private static Texture2D? _treeMidTile;
    private static bool _treeMidTileLoadAttempted;
    private static Texture2D? _treeTopTile;
    private static bool _treeTopTileLoadAttempted;
    private static Texture2D? _plainsTile;
    private static bool _plainsTileLoadAttempted;
    private static Texture2D? _waterTile;
    private static bool _waterTileLoadAttempted;
    private static Texture2D? _riverTile;
    private static bool _riverTileLoadAttempted;
    private static Texture2D? _waterShoreTopTile;
    private static bool _waterShoreTopTileLoadAttempted;
    private static Texture2D? _waterShoreCurveTile;
    private static bool _waterShoreCurveTileLoadAttempted;
    private static Texture2D? _waterShoreCurveCloseTile;
    private static bool _waterShoreCurveCloseTileLoadAttempted;
    private static Texture2D? _waterTipTile;
    private static bool _waterTipTileLoadAttempted;
    private static Texture2D? _bridgeTile;
    private static bool _bridgeTileLoadAttempted;
    private static Texture2D? _mountainTile;
    private static bool _mountainTileLoadAttempted;
    private static Texture2D? _peakTile;
    private static bool _peakTileLoadAttempted;
    private static Texture2D? _fortOverlay;
    private static bool _fortOverlayLoadAttempted;
    private static Texture2D? _roadStraight;
    private static bool _roadStraightLoadAttempted;
    private static Texture2D? _roadDeadEnd;
    private static bool _roadDeadEndLoadAttempted;
    private static Texture2D? _roadTriCrossSouth;
    private static bool _roadTriCrossSouthLoadAttempted;
    private static Texture2D? _roadTriCrossNorth;
    private static bool _roadTriCrossNorthLoadAttempted;
    private static Texture2D? _roadCross;
    private static bool _roadCrossLoadAttempted;
    private static Texture2D? _roadCurveRight;
    private static bool _roadCurveRightLoadAttempted;
    private static Texture2D? _roadCurveLeft;
    private static bool _roadCurveLeftLoadAttempted;
    private static readonly Dictionary<(ulong Id, TileSpriteTransform Transform), Texture2D> _transformedTextures = new();

    private enum TileSpriteTransform : byte
    {
        None,
        FlipX,
        FlipY,
        FlipXY,
        RotateClockwise,
        RotateCounterClockwise,
    }

    public static Vector2 TileTopLeft(int tileX, int tileY, Vector2 origin)
        => origin + new Vector2(tileX * TilePx, tileY * TilePx);

    public static Vector2 TileCenter(int tileX, int tileY, Vector2 origin)
        => origin + new Vector2((tileX + 0.5f) * TilePx, (tileY + 0.5f) * TilePx);

    public static (int tileX, int tileY) ScreenToTile(Vector2 screen, Vector2 origin)
    {
        Vector2 local = (screen - origin) / TilePx;
        return ((int)Mathf.Floor(local.X), (int)Mathf.Floor(local.Y));
    }

    /// <summary>
    /// Where a unit *visually* sits, accounting for sub-tile path interpolation
    /// and ease-out smoothing. Use this for both rendering and click-hit-testing
    /// so the selection ring stays glued to the moving unit and clicks register
    /// where the unit actually is on screen.
    /// </summary>
    public static Vector2 UnitVisualCenter(in Unit u, in MapState map, Vector2 origin)
    {
        Vector2 anchor = TileCenter(u.TileX, u.TileY, origin);
        if (u.Path is null || u.Path.Count == 0) return anchor;

        int next = u.Path[0];
        int nx = next % map.Width, ny = next / map.Width;
        Vector2 nextCenter = TileCenter(nx, ny, origin);
        float frac = (float)(u.ProgressRaw / (double)WarGame.Sim.Math.FP.OneRaw);
        // Quadratic ease-out: decelerates as the unit approaches the next
        // tile center. Render-only — the sim still steps linearly. Makes
        // unit movement feel weighty and intentional per PLAN.md §1.6 #5.
        frac = 1f - (1f - frac) * (1f - frac);
        return anchor.Lerp(nextCenter, frac);
    }

    public static float UnitRadius(UnitType t)
        => t == UnitType.Heavy ? UnitRadiusHeavy : UnitRadiusLight;

    public static void Draw(CanvasItem canvas, in GameState state, Vector2 origin)
        => Draw(canvas, state, origin, PlayerId.None);

    public static void Draw(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        DrawTerrain(canvas, state, origin, viewer);
        DrawTerrainEdges(canvas, state, origin, viewer);
        DrawElevationShadows(canvas, state, origin, viewer);
        DrawTerrainGrid(canvas, state, origin);
        DrawTerrainOverlays(canvas, state, origin, viewer);
        DrawBorders(canvas, state, origin, viewer);
        DrawCities(canvas, state, origin, viewer);
        DrawPendingForts(canvas, state, origin, viewer);
        DrawPendingRoads(canvas, state, origin, viewer);
        DrawUnits(canvas, state, origin, viewer);
    }

    // -------- 4c) Pending road/bridge construction ----------------------
    private static void DrawPendingRoads(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        if (state.PendingRoads is null || state.PendingRoads.Count == 0) return;

        for (int i = 0; i < state.PendingRoads.Count; i++)
        {
            RoadOrder r = state.PendingRoads[i];
            if (r.Path is null) continue;
            for (int p = r.CurrentPathIndex; p < r.Path.Count; p++)
            {
                int flat = r.Path[p];
                int x = flat % state.Map.Width, y = flat / state.Map.Width;
                if (!FogOfWar.IsVisible(state, viewer, x, y)) continue;
                TileType t = state.Map.GetTileUnchecked(x, y);
                Color c = WarGame.Sim.Systems.Pathfinding.IsBridgeTerrain(t) ? Theme.BridgePreview : Theme.RoadPreview;
                c.A *= p == r.CurrentPathIndex ? 0.85f : 0.45f;
                Rect2 inset = new(
                    TileTopLeft(x, y, origin) + new Vector2(TilePx * 0.18f, TilePx * 0.18f),
                    new Vector2(TilePx * 0.64f, TilePx * 0.64f));
                canvas.DrawRect(inset, c);
            }

            if (r.CurrentPathIndex < r.Path.Count && r.TicksRemainingOnTile > 0)
            {
                int flat = r.Path[r.CurrentPathIndex];
                int x = flat % state.Map.Width, y = flat / state.Map.Width;
                if (!FogOfWar.IsVisible(state, viewer, x, y)) continue;
                TileType t = state.Map.GetTileUnchecked(x, y);
                int total = WarGame.Sim.Systems.RoadConstruction.BuildTicksFor(t);
                float frac = 1f - Mathf.Clamp((float)r.TicksRemainingOnTile / total, 0f, 1f);
                Vector2 tl = TileTopLeft(x, y, origin) + new Vector2(TilePx * 0.12f, TilePx * 0.78f);
                float barW = TilePx * 0.76f, barH = 4f;
                Color bg = Theme.HudPanel; bg.A = 0.85f;
                canvas.DrawRect(new Rect2(tl, new Vector2(barW, barH)), bg);
                canvas.DrawRect(new Rect2(tl, new Vector2(barW * frac, barH)),
                    WarGame.Sim.Systems.Pathfinding.IsBridgeTerrain(t) ? Theme.Bridge : Theme.Road);
            }
        }
    }

    // -------- 1) Terrain backdrop ------------------------------------------
    private static void DrawTerrain(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                VisibilityState vis = FogOfWar.GetVisibility(state, viewer, x, y);
                TileType t = vis == VisibilityState.Hidden
                    ? TileType.Plains
                    : FogOfWar.GetKnownTileType(state, viewer, x, y);
                TileType baseTile = VisualBaseTile(state, viewer, x, y, t);
                Color baseColor = TerrainVisualColor(baseTile, x, y, vis);
                Vector2 tl = TileTopLeft(x, y, origin);

                // Base fill.
                Rect2 full = new(tl, new Vector2(TilePx, TilePx));
                canvas.DrawRect(full, baseColor);
                if (vis != VisibilityState.Hidden)
                    DrawTerrainTexture(canvas, state, origin, viewer, x, y, t, baseTile, vis, full);
            }
        }
    }

    private static Color TerrainVisualColor(TileType baseTile, int x, int y, VisibilityState vis)
    {
        if (vis == VisibilityState.Hidden) return Theme.FogHidden;

        Color color = Theme.ForTile(baseTile);
        float variance = (TileHash01(x, y, baseTile, 11) - 0.5f) * 0.10f;
        color = variance >= 0f
            ? color.Lightened(variance)
            : color.Darkened(-variance);

        if (vis == VisibilityState.Explored)
            color = Dim(color, 0.42f);

        return color;
    }

    private static void DrawTerrainTexture(
        CanvasItem canvas,
        in GameState state,
        Vector2 origin,
        PlayerId viewer,
        int x,
        int y,
        TileType tile,
        TileType baseTile,
        VisibilityState vis,
        Rect2 full)
    {
        float alpha = vis == VisibilityState.Explored
            ? Theme.TerrainTextureAlphaExplored
            : Theme.TerrainTextureAlpha;

        switch (TileFamily(baseTile))
        {
            case TerrainFamily.Plains:
                DrawPlainsTexture(canvas, full, x, y, alpha);
                break;
            case TerrainFamily.Forest:
                DrawForestTexture(canvas, full, x, y, alpha);
                break;
            case TerrainFamily.Mountain:
                DrawMountainTexture(canvas, full, x, y, alpha, vis);
                break;
            case TerrainFamily.Water:
                if (tile == TileType.Water)
                    DrawWaterDetail(canvas, full, x, y, vis);
                break;
        }
    }

    private static void DrawPlainsTexture(CanvasItem canvas, Rect2 full, int x, int y, float alpha)
    {
        Color mark = Theme.PlainsHighlight;
        mark.A = alpha * 0.75f;
        for (int i = 0; i < 2; i++)
        {
            float px = full.Position.X + TilePx * (0.20f + TileHash01(x, y, TileType.Plains, 30 + i) * 0.60f);
            float py = full.Position.Y + TilePx * (0.20f + TileHash01(x, y, TileType.Plains, 40 + i) * 0.58f);
            float len = TilePx * (0.08f + TileHash01(x, y, TileType.Plains, 50 + i) * 0.07f);
            canvas.DrawLine(new Vector2(px, py), new Vector2(px + len, py - len * 0.35f), mark, 1f, true);
        }
    }

    private static void DrawForestTexture(CanvasItem canvas, Rect2 full, int x, int y, float alpha)
    {
        Color light = Theme.ForestHighlight;
        light.A = alpha * 0.95f;
        Color dark = Theme.Forest.Darkened(0.18f);
        dark.A = alpha * 0.85f;

        for (int i = 0; i < 4; i++)
        {
            float px = full.Position.X + TilePx * (0.16f + TileHash01(x, y, TileType.Forest, 60 + i) * 0.68f);
            float py = full.Position.Y + TilePx * (0.16f + TileHash01(x, y, TileType.Forest, 70 + i) * 0.68f);
            float radius = TilePx * (0.035f + TileHash01(x, y, TileType.Forest, 80 + i) * 0.025f);
            canvas.DrawCircle(new Vector2(px, py), radius, (i & 1) == 0 ? light : dark);
        }
    }

    private static void DrawMountainTexture(CanvasItem canvas, Rect2 full, int x, int y, float alpha, VisibilityState vis)
    {
        Color shade = Theme.MountainShadow;
        shade.A = (vis == VisibilityState.Explored ? 0.08f : 0.16f) + alpha * 0.35f;
        Color light = Theme.Mountain.Lightened(0.10f);
        light.A = alpha * 0.85f;

        Vector2 c = full.Position + full.Size * 0.5f;
        float skew = (TileHash01(x, y, TileType.Mountain, 90) - 0.5f) * TilePx * 0.12f;
        Vector2[] shadowFacet =
        [
            c + new Vector2(-TilePx * 0.02f + skew, -TilePx * 0.18f),
            c + new Vector2(TilePx * 0.26f + skew, TilePx * 0.18f),
            c + new Vector2(TilePx * 0.05f + skew, TilePx * 0.25f),
        ];
        canvas.DrawColoredPolygon(shadowFacet, shade);
        canvas.DrawLine(
            c + new Vector2(-TilePx * 0.18f + skew, TilePx * 0.10f),
            c + new Vector2(TilePx * 0.02f + skew, -TilePx * 0.16f),
            light,
            1f,
            true);
    }

    private static void DrawWaterDetail(CanvasItem canvas, Rect2 full, int x, int y, VisibilityState vis)
    {
        Color wave = Theme.WaterWave;
        float shimmer = 0f;
        if (vis == VisibilityState.Visible)
        {
            float time = (float)(Time.GetTicksMsec() / 1000.0);
            shimmer = 0.35f + 0.25f * Mathf.Sin(time * 0.65f + TileHash01(x, y, TileType.Water, 100) * Mathf.Tau);
        }
        wave.A = vis == VisibilityState.Explored
            ? Theme.WaterWaveAlphaExplored
            : Theme.WaterWaveAlpha * (0.65f + shimmer);

        for (int i = 0; i < 2; i++)
        {
            float px = full.Position.X + TilePx * (0.18f + TileHash01(x, y, TileType.Water, 110 + i) * 0.48f);
            float py = full.Position.Y + TilePx * (0.24f + TileHash01(x, y, TileType.Water, 120 + i) * 0.45f);
            float len = TilePx * (0.18f + TileHash01(x, y, TileType.Water, 130 + i) * 0.16f);
            canvas.DrawLine(new Vector2(px, py), new Vector2(px + len, py), wave, 1.2f, true);
        }
    }

    private static void DrawTerrainEdges(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                VisibilityState vis = FogOfWar.GetVisibility(state, viewer, x, y);
                if (vis == VisibilityState.Hidden) continue;

                TerrainFamily family = KnownTerrainFamily(state, viewer, x, y);
                Vector2 tl = TileTopLeft(x, y, origin);
                DrawTerrainTransition(canvas, state, viewer, x + 1, y, family, vis,
                    tl + new Vector2(TilePx, 0f), tl + new Vector2(TilePx, TilePx));
                DrawTerrainTransition(canvas, state, viewer, x, y + 1, family, vis,
                    tl + new Vector2(0f, TilePx), tl + new Vector2(TilePx, TilePx));
            }
        }
    }

    private static void DrawTerrainTransition(
        CanvasItem canvas,
        in GameState state,
        PlayerId viewer,
        int nx,
        int ny,
        TerrainFamily family,
        VisibilityState vis,
        Vector2 a,
        Vector2 b)
    {
        if (!state.Map.InBounds(nx, ny)) return;
        VisibilityState otherVis = FogOfWar.GetVisibility(state, viewer, nx, ny);
        if (otherVis == VisibilityState.Hidden) return;

        TerrainFamily other = KnownTerrainFamily(state, viewer, nx, ny);
        if (family == other) return;

        Color edge = TerrainTransitionColor(family, other);
        edge.A = Theme.TerrainTransitionAlpha * (vis == VisibilityState.Explored || otherVis == VisibilityState.Explored ? 0.45f : 1f);
        canvas.DrawLine(a, b, edge, TerrainEdgeWidth(family, other), true);
    }

    private static void DrawElevationShadows(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                VisibilityState vis = FogOfWar.GetVisibility(state, viewer, x, y);
                if (vis == VisibilityState.Hidden) continue;

                TerrainFamily family = KnownTerrainFamily(state, viewer, x, y);
                if (family is not (TerrainFamily.Forest or TerrainFamily.Mountain)) continue;

                Vector2 tl = TileTopLeft(x, y, origin);
                Color shadow = Theme.TerrainShadow;
                shadow.A = (family == TerrainFamily.Mountain ? Theme.TerrainShadowAlpha : Theme.TerrainShadowAlpha * 0.62f)
                    * (vis == VisibilityState.Explored ? 0.45f : 1f);
                float width = family == TerrainFamily.Mountain ? 3f : 2f;

                if (!SameKnownFamily(state, viewer, x, y + 1, family))
                    canvas.DrawLine(tl + new Vector2(0f, TilePx), tl + new Vector2(TilePx, TilePx), shadow, width, true);
                if (!SameKnownFamily(state, viewer, x + 1, y, family))
                    canvas.DrawLine(tl + new Vector2(TilePx, 0f), tl + new Vector2(TilePx, TilePx), shadow, width, true);
            }
        }
    }

    private static Color TerrainTransitionColor(TerrainFamily a, TerrainFamily b)
    {
        if (a == TerrainFamily.Water || b == TerrainFamily.Water)
            return Theme.WaterHighlight.Darkened(0.22f);
        if (a == TerrainFamily.Mountain || b == TerrainFamily.Mountain)
            return Theme.MountainShadow;
        if (a == TerrainFamily.Forest || b == TerrainFamily.Forest)
            return Theme.Forest.Darkened(0.24f);
        return Theme.TerrainTransition;
    }

    private static float TerrainEdgeWidth(TerrainFamily a, TerrainFamily b)
        => a == TerrainFamily.Water || b == TerrainFamily.Water ? 2f : 1.5f;

    private static bool SameKnownFamily(in GameState state, PlayerId viewer, int x, int y, TerrainFamily family)
    {
        if (!state.Map.InBounds(x, y)) return false;
        if (FogOfWar.GetVisibility(state, viewer, x, y) == VisibilityState.Hidden) return false;
        return KnownTerrainFamily(state, viewer, x, y) == family;
    }

    private static TerrainFamily KnownTerrainFamily(in GameState state, PlayerId viewer, int x, int y)
    {
        TileType tile = FogOfWar.GetKnownTileType(state, viewer, x, y);
        TileType baseTile = VisualBaseTile(state, viewer, x, y, tile);
        return TileFamily(baseTile);
    }

    private static TerrainFamily TileFamily(TileType tile) => tile switch
    {
        TileType.Forest => TerrainFamily.Forest,
        TileType.Mountain or TileType.MountainPeak => TerrainFamily.Mountain,
        TileType.Water or TileType.River or TileType.Bridge => TerrainFamily.Water,
        _ => TerrainFamily.Plains,
    };

    private static float TileHash01(int x, int y, TileType tile, int salt)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)x) * 16777619u;
            h = (h ^ (uint)y) * 16777619u;
            h = (h ^ (uint)tile) * 16777619u;
            h = (h ^ (uint)salt) * 16777619u;
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFFu) / 16777215f;
        }
    }

    private static void DrawTerrainGrid(CanvasItem canvas, in GameState state, Vector2 origin)
    {
        Color grid = Theme.GridLine;
        float widthPx = 1f;
        float mapW = state.Map.Width * TilePx;
        float mapH = state.Map.Height * TilePx;

        for (int x = 0; x <= state.Map.Width; x++)
        {
            float px = origin.X + x * TilePx;
            canvas.DrawLine(new Vector2(px, origin.Y), new Vector2(px, origin.Y + mapH), grid, widthPx);
        }

        for (int y = 0; y <= state.Map.Height; y++)
        {
            float py = origin.Y + y * TilePx;
            canvas.DrawLine(new Vector2(origin.X, py), new Vector2(origin.X + mapW, py), grid, widthPx);
        }
    }

    private static void DrawTerrainOverlays(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                VisibilityState vis = FogOfWar.GetVisibility(state, viewer, x, y);
                if (vis == VisibilityState.Hidden) continue;

                TileType t = FogOfWar.GetKnownTileType(state, viewer, x, y);
                DrawTerrainOverlay(canvas, state, origin, viewer, x, y, t, vis);
            }
        }
    }

    private static TileType VisualBaseTile(
        in GameState state,
        PlayerId viewer,
        int x,
        int y,
        TileType tile) => tile switch
    {
        TileType.Road => TileType.Plains,
        TileType.Bridge => TileType.Water,
        TileType.River => RiverBaseTile(state, viewer, x, y),
        TileType.MountainPeak => TileType.Mountain,
        _ => tile,
    };

    private static TileType RiverBaseTile(in GameState state, PlayerId viewer, int x, int y)
    {
        int plains = 1, forest = 0, mountain = 0;
        CountRiverBaseNeighbor(state, viewer, x, y - 1, ref plains, ref forest, ref mountain);
        CountRiverBaseNeighbor(state, viewer, x + 1, y, ref plains, ref forest, ref mountain);
        CountRiverBaseNeighbor(state, viewer, x, y + 1, ref plains, ref forest, ref mountain);
        CountRiverBaseNeighbor(state, viewer, x - 1, y, ref plains, ref forest, ref mountain);

        if (mountain > forest && mountain > plains) return TileType.Mountain;
        if (forest >= plains) return TileType.Forest;
        return TileType.Plains;
    }

    private static void CountRiverBaseNeighbor(
        in GameState state,
        PlayerId viewer,
        int x,
        int y,
        ref int plains,
        ref int forest,
        ref int mountain)
    {
        if (!state.Map.InBounds(x, y)) return;
        if (FogOfWar.GetVisibility(state, viewer, x, y) == VisibilityState.Hidden) return;

        TileType t = FogOfWar.GetKnownTileType(state, viewer, x, y);
        switch (t)
        {
            case TileType.Forest:
                forest++;
                break;
            case TileType.Mountain:
            case TileType.MountainPeak:
                mountain++;
                break;
            case TileType.Plains:
            case TileType.City:
            case TileType.Capital:
            case TileType.Road:
            case TileType.Fort:
                plains++;
                break;
        }
    }

    private static void DrawTerrainOverlay(
        CanvasItem canvas,
        in GameState state,
        Vector2 origin,
        PlayerId viewer,
        int x,
        int y,
        TileType tile,
        VisibilityState vis)
    {
        if (tile == TileType.Road)
        {
            DrawRoadStroke(canvas, state, origin, viewer, x, y, Theme.Road, vis, false);
            return;
        }

        if (tile == TileType.River)
        {
            DrawRiverStroke(canvas, state, origin, viewer, x, y, vis);
            return;
        }

        if (tile == TileType.Bridge)
        {
            DrawRoadStroke(canvas, state, origin, viewer, x, y, Theme.Bridge, vis, true);
            return;
        }

        if (tile == TileType.MountainPeak)
        {
            DrawPeakAccent(canvas, state, origin, viewer, x, y, vis);
        }
    }

    private static void DrawRiverStroke(
        CanvasItem canvas,
        in GameState state,
        Vector2 origin,
        PlayerId viewer,
        int x,
        int y,
        VisibilityState vis)
    {
        bool n = RiverConnects(state, viewer, x, y - 1);
        bool e = RiverConnects(state, viewer, x + 1, y);
        bool s = RiverConnects(state, viewer, x, y + 1);
        bool w = RiverConnects(state, viewer, x - 1, y);

        Color water = Theme.Water;
        water.A = vis == VisibilityState.Explored ? 0.50f : 0.90f;
        Color highlight = Theme.WaterHighlight;
        highlight.A = vis == VisibilityState.Explored ? 0.18f : 0.32f;
        Color sideHighlight = Theme.WaterHighlight.Lightened(0.10f);
        sideHighlight.A = vis == VisibilityState.Explored ? 0.10f : 0.24f;

        float width = TilePx * 0.24f;
        float highlightWidth = width + TilePx * 0.10f;
        Vector2 center = TileCenter(x, y, origin);

        int count = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);
        if (count == 0)
        {
            canvas.DrawCircle(center, highlightWidth * 0.58f, highlight);
            canvas.DrawCircle(center, width * 0.58f, water);
            return;
        }

        DrawConnectedStroke(canvas, center, n, e, s, w, highlight, highlightWidth);
        DrawConnectedStroke(canvas, center, n, e, s, w, water, width);
        DrawConnectedStroke(canvas, center + new Vector2(-TilePx * 0.035f, -TilePx * 0.035f),
            n, e, s, w, sideHighlight, TilePx * 0.045f);
    }

    private static bool RiverConnects(in GameState state, PlayerId viewer, int x, int y)
    {
        if (!state.Map.InBounds(x, y)) return false;
        if (FogOfWar.GetVisibility(state, viewer, x, y) == VisibilityState.Hidden) return false;

        TileType t = FogOfWar.GetKnownTileType(state, viewer, x, y);
        return t is TileType.River or TileType.Water or TileType.Bridge;
    }

    private static void DrawTerrainCluster(
        CanvasItem canvas,
        in GameState state,
        Vector2 origin,
        PlayerId viewer,
        int x,
        int y,
        TileType family,
        VisibilityState vis)
    {
        bool n = IsClusterNeighbor(state, viewer, x, y - 1, family);
        bool e = IsClusterNeighbor(state, viewer, x + 1, y, family);
        bool s = IsClusterNeighbor(state, viewer, x, y + 1, family);
        bool w = IsClusterNeighbor(state, viewer, x - 1, y, family);

        Color color = family == TileType.Forest
            ? Theme.ForestHighlight
            : Theme.MountainShadow;
        color.A = family == TileType.Forest
            ? (vis == VisibilityState.Explored ? 0.07f : 0.16f)
            : (vis == VisibilityState.Explored ? 0.05f : 0.13f);

        Vector2 center = TileCenter(x, y, origin);
        float r = family == TileType.Forest ? TilePx * 0.34f : TilePx * 0.36f;
        float halfW = family == TileType.Forest ? TilePx * 0.22f : TilePx * 0.24f;
        float halfH = TilePx * 0.50f;

        canvas.DrawCircle(center, r, color);
        if (n)
            canvas.DrawRect(new Rect2(center + new Vector2(-halfW, -halfH), new Vector2(halfW * 2f, halfH)), color);
        if (s)
            canvas.DrawRect(new Rect2(center + new Vector2(-halfW, 0f), new Vector2(halfW * 2f, halfH)), color);
        if (w)
            canvas.DrawRect(new Rect2(center + new Vector2(-halfH, -halfW), new Vector2(halfH, halfW * 2f)), color);
        if (e)
            canvas.DrawRect(new Rect2(center + new Vector2(0f, -halfW), new Vector2(halfH, halfW * 2f)), color);
    }

    private static bool IsClusterNeighbor(in GameState state, PlayerId viewer, int x, int y, TileType family)
    {
        if (!state.Map.InBounds(x, y)) return false;
        if (FogOfWar.GetVisibility(state, viewer, x, y) == VisibilityState.Hidden) return false;

        TileType t = FogOfWar.GetKnownTileType(state, viewer, x, y);
        if (family == TileType.Forest)
            return t == TileType.Forest;

        return t is TileType.Mountain or TileType.MountainPeak;
    }

    private static void DrawPeakAccent(
        CanvasItem canvas,
        in GameState state,
        Vector2 origin,
        PlayerId viewer,
        int x,
        int y,
        VisibilityState vis)
    {
        Color peak = Theme.MountainPeak;
        peak.A = vis == VisibilityState.Explored ? 0.46f : 0.88f;
        Color peakShade = Theme.MountainPeak.Darkened(0.18f);
        peakShade.A = vis == VisibilityState.Explored ? 0.34f : 0.68f;
        Color shadow = Theme.MountainShadow;
        shadow.A = vis == VisibilityState.Explored ? 0.12f : 0.24f;

        Vector2 center = TileCenter(x, y, origin);

        var shadowPts = new Vector2[]
        {
            center + new Vector2(-TilePx * 0.20f, TilePx * 0.15f),
            center + new Vector2(-TilePx * 0.02f, -TilePx * 0.21f),
            center + new Vector2(TilePx * 0.22f, TilePx * 0.14f),
            center + new Vector2(TilePx * 0.12f, TilePx * 0.22f),
            center + new Vector2(-TilePx * 0.12f, TilePx * 0.22f),
        };
        canvas.DrawColoredPolygon(shadowPts, shadow);

        Vector2 summit = center + new Vector2(-TilePx * 0.02f, -TilePx * 0.20f);
        Vector2 leftBase = center + new Vector2(-TilePx * 0.18f, TilePx * 0.12f);
        Vector2 cleft = center + new Vector2(-TilePx * 0.03f, TilePx * 0.04f);
        Vector2 rightBase = center + new Vector2(TilePx * 0.18f, TilePx * 0.12f);

        var shadedFacet = new Vector2[]
        {
            summit,
            cleft,
            rightBase,
            center + new Vector2(TilePx * 0.07f, TilePx * 0.16f),
        };
        canvas.DrawColoredPolygon(shadedFacet, peakShade);

        var brightFacet = new Vector2[]
        {
            summit,
            leftBase,
            cleft,
        };
        canvas.DrawColoredPolygon(brightFacet, peak);

        Color ridge = peak;
        ridge.A *= 0.72f;
        canvas.DrawLine(
            summit + new Vector2(TilePx * 0.01f, TilePx * 0.03f),
            center + new Vector2(TilePx * 0.10f, TilePx * 0.12f),
            ridge,
            TilePx * 0.035f,
            true);
    }

    private static bool IsPeakNeighbor(in GameState state, PlayerId viewer, int x, int y)
    {
        if (!state.Map.InBounds(x, y)) return false;
        if (FogOfWar.GetVisibility(state, viewer, x, y) == VisibilityState.Hidden) return false;
        return FogOfWar.GetKnownTileType(state, viewer, x, y) == TileType.MountainPeak;
    }

    private static void DrawRoadStroke(
        CanvasItem canvas,
        in GameState state,
        Vector2 origin,
        PlayerId viewer,
        int x,
        int y,
        Color color,
        VisibilityState vis,
        bool bridge)
    {
        bool n = RoadConnects(state, viewer, x, y - 1, out _);
        bool e = RoadConnects(state, viewer, x + 1, y, out _);
        bool s = RoadConnects(state, viewer, x, y + 1, out _);
        bool w = RoadConnects(state, viewer, x - 1, y, out _);

        int count = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);
        color.A = vis == VisibilityState.Explored ? 0.58f : 1.0f;

        float width = bridge ? TilePx * 0.15f : TilePx * 0.18f;
        Vector2 center = TileCenter(x, y, origin);

        if (count == 0)
        {
            Vector2 a = center + new Vector2(-TilePx * 0.24f, 0f);
            Vector2 b = center + new Vector2(TilePx * 0.24f, 0f);
            canvas.DrawLine(a, b, color, width, true);
            return;
        }

        DrawConnectedStroke(canvas, center, n, e, s, w, color, width);
    }

    private static void DrawConnectedStroke(
        CanvasItem canvas,
        Vector2 center,
        bool n,
        bool e,
        bool s,
        bool w,
        Color color,
        float width)
    {
        int count = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);
        Vector2 north = new(0f, -TilePx * 0.5f);
        Vector2 east = new(TilePx * 0.5f, 0f);
        Vector2 south = new(0f, TilePx * 0.5f);
        Vector2 west = new(-TilePx * 0.5f, 0f);

        if (count == 2)
        {
            if (n && s)
            {
                canvas.DrawLine(center + north, center + south, color, width, true);
                return;
            }
            if (e && w)
            {
                canvas.DrawLine(center + west, center + east, color, width, true);
                return;
            }
            if (n && e)
            {
                DrawCornerStroke(canvas, center + north, center, center + east, color, width);
                return;
            }
            if (e && s)
            {
                DrawCornerStroke(canvas, center + east, center, center + south, color, width);
                return;
            }
            if (s && w)
            {
                DrawCornerStroke(canvas, center + south, center, center + west, color, width);
                return;
            }
            if (w && n)
            {
                DrawCornerStroke(canvas, center + west, center, center + north, color, width);
                return;
            }
        }

        DrawStrokeLeg(canvas, center, north, n, color, width);
        DrawStrokeLeg(canvas, center, east, e, color, width);
        DrawStrokeLeg(canvas, center, south, s, color, width);
        DrawStrokeLeg(canvas, center, west, w, color, width);
    }

    private static void DrawStrokeLeg(
        CanvasItem canvas,
        Vector2 center,
        Vector2 offset,
        bool connected,
        Color color,
        float width)
    {
        if (!connected) return;

        Vector2 end = center + offset;
        canvas.DrawLine(center, end, color, width, true);
    }

    private static void DrawCornerStroke(
        CanvasItem canvas,
        Vector2 from,
        Vector2 control,
        Vector2 to,
        Color color,
        float width)
    {
        const int segments = 8;
        Vector2[] points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float inv = 1f - t;
            points[i] = (inv * inv * from) + (2f * inv * t * control) + (t * t * to);
        }

        canvas.DrawPolyline(points, color, width, antialiased: true);
    }

    // -------- 2) Territory fill (tint each tile by its owner) ----------
    private static void DrawTerritoryFill(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        int w = state.Map.Width;
        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < w; x++)
            {
                VisibilityState vis = FogOfWar.GetVisibility(state, viewer, x, y);
                if (vis == VisibilityState.Hidden) continue;

                var owner = FogOfWar.GetKnownTileOwner(state, viewer, x, y);
                if (owner == PlayerId.None) continue;
                Color tint = Theme.ForPlayer(owner);
                tint.A = vis == VisibilityState.Explored
                    ? Theme.TerritoryFillAlpha * 0.45f
                    : Theme.TerritoryFillAlpha;
                Rect2 r = new(TileTopLeft(x, y, origin), new Vector2(TilePx, TilePx));
                canvas.DrawRect(r, tint);
            }
        }
    }

    // -------- 3) Border edges between owners ---------------------------
    private static void DrawBorders(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        int w = state.Map.Width, h = state.Map.Height;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                VisibilityState vis = FogOfWar.GetVisibility(state, viewer, x, y);
                if (vis == VisibilityState.Hidden) continue;

                PlayerId self = FogOfWar.GetKnownTileOwner(state, viewer, x, y);
                if (self == PlayerId.None) continue;
                Color edge = Theme.ForPlayer(self);
                edge.A = vis == VisibilityState.Explored
                    ? Theme.BorderEdgeAlpha * 0.45f
                    : Theme.BorderEdgeAlpha;

                Vector2 tl = TileTopLeft(x, y, origin);
                // Right edge.
                if (!TryKnownOwner(state, viewer, x + 1, y, out PlayerId right) || right != self)
                    canvas.DrawLine(tl + new Vector2(TilePx, 0), tl + new Vector2(TilePx, TilePx), edge, 2f);
                // Bottom edge.
                if (!TryKnownOwner(state, viewer, x, y + 1, out PlayerId down) || down != self)
                    canvas.DrawLine(tl + new Vector2(0, TilePx), tl + new Vector2(TilePx, TilePx), edge, 2f);
                // Left edge (only when neighbor differs and isn't already
                // covered by *its* right edge — avoid double draw).
                if (!TryKnownOwner(state, viewer, x - 1, y, out PlayerId left) || left == PlayerId.None)
                    canvas.DrawLine(tl, tl + new Vector2(0, TilePx), edge, 2f);
                // Top edge.
                if (!TryKnownOwner(state, viewer, x, y - 1, out PlayerId up) || up == PlayerId.None)
                    canvas.DrawLine(tl, tl + new Vector2(TilePx, 0), edge, 2f);
            }
        }
    }

    // -------- 3a) Forest canopy overlays --------------------------------
    private static void DrawForestTopOverlays(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        Texture2D? treeTop = TreeTopTexture();
        if (treeTop is null) return;

        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                VisibilityState sourceVis = FogOfWar.GetVisibility(state, viewer, x, y);
                if (sourceVis == VisibilityState.Hidden) continue;
                if (FogOfWar.GetKnownTileType(state, viewer, x, y) != TileType.Forest) continue;

                bool forestAbove = IsForestNeighbor(state, viewer, x, y - 1);
                bool forestBelow = IsForestNeighbor(state, viewer, x, y + 1);
                if (forestAbove || !forestBelow) continue;

                int overlayY = y - 1;
                if (!state.Map.InBounds(x, overlayY)) continue;

                VisibilityState targetVis = FogOfWar.GetVisibility(state, viewer, x, overlayY);
                if (targetVis == VisibilityState.Hidden) continue;

                Color modulate = sourceVis == VisibilityState.Explored || targetVis == VisibilityState.Explored
                    ? new Color(0.48f, 0.48f, 0.48f, 0.72f)
                    : new Color(1f, 1f, 1f, 1f);
                Rect2 full = new(TileTopLeft(x, overlayY, origin), new Vector2(TilePx, TilePx));
                DrawTileTexture(canvas, treeTop, full, modulate, TileSpriteTransform.None);
            }
        }
    }

    // -------- 3b) Bridge overlays ----------------------------------------
    private static void DrawBridges(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        Texture2D? bridge = BridgeTileTexture();
        if (bridge is null) return;

        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                VisibilityState vis = FogOfWar.GetVisibility(state, viewer, x, y);
                if (vis == VisibilityState.Hidden) continue;

                TileType t = FogOfWar.GetKnownTileType(state, viewer, x, y);
                if (t != TileType.Bridge) continue;

                Color modulate = vis == VisibilityState.Explored
                    ? new Color(0.48f, 0.48f, 0.48f, 0.72f)
                    : new Color(1f, 1f, 1f, 1f);
                Rect2 full = new(TileTopLeft(x, y, origin), new Vector2(TilePx, TilePx));
                TileSpriteTransform transform = BridgeTransform(state, viewer, x, y);
                DrawBridgeDeckUnderlay(canvas, full, transform, vis);
                DrawTileTexture(canvas, bridge, full, modulate, transform);
            }
        }
    }

    private static void DrawBridgeDeckUnderlay(
        CanvasItem canvas,
        Rect2 full,
        TileSpriteTransform transform,
        VisibilityState vis)
    {
        bool horizontal = transform == TileSpriteTransform.RotateClockwise
            || transform == TileSpriteTransform.RotateCounterClockwise;

        Color deck = Theme.Road;
        deck.A = vis == VisibilityState.Explored ? 0.44f : 0.94f;

        Rect2 deckRect = horizontal
            ? new Rect2(
                full.Position + new Vector2(0f, TilePx * 0.28f),
                new Vector2(TilePx, TilePx * 0.44f))
            : new Rect2(
                full.Position + new Vector2(TilePx * 0.28f, 0f),
                new Vector2(TilePx * 0.44f, TilePx));

        canvas.DrawRect(deckRect, deck);
    }

    // -------- 4) Cities & Forts -----------------------------------------
    private static void DrawCities(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        DrawExploredStructures(canvas, state, origin, viewer);

        for (int i = 0; i < state.Cities.Count; i++)
        {
            City c = state.Cities[i];
            if (!FogOfWar.IsVisible(state, viewer, c.TileX, c.TileY)) continue;

            Vector2 center = TileCenter(c.TileX, c.TileY, origin);
            Color owner = Theme.ForPlayer(c.Owner);

            // Forts (cities sitting on Fort tiles) render as diamonds.
            TileType tileTy = state.Map.GetTileUnchecked(c.TileX, c.TileY);
            bool isFort = tileTy == WarGame.Sim.State.TileType.Fort;

            float half = StructureMarkerHalf(c.IsCapital, isFort);
            DrawStructureMarker(canvas, center, owner, c.IsCapital, isFort, 1f);

            // Production/upgrade progress bar above the city. Forts don't
            // produce or upgrade, so this only fires for real cities.
            if (c.IsProducing || c.IsUpgrading)
            {
                float costRaw;
                float progressRaw;
                Color fill;
                if (c.IsUpgrading)
                {
                    costRaw = WarGame.Sim.State.UnitStats.UpgradeCost(c.DevelopmentLevel);
                    progressRaw = (float)c.DevelopmentProgress.ToDoubleUnsafe();
                    fill = Theme.SelectRing;
                }
                else
                {
                    var type = (UnitType)(c.ProductionOrder - 1);
                    costRaw = WarGame.Sim.State.UnitStats.EcoCost(type);
                    progressRaw = (float)c.ProductionProgress.ToDoubleUnsafe();
                    fill = Theme.ForPlayer(c.Owner);
                }
                float frac = costRaw > 0 ? Mathf.Clamp(progressRaw / costRaw, 0f, 1f) : 0f;

                float barW = TilePx * 0.85f;
                float barH = 4f;
                Vector2 barTl = new(center.X - barW * 0.5f, center.Y - half - 8f);
                Color faint = Theme.HudPanel; faint.A = 0.85f;
                canvas.DrawRect(new Rect2(barTl, new Vector2(barW, barH)), faint);
                canvas.DrawRect(new Rect2(barTl, new Vector2(barW * frac, barH)), fill);
            }

            // Capture HP bar below the city/fort — visible when under attack.
            int maxCap = c.MaxCaptureHp;
            if (c.CaptureHp < maxCap)
            {
                float capFrac = Mathf.Clamp((float)c.CaptureHp / maxCap, 0f, 1f);
                float barW = TilePx * 0.85f;
                float barH = 5f;
                Vector2 barTl = new(center.X - barW * 0.5f, center.Y + half + 4f);

                Color bg = Theme.HpBarBg;
                canvas.DrawRect(new Rect2(barTl, new Vector2(barW, barH)), bg);

                Color capFill = capFrac > 0.5f ? Theme.HpBarFill : Theme.HpBarLow;
                canvas.DrawRect(new Rect2(barTl, new Vector2(barW * capFrac, barH)), capFill);
            }
        }
    }

    // -------- 4b) Pending fort construction ghosts ----------------------
    private static void DrawPendingForts(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        if (state.PendingForts is null || state.PendingForts.Count == 0) return;

        for (int i = 0; i < state.PendingForts.Count; i++)
        {
            var f = state.PendingForts[i];
            if (!FogOfWar.IsVisible(state, viewer, f.TileX, f.TileY)) continue;
            Vector2 center = TileCenter(f.TileX, f.TileY, origin);
            Color ghost = Theme.ForPlayer(f.Owner);
            ghost.A = 0.4f; // Semi-transparent ghost

            float half = StructureMarkerHalf(isCapital: false, isFort: true);
            DrawStructureMarker(canvas, center, ghost, isCapital: false, isFort: true, 0.62f);

            // Build progress bar above the ghost.
            float frac = 1f - Mathf.Clamp((float)f.TicksRemaining / WarGame.Sim.Systems.FortConstruction.FortBuildTicks, 0f, 1f);
            float barW = TilePx * 0.85f;
            float barH = 4f;
            Vector2 barTl = new(center.X - barW * 0.5f, center.Y - half - 8f);
            Color bgCol = Theme.HudPanel; bgCol.A = 0.85f;
            canvas.DrawRect(new Rect2(barTl, new Vector2(barW, barH)), bgCol);
            Color fillCol = Theme.ForPlayer(f.Owner);
            canvas.DrawRect(new Rect2(barTl, new Vector2(barW * frac, barH)), fillCol);
        }
    }

    /// <summary>Draw a diamond (rotated square) centered at the given point.</summary>
    private static void DrawDiamond(CanvasItem canvas, Vector2 center, float half, Color color)
    {
        var pts = new Vector2[]
        {
            center + new Vector2(0, -half),   // top
            center + new Vector2(half, 0),     // right
            center + new Vector2(0, half),     // bottom
            center + new Vector2(-half, 0),    // left
        };
        canvas.DrawColoredPolygon(pts, color);
    }

    private static float StructureMarkerHalf(bool isCapital, bool isFort)
    {
        if (isFort) return TilePx * 0.34f;
        return isCapital ? TilePx * 0.38f : TilePx * 0.30f;
    }

    private static void DrawStructureMarker(
        CanvasItem canvas,
        Vector2 center,
        Color owner,
        bool isCapital,
        bool isFort,
        float alpha)
    {
        owner.A *= alpha;
        Color shadow = new(0, 0, 0, 0.34f * alpha);
        Color inner = Theme.HudPanel;
        inner.A = 0.42f * alpha;

        if (isFort)
        {
            float half = StructureMarkerHalf(isCapital: false, isFort: true);
            DrawDiamond(canvas, center + new Vector2(0, 2), half + 2f, shadow);
            DrawDiamond(canvas, center, half + 1.5f, owner.Darkened(0.28f));
            DrawDiamond(canvas, center, half * 0.80f, owner);
            DrawDiamond(canvas, center, half * 0.38f, inner);
            return;
        }

        if (isCapital)
        {
            float r = StructureMarkerHalf(isCapital: true, isFort: false);
            canvas.DrawCircle(center + new Vector2(0, 2), r + 2f, shadow);
            canvas.DrawCircle(center, r + 1.5f, owner.Darkened(0.26f));
            canvas.DrawCircle(center, r, owner);
            DrawStar(canvas, center, r * 0.60f, r * 0.27f, new Color(1f, 1f, 1f, 0.96f * alpha));
            return;
        }

        float halfCity = StructureMarkerHalf(isCapital: false, isFort: false);
        Rect2 shadowRect = new(
            center - new Vector2(halfCity + 2f, halfCity + 2f) + new Vector2(0, 2),
            new Vector2((halfCity + 2f) * 2f, (halfCity + 2f) * 2f));
        Rect2 outer = new(
            center - new Vector2(halfCity + 1.5f, halfCity + 1.5f),
            new Vector2((halfCity + 1.5f) * 2f, (halfCity + 1.5f) * 2f));
        Rect2 marker = new(
            center - new Vector2(halfCity, halfCity),
            new Vector2(halfCity * 2f, halfCity * 2f));
        Rect2 core = new(
            center - new Vector2(halfCity * 0.34f, halfCity * 0.34f),
            new Vector2(halfCity * 0.68f, halfCity * 0.68f));

        canvas.DrawRect(shadowRect, shadow);
        canvas.DrawRect(outer, owner.Darkened(0.26f));
        canvas.DrawRect(marker, owner);
        canvas.DrawRect(core, inner);
    }

    private static void DrawExploredStructures(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        if (viewer == PlayerId.None) return;

        int w = state.Map.Width;
        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (FogOfWar.GetVisibility(state, viewer, x, y) != VisibilityState.Explored)
                    continue;

                TileType remembered = FogOfWar.GetKnownTileType(state, viewer, x, y);
                if (!remembered.IsCityTile() && !remembered.IsFortTile()) continue;

                PlayerId ownerId = FogOfWar.GetKnownTileOwner(state, viewer, x, y);
                Color owner = Dim(Theme.ForPlayer(ownerId), 0.62f);
                owner.A = 0.65f;

                Vector2 center = TileCenter(x, y, origin);
                if (remembered.IsFortTile())
                {
                    DrawStructureMarker(canvas, center, owner, isCapital: false, isFort: true, 0.72f);
                    continue;
                }

                if (remembered == TileType.Capital)
                {
                    DrawStructureMarker(canvas, center, owner, isCapital: true, isFort: false, 0.72f);
                    continue;
                }

                DrawStructureMarker(canvas, center, owner, isCapital: false, isFort: false, 0.72f);
            }
        }
    }

    // -------- 5) Units -------------------------------------------------
    private static void DrawUnits(CanvasItem canvas, in GameState state, Vector2 origin, PlayerId viewer)
    {
        // Pre-pass: build per-unit (idxInStack, stackSize) so units sharing a
        // tile + owner can be fanned in a small cluster. Without this, two
        // stationary units on the same square render at the exact same pixel
        // and look like one.
        int n = state.Units.Count;
        if (n == 0) return;
        var stackIdx  = new int[n];
        var stackSize = new int[n];
        ComputeStackLayout(state, viewer, stackIdx, stackSize);

        int w = state.Map.Width;

        for (int i = 0; i < n; i++)
        {
            Unit u = state.Units[i];
            if (!u.IsAlive) continue;
            if (!FogOfWar.IsVisible(state, viewer, u.TileX, u.TileY)) continue;

            Vector2 anchor = UnitVisualCenter(u, state.Map, origin);
            // Apply fan-out only for stationary stacks. Moving units keep
            // their interpolated visual center.
            Vector2 center = anchor;
            int size = stackSize[i];
            int idx  = stackIdx[i];
            bool stationary = u.Path is null || u.Path.Count == 0;
            if (stationary && size > 1)
                center = anchor + StackOffset(idx, size, TilePx);

            Color faction = Theme.ForPlayer(u.Owner);
            float radius = UnitRadius(u.Type);
            // Slightly shrink units inside a stack so the cluster fits nicely.
            if (stationary && size > 1) radius *= 0.85f;

            // Behind-enemy-lines ring: pulses faintly around units standing
            // on a tile their player does not own. Doesn't affect gameplay
            // (Phase 3a's supply lines will), but it makes "danger zone"
            // legible at a glance.
            bool canShowFriendlyOperationalState = viewer == PlayerId.None || u.Owner == viewer;
            if (canShowFriendlyOperationalState
                && state.TileOwner is not null
                && (uint)u.TileX < (uint)state.Map.Width
                && (uint)u.TileY < (uint)state.Map.Height)
            {
                int tileOwnerByte = state.TileOwner[u.TileY * w + u.TileX];
                if (tileOwnerByte != (int)u.Owner && tileOwnerByte != (int)PlayerId.None)
                {
                    Color ring = Theme.HostileRing;
                    canvas.DrawArc(center, radius + 5f, 0, Mathf.Tau, 24, ring, 2f);
                }
            }

            if (canShowFriendlyOperationalState)
            {
                SupplyStatus supply = WarGame.Sim.Systems.SupplyLines.GetUnitStatus(state, i);
                if (supply == SupplyStatus.CutOff)
                {
                    Color cut = Theme.HostileRing;
                    cut.A = 0.95f;
                    canvas.DrawArc(center, radius + 8f, 0, Mathf.Tau, 24, cut, 2.5f);
                }
                else if (supply == SupplyStatus.RoadSupplied)
                {
                    Color road = Theme.Road;
                    road.A = 0.85f;
                    canvas.DrawArc(center, radius + 6f, 0, Mathf.Tau, 24, road, 2f);
                }
            }

            if (u.PromotionPoints > 0)
            {
                float time = (float)(Time.GetTicksMsec() / 1000.0);
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 2.5f);
                Color glow = Theme.SelectRing;
                glow.A = 0.18f + pulse * 0.14f;
                canvas.DrawCircle(center, radius + 8f + pulse * 2f, glow);
                Color edgeGlow = Theme.SelectRing;
                edgeGlow.A = 0.82f;
                canvas.DrawArc(center, radius + 8f + pulse * 2f, 0, Mathf.Tau, 32, edgeGlow, 1.4f);
            }

            // Drop shadow.
            canvas.DrawCircle(center + new Vector2(0, 2), radius, new Color(0, 0, 0, 0.40f));

            bool shipVisual = IsBroadWaterVisual(u, state.Map);
            Color outline = Theme.HudPanel;
            outline.A = 0.92f;
            if (shipVisual)
            {
                DrawShip(canvas, center, radius * 1.15f + 2.2f, outline);
                DrawShip(canvas, center, radius * 1.15f, faction);
            }
            else if (u.Type == UnitType.Heavy)
            {
                DrawHexagon(canvas, center, radius + 2.2f, outline);
                DrawHexagon(canvas, center, radius, faction);
            }
            else
            {
                canvas.DrawCircle(center, radius + 2.2f, outline);
                canvas.DrawCircle(center, radius, faction);
            }

            // Faint inner mark to differentiate at a glance.
            Color inner = Theme.ForPlayerDim(u.Owner);
            if (shipVisual)
                canvas.DrawLine(center + new Vector2(-radius * 0.45f, radius * 0.1f),
                    center + new Vector2(radius * 0.45f, radius * 0.1f), inner, 2f);
            else
                canvas.DrawCircle(center, radius * 0.4f, inner);

            // HP bar — only when wounded so unscathed units stay clean.
            DrawHpBar(canvas, u, center, radius);
            DrawRankStars(canvas, u, center, radius);
        }

        // Stack-count badges. We draw these AFTER units so the badge sits
        // on top of the cluster. One badge per (tile,owner) group whose
        // size > 6 (where fan-out crowding starts to hurt legibility).
        for (int i = 0; i < n; i++)
        {
            if (stackSize[i] <= 6) continue;
            if (stackIdx[i] != 0) continue;     // one badge per stack
            Unit u = state.Units[i];
            if (!u.IsAlive) continue;
            if (u.Path is { Count: > 0 }) continue;
            if (!FogOfWar.IsVisible(state, viewer, u.TileX, u.TileY)) continue;

            Vector2 anchor = TileCenter(u.TileX, u.TileY, origin);
            Vector2 badgeCenter = anchor + new Vector2(TilePx * 0.32f, -TilePx * 0.32f);
            canvas.DrawCircle(badgeCenter, 9f, new Color(0, 0, 0, 0.72f));
            canvas.DrawString(
                Theme.BuildPrimaryFont(),
                badgeCenter + new Vector2(-7, 4),
                $"{stackSize[i]}",
                HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, 0.95f));
        }
    }

    private static void DrawHpBar(CanvasItem canvas, in Unit u, Vector2 center, float radius)
    {
        // No bar when at full HP — keeps the field uncluttered until combat
        // actually starts.
        var max = WarGame.Sim.State.UnitStats.MaxHp(u.Type);
        if (u.Hp >= max) return;

        float frac = (float)(u.Hp.ToDoubleUnsafe() / max.ToDoubleUnsafe());
        if (frac < 0f) frac = 0f;
        if (frac > 1f) frac = 1f;

        float barW = radius * 2.2f;
        float barH = 3f;
        Vector2 tl = new(center.X - barW * 0.5f, center.Y - radius - 7f);
        canvas.DrawRect(new Rect2(tl, new Vector2(barW, barH)), Theme.HpBarBg);

        Color fill = frac < 0.33f ? Theme.HpBarLow : Theme.HpBarFill;
        canvas.DrawRect(new Rect2(tl, new Vector2(barW * frac, barH)), fill);
    }

    private static void DrawRankStars(CanvasItem canvas, in Unit u, Vector2 center, float radius)
    {
        int stars = Mathf.Clamp((int)u.Rank - 1, 0, 3);
        if (stars <= 0) return;

        float totalW = (stars - 1) * 8f;
        Vector2 start = center + new Vector2(-totalW * 0.5f, -radius - 13f);
        for (int i = 0; i < stars; i++)
        {
            Vector2 c = start + new Vector2(i * 8f, 0);
            DrawStar(canvas, c, 4.2f, 1.8f, Theme.SelectRing);
        }
    }

    private static bool IsBroadWaterVisual(in Unit u, in MapState map)
    {
        if (TerrainRules.IsBroadWater(map, u.TileX, u.TileY)) return true;
        if (u.Path is null || u.Path.Count == 0) return false;

        int next = u.Path[0];
        int nx = next % map.Width, ny = next / map.Width;
        return TerrainRules.IsBroadWater(map, nx, ny);
    }

    private static void DrawShip(CanvasItem canvas, Vector2 center, float radius, Color color)
    {
        var hull = new Vector2[]
        {
            center + new Vector2(0, -radius),
            center + new Vector2(radius * 0.72f, -radius * 0.18f),
            center + new Vector2(radius * 0.44f, radius * 0.78f),
            center + new Vector2(-radius * 0.44f, radius * 0.78f),
            center + new Vector2(-radius * 0.72f, -radius * 0.18f),
        };
        canvas.DrawColoredPolygon(hull, color);
    }

    private static Vector2 StackOffset(int idx, int size, float tilePx)
    {
        // Place stack members on a small circle around the tile center.
        // The cluster radius is sized so 6 units fit without overlapping
        // their outer radius.
        float clusterR = tilePx * 0.18f;
        // Start at top (-pi/2) so duos read vertically rather than horizontally,
        // matching how players intuit "stack of two" on a map.
        float a = -Mathf.Pi / 2f + idx * Mathf.Tau / Mathf.Min(size, 6);
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * clusterR;
    }

    private static void ComputeStackLayout(in GameState s, PlayerId viewer, int[] outIdx, int[] outSize)
    {
        // Bucket by (tile, owner). We only count stationary units — moving
        // ones already have unique interpolated positions and don't need
        // fanning. Dictionary iteration order doesn't matter here because
        // we write per-unit into outIdx/outSize at known indices.
        var buckets = new Dictionary<long, List<int>>();
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive) continue;
            if (u.Path is { Count: > 0 }) continue;
            if (!FogOfWar.IsVisible(s, viewer, u.TileX, u.TileY)) continue;
            long key = ((long)(byte)u.Owner << 48) | ((long)(uint)u.TileY << 24) | (uint)u.TileX;
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<int>();
            list.Add(i);
        }
        foreach (var kv in buckets)
        {
            int sz = kv.Value.Count;
            for (int j = 0; j < sz; j++)
            {
                outIdx[kv.Value[j]] = j;
                outSize[kv.Value[j]] = sz;
            }
        }
    }

    private static void DrawHexagon(CanvasItem canvas, Vector2 center, float radius, Color color)
    {
        // Pointy-top hexagon. Six vertices, then closed via PolyDraw.
        var pts = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Pi / 6f + i * Mathf.Pi / 3f;
            pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        canvas.DrawColoredPolygon(pts, color);
    }

    private static bool TryKnownOwner(in GameState state, PlayerId viewer, int x, int y, out PlayerId owner)
    {
        owner = PlayerId.None;
        if (!state.Map.InBounds(x, y)) return false;
        if (FogOfWar.GetVisibility(state, viewer, x, y) == VisibilityState.Hidden) return false;
        owner = FogOfWar.GetKnownTileOwner(state, viewer, x, y);
        return true;
    }

    private static Color Dim(Color c, float factor)
        => new(c.R * factor, c.G * factor, c.B * factor, c.A);

    private static Texture2D? CapitalOverlayTexture()
        => LoadFirstTexture(CapitalOverlayPaths, ref _capitalOverlay, ref _capitalOverlayLoadAttempted);

    private static Texture2D? CityOverlayTexture()
        => LoadFirstTexture(CityOverlayPaths, ref _cityOverlay, ref _cityOverlayLoadAttempted);

    private static Texture2D? ForestTileTexture()
        => LoadFirstTexture(ForestTilePaths, ref _forestTile, ref _forestTileLoadAttempted);

    private static Texture2D? TreeBottomTexture()
        => LoadFirstTexture(TreeBottomPaths, ref _treeBottomTile, ref _treeBottomTileLoadAttempted);

    private static Texture2D? TreeMidTexture()
        => LoadFirstTexture(TreeMidPaths, ref _treeMidTile, ref _treeMidTileLoadAttempted);

    private static Texture2D? TreeTopTexture()
        => LoadFirstTexture(TreeTopPaths, ref _treeTopTile, ref _treeTopTileLoadAttempted);

    private static Texture2D? PlainsTileTexture()
        => LoadFirstTexture(PlainsTilePaths, ref _plainsTile, ref _plainsTileLoadAttempted);

    private static Texture2D? WaterTileTexture()
        => LoadFirstTexture(WaterTilePaths, ref _waterTile, ref _waterTileLoadAttempted);

    private static Texture2D? RiverTileTexture()
        => LoadFirstTexture(RiverTilePaths, ref _riverTile, ref _riverTileLoadAttempted);

    private static Texture2D? WaterShoreTopTexture()
        => LoadFirstTexture(WaterShoreTopPaths, ref _waterShoreTopTile, ref _waterShoreTopTileLoadAttempted);

    private static Texture2D? WaterShoreCurveTexture()
        => LoadFirstTexture(WaterShoreCurvePaths, ref _waterShoreCurveTile, ref _waterShoreCurveTileLoadAttempted);

    private static Texture2D? WaterShoreCurveCloseTexture()
        => LoadFirstTexture(WaterShoreCurveClosePaths, ref _waterShoreCurveCloseTile, ref _waterShoreCurveCloseTileLoadAttempted);

    private static Texture2D? WaterTipTexture()
        => LoadFirstTexture(WaterTipPaths, ref _waterTipTile, ref _waterTipTileLoadAttempted);

    private static Texture2D? BridgeTileTexture()
        => LoadFirstTexture(BridgeTilePaths, ref _bridgeTile, ref _bridgeTileLoadAttempted);

    private static Texture2D? MountainTileTexture()
        => LoadFirstTexture(MountainTilePaths, ref _mountainTile, ref _mountainTileLoadAttempted);

    private static Texture2D? PeakTileTexture()
        => LoadFirstTexture(PeakTilePaths, ref _peakTile, ref _peakTileLoadAttempted);

    private static Texture2D? FortOverlayTexture()
        => LoadFirstTexture(FortOverlayPaths, ref _fortOverlay, ref _fortOverlayLoadAttempted);

    private static Texture2D? RoadStraightTexture()
        => LoadFirstTexture(RoadStraightPaths, ref _roadStraight, ref _roadStraightLoadAttempted);

    private static Texture2D? RoadDeadEndTexture()
        => LoadFirstTexture(RoadDeadEndPaths, ref _roadDeadEnd, ref _roadDeadEndLoadAttempted);

    private static Texture2D? RoadTriCrossSouthTexture()
        => LoadFirstTexture(RoadTriCrossSouthPaths, ref _roadTriCrossSouth, ref _roadTriCrossSouthLoadAttempted);

    private static Texture2D? RoadTriCrossNorthTexture()
        => LoadFirstTexture(RoadTriCrossNorthPaths, ref _roadTriCrossNorth, ref _roadTriCrossNorthLoadAttempted);

    private static Texture2D? RoadCrossTexture()
        => LoadFirstTexture(RoadCrossPaths, ref _roadCross, ref _roadCrossLoadAttempted);

    private static Texture2D? RoadCurveRightTexture()
        => LoadFirstTexture(RoadCurveRightPaths, ref _roadCurveRight, ref _roadCurveRightLoadAttempted);

    private static Texture2D? RoadCurveLeftTexture()
        => LoadFirstTexture(RoadCurveLeftPaths, ref _roadCurveLeft, ref _roadCurveLeftLoadAttempted);

    private static (Texture2D? Texture, TileSpriteTransform Transform) TerrainTileSprite(
        in GameState state,
        PlayerId viewer,
        int x,
        int y,
        TileType tile) => tile switch
    {
        TileType.Plains or TileType.City or TileType.Capital => (PlainsTileTexture(), TileSpriteTransform.None),
        TileType.Forest => ForestTileSprite(state, viewer, x, y),
        TileType.Water or TileType.River => WaterTileSprite(state, viewer, x, y),
        TileType.Bridge => (WaterTileTexture(), TileSpriteTransform.None),
        TileType.Mountain => (MountainTileTexture(), TileSpriteTransform.None),
        TileType.MountainPeak => (PeakTileTexture(), TileSpriteTransform.None),
        _ => (null, TileSpriteTransform.None),
    };

    private static (Texture2D? Texture, TileSpriteTransform Transform) ForestTileSprite(
        in GameState state,
        PlayerId viewer,
        int x,
        int y)
    {
        bool forestBelow = IsForestNeighbor(state, viewer, x, y + 1);

        Texture2D? texture = forestBelow
            ? TreeMidTexture() ?? TreeBottomTexture()
            : TreeBottomTexture();

        return (texture ?? ForestTileTexture(), TileSpriteTransform.None);
    }

    private static (Texture2D? Texture, TileSpriteTransform Transform) WaterTileSprite(
        in GameState state,
        PlayerId viewer,
        int x,
        int y)
    {
        bool n = IsShoreNeighbor(state, viewer, x, y - 1);
        bool e = IsShoreNeighbor(state, viewer, x + 1, y);
        bool s = IsShoreNeighbor(state, viewer, x, y + 1);
        bool w = IsShoreNeighbor(state, viewer, x - 1, y);

        int mask = (n ? 1 : 0) | (e ? 2 : 0) | (s ? 4 : 0) | (w ? 8 : 0);
        switch (mask)
        {
            case 0:
                return (WaterTileTexture(), TileSpriteTransform.None);

            case 1:
                return WaterEdgeSprite(TileSpriteTransform.None);
            case 2:
                return WaterEdgeSprite(TileSpriteTransform.RotateClockwise);
            case 4:
                return WaterEdgeSprite(TileSpriteTransform.FlipY);
            case 8:
                return WaterEdgeSprite(TileSpriteTransform.RotateCounterClockwise);

            case 3:
                return WaterCurveSprite(IsShoreNeighbor(state, viewer, x - 1, y + 1), TileSpriteTransform.None);
            case 6:
                return WaterCurveSprite(IsShoreNeighbor(state, viewer, x - 1, y - 1), TileSpriteTransform.RotateClockwise);
            case 12:
                return WaterCurveSprite(IsShoreNeighbor(state, viewer, x + 1, y - 1), TileSpriteTransform.FlipXY);
            case 9:
                return WaterCurveSprite(IsShoreNeighbor(state, viewer, x + 1, y + 1), TileSpriteTransform.RotateCounterClockwise);

            case 5:
                return WaterChannelSprite(TileSpriteTransform.RotateClockwise);
            case 10:
                return WaterChannelSprite(TileSpriteTransform.None);

            case 7:
                return WaterTipSprite(TileSpriteTransform.None);
            case 14:
                return WaterTipSprite(TileSpriteTransform.RotateClockwise);
            case 13:
                return WaterTipSprite(TileSpriteTransform.FlipX);
            case 11:
                return WaterTipSprite(TileSpriteTransform.RotateCounterClockwise);

            default:
                return WaterTipSprite(TileSpriteTransform.None);
        }
    }

    private static (Texture2D? Texture, TileSpriteTransform Transform) WaterEdgeSprite(TileSpriteTransform transform)
        => (WaterShoreTopTexture() ?? WaterTileTexture(), transform);

    private static (Texture2D? Texture, TileSpriteTransform Transform) WaterChannelSprite(TileSpriteTransform transform)
        => (RiverTileTexture() ?? WaterTileTexture(), transform);

    private static (Texture2D? Texture, TileSpriteTransform Transform) WaterTipSprite(TileSpriteTransform transform)
        => (WaterTipTexture() ?? WaterShoreCurveCloseTexture() ?? WaterTileTexture(), transform);

    private static (Texture2D? Texture, TileSpriteTransform Transform) WaterCurveSprite(
        bool useCloseCurve,
        TileSpriteTransform transform)
    {
        Texture2D? curve = useCloseCurve
            ? WaterShoreCurveCloseTexture() ?? WaterShoreCurveTexture()
            : WaterShoreCurveTexture();
        return (curve ?? WaterTileTexture(), transform);
    }

    private static bool IsShoreNeighbor(in GameState state, PlayerId viewer, int x, int y)
    {
        if (!state.Map.InBounds(x, y)) return false;
        if (FogOfWar.GetVisibility(state, viewer, x, y) == VisibilityState.Hidden) return false;

        TileType t = FogOfWar.GetKnownTileType(state, viewer, x, y);
        return t is not (TileType.Water or TileType.River or TileType.Bridge);
    }

    private static bool IsForestNeighbor(in GameState state, PlayerId viewer, int x, int y)
    {
        if (!state.Map.InBounds(x, y)) return false;
        if (FogOfWar.GetVisibility(state, viewer, x, y) == VisibilityState.Hidden) return false;
        return FogOfWar.GetKnownTileType(state, viewer, x, y) == TileType.Forest;
    }

    private static (Texture2D? Texture, TileSpriteTransform Transform) RoadTileTexture(
        in GameState state,
        PlayerId viewer,
        int x,
        int y)
    {
        bool n = RoadConnects(state, viewer, x, y - 1, out bool nStructure);
        bool e = RoadConnects(state, viewer, x + 1, y, out bool eStructure);
        bool s = RoadConnects(state, viewer, x, y + 1, out bool sStructure);
        bool w = RoadConnects(state, viewer, x - 1, y, out bool wStructure);

        int count = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);
        if (count >= 4)
            return (RoadCrossTexture(), TileSpriteTransform.None);

        if (count == 3)
        {
            if (!n) return (RoadTriCrossSouthTexture(), TileSpriteTransform.None);
            if (!s) return (RoadTriCrossNorthTexture(), TileSpriteTransform.None);
            if (!e) return (RoadTriCrossSouthTexture(), TileSpriteTransform.RotateClockwise);
            return (RoadTriCrossSouthTexture(), TileSpriteTransform.RotateCounterClockwise);
        }

        if (count == 2)
        {
            if (n && s) return (RoadStraightTexture(), TileSpriteTransform.None);
            if (e && w) return (RoadStraightTexture(), TileSpriteTransform.RotateClockwise);
            if (s && e) return (RoadCurveRightTexture(), TileSpriteTransform.None);
            if (s && w) return RoadCurveLeftOrMirrored(TileSpriteTransform.None, TileSpriteTransform.FlipX);
            if (n && e) return (RoadCurveRightTexture(), TileSpriteTransform.FlipY);
            return RoadCurveLeftOrMirrored(TileSpriteTransform.FlipY, TileSpriteTransform.FlipXY);
        }

        if (count == 1)
        {
            bool endsAtStructure = (n && nStructure) || (e && eStructure) || (s && sStructure) || (w && wStructure);
            Texture2D? texture = endsAtStructure ? RoadStraightTexture() : RoadDeadEndTexture();
            return (texture, SingleRoadTransform(n, e, s, w, endsAtStructure));
        }

        return (RoadDeadEndTexture(), TileSpriteTransform.None);
    }

    private static (Texture2D? Texture, TileSpriteTransform Transform) RoadCurveLeftOrMirrored(
        TileSpriteTransform leftTransform,
        TileSpriteTransform mirroredRightTransform)
    {
        Texture2D? left = RoadCurveLeftTexture();
        if (left is not null) return (left, leftTransform);
        return (RoadCurveRightTexture(), mirroredRightTransform);
    }

    private static TileSpriteTransform SingleRoadTransform(bool n, bool e, bool s, bool w, bool straight)
    {
        if (straight) return (e || w) ? TileSpriteTransform.RotateClockwise : TileSpriteTransform.None;
        if (s) return TileSpriteTransform.None;
        if (n) return TileSpriteTransform.FlipY;
        if (e) return TileSpriteTransform.RotateCounterClockwise;
        return TileSpriteTransform.RotateClockwise;
    }

    private static TileSpriteTransform BridgeTransform(in GameState state, PlayerId viewer, int x, int y)
    {
        bool n = RoadConnects(state, viewer, x, y - 1, out _);
        bool e = RoadConnects(state, viewer, x + 1, y, out _);
        bool s = RoadConnects(state, viewer, x, y + 1, out _);
        bool w = RoadConnects(state, viewer, x - 1, y, out _);

        int vertical = (n ? 1 : 0) + (s ? 1 : 0);
        int horizontal = (e ? 1 : 0) + (w ? 1 : 0);
        return horizontal > vertical
            ? TileSpriteTransform.RotateClockwise
            : TileSpriteTransform.None;
    }

    private static bool RoadConnects(in GameState state, PlayerId viewer, int x, int y, out bool structure)
    {
        structure = false;
        if (!state.Map.InBounds(x, y)) return false;
        if (FogOfWar.GetVisibility(state, viewer, x, y) == VisibilityState.Hidden) return false;

        TileType t = FogOfWar.GetKnownTileType(state, viewer, x, y);
        structure = t is TileType.City or TileType.Capital or TileType.Fort;
        return structure || t is TileType.Road or TileType.Bridge;
    }

    private static void DrawTileTexture(
        CanvasItem canvas,
        Texture2D texture,
        Rect2 rect,
        Color modulate,
        TileSpriteTransform transform)
    {
        Texture2D? drawTexture = TransformedTexture(texture, transform);
        if (drawTexture is null) return;
        canvas.DrawTextureRect(drawTexture, rect, false, modulate);
    }

    private static Texture2D? TransformedTexture(Texture2D texture, TileSpriteTransform transform)
    {
        if (transform == TileSpriteTransform.None) return texture;

        var key = (texture.GetInstanceId(), transform);
        if (_transformedTextures.TryGetValue(key, out Texture2D? cached))
            return cached;

        Image? image = texture.GetImage();
        if (image is null || image.GetWidth() <= 0 || image.GetHeight() <= 0)
            return texture;

        switch (transform)
        {
            case TileSpriteTransform.FlipX:
                image.FlipX();
                break;
            case TileSpriteTransform.FlipY:
                image.FlipY();
                break;
            case TileSpriteTransform.FlipXY:
                image.FlipX();
                image.FlipY();
                break;
            case TileSpriteTransform.RotateClockwise:
                image.Rotate90(ClockDirection.Clockwise);
                break;
            case TileSpriteTransform.RotateCounterClockwise:
                image.Rotate90(ClockDirection.Counterclockwise);
                break;
        }

        Texture2D transformed = ImageTexture.CreateFromImage(image);
        _transformedTextures[key] = transformed;
        return transformed;
    }

    private static Texture2D? LoadFirstTexture(string[] paths, ref Texture2D? texture, ref bool loadAttempted)
    {
        if (loadAttempted) return texture;

        loadAttempted = true;
        foreach (string path in paths)
        {
            texture = GD.Load<Texture2D>(path);
            texture ??= LoadTextureFromFile(path);
            if (texture is not null) break;
        }

        return texture;
    }

    private static ImageTexture? LoadTextureFromFile(string path)
    {
        Image? image = Image.LoadFromFile(path);
        if (image is null || image.GetWidth() <= 0 || image.GetHeight() <= 0)
            return null;

        return ImageTexture.CreateFromImage(image);
    }

    private static void DrawStar(CanvasItem canvas, Vector2 center, float outerR, float innerR, Color color)
    {
        // 5-point star: 10 vertices alternating outer/inner radius.
        const int points = 5;
        var pts = new Vector2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float a = -Mathf.Pi / 2f + i * Mathf.Pi / points;
            float r = (i % 2 == 0) ? outerR : innerR;
            pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        canvas.DrawColoredPolygon(pts, color);
    }
}
