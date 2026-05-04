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
    private static readonly string[] PlainsTilePaths =
    [
        "res://assets/images/plains.png",
    ];
    private static readonly string[] WaterTilePaths =
    [
        "res://assets/images/water.png",
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
    private static Texture2D? _plainsTile;
    private static bool _plainsTileLoadAttempted;
    private static Texture2D? _waterTile;
    private static bool _waterTileLoadAttempted;
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
        DrawTerritoryFill(canvas, state, origin, viewer);
        DrawBorders(canvas, state, origin, viewer);
        DrawBridges(canvas, state, origin, viewer);
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
                Color baseColor = vis switch
                {
                    VisibilityState.Hidden => Theme.FogHidden,
                    VisibilityState.Explored => Dim(Theme.ForTile(t), 0.42f),
                    _ => Theme.ForTile(t),
                };
                Vector2 tl = TileTopLeft(x, y, origin);

                // Base fill.
                Rect2 full = new(tl, new Vector2(TilePx, TilePx));
                canvas.DrawRect(full, baseColor);

                if (vis != VisibilityState.Hidden)
                {
                    Texture2D? terrain = TerrainTileTexture(t);
                    if (terrain is not null)
                    {
                        Color modulate = vis == VisibilityState.Explored
                            ? new Color(0.48f, 0.48f, 0.48f, 0.72f)
                            : new Color(1f, 1f, 1f, 1f);
                        canvas.DrawTextureRect(terrain, full, false, modulate);
                    }

                    if (t == TileType.Road)
                    {
                        (Texture2D? road, TileSpriteTransform transform) = RoadTileTexture(state, viewer, x, y);
                        if (road is not null)
                        {
                            Color modulate = vis == VisibilityState.Explored
                                ? new Color(0.48f, 0.48f, 0.48f, 0.72f)
                                : new Color(1f, 1f, 1f, 1f);
                            DrawTileTexture(canvas, road, full, modulate, transform);
                        }
                    }
                }

                // Subtle depth: 1-px top/left edge highlight and 1-px
                // bottom/right edge shadow to complete the grid look.
                Color highlight = vis == VisibilityState.Hidden
                    ? Theme.FogGrid
                    : Theme.ForTileEdgeHighlight(t);
                if (vis == VisibilityState.Explored) highlight = Dim(highlight, 0.45f);
                highlight.A = vis == VisibilityState.Hidden ? 0.22f : 0.30f;
                canvas.DrawLine(tl, tl + new Vector2(TilePx, 0), highlight, 1f); // Top
                canvas.DrawLine(tl, tl + new Vector2(0, TilePx), highlight, 1f); // Left

                Color shadow = vis == VisibilityState.Hidden
                    ? new Color(0, 0, 0, 0.32f)
                    : new Color(0, 0, 0, vis == VisibilityState.Explored ? 0.24f : 0.15f);
                Vector2 bl = tl + new Vector2(0, TilePx - 1);
                canvas.DrawLine(bl, bl + new Vector2(TilePx, 0), shadow, 1f); // Bottom
                Vector2 tr = tl + new Vector2(TilePx - 1, 0);
                canvas.DrawLine(tr, tr + new Vector2(0, TilePx), shadow, 1f); // Right
            }
        }
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

            float half = isFort ? CityMarkerHalf * 0.85f : CapitalMarkerHalf;

            if (isFort)
            {
                Rect2 marker = new(TileTopLeft(c.TileX, c.TileY, origin), new Vector2(TilePx, TilePx));
                Rect2 shadow = new(marker.Position + new Vector2(0, 2), marker.Size);
                canvas.DrawRect(shadow, new Color(0, 0, 0, 0.35f));
                canvas.DrawRect(marker, owner);

                Texture2D? fort = FortOverlayTexture();
                if (fort is not null)
                {
                    canvas.DrawTextureRect(fort, marker, false);
                }
                else
                {
                    // Diamond = rotated square. Draw as 4-point polygon.
                    DrawDiamond(canvas, center, half, owner);
                }
            }
            else
            {
                Texture2D? cityOverlay = c.IsCapital ? null : CityOverlayTexture();
                Rect2 marker = new(TileTopLeft(c.TileX, c.TileY, origin), new Vector2(TilePx, TilePx));

                // Subtle drop shadow.
                Rect2 shadow = new(marker.Position + new Vector2(0, 2), marker.Size);
                canvas.DrawRect(shadow, new Color(0, 0, 0, 0.35f));

                canvas.DrawRect(marker, owner);

                // Capital overlay uses the supplied transparent sprite over
                // the owner-colored underlay. Fallback keeps dev builds usable
                // before Godot imports the PNG.
                if (c.IsCapital)
                {
                    Texture2D? overlay = CapitalOverlayTexture();
                    if (overlay is not null)
                    {
                        canvas.DrawTextureRect(overlay, marker, false);
                    }
                    else
                    {
                        DrawStar(canvas, center, half * 0.78f, half * 0.34f,
                            Theme.ForPlayerDim(c.Owner));
                    }
                }
                else if (cityOverlay is not null)
                {
                    canvas.DrawTextureRect(cityOverlay, marker, false);
                }
            }

            // Production progress bar above the city (only when producing).
            // Forts don't produce units, so this only fires for real cities.
            if (c.IsProducing)
            {
                var type = (UnitType)(c.ProductionOrder - 1);
                float costRaw = WarGame.Sim.State.UnitStats.EcoCost(type);
                float progressRaw = (float)(c.ProductionProgress.ToDoubleUnsafe());
                float frac = costRaw > 0 ? Mathf.Clamp(progressRaw / costRaw, 0f, 1f) : 0f;

                float barW = TilePx * 0.85f;
                float barH = 4f;
                Vector2 barTl = new(center.X - barW * 0.5f, center.Y - half - 8f);
                Color faint = Theme.HudPanel; faint.A = 0.85f;
                canvas.DrawRect(new Rect2(barTl, new Vector2(barW, barH)), faint);
                Color fill = Theme.ForPlayer(c.Owner);
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

            float half = CityMarkerHalf * 0.85f;
            Texture2D? fort = FortOverlayTexture();
            if (fort is not null)
            {
                Rect2 marker = new(TileTopLeft(f.TileX, f.TileY, origin), new Vector2(TilePx, TilePx));
                canvas.DrawRect(marker, ghost);
                canvas.DrawTextureRect(fort, marker, false, new Color(1f, 1f, 1f, 0.48f));
            }
            else
            {
                DrawDiamond(canvas, center, half, ghost);
            }

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
                    Texture2D? fort = FortOverlayTexture();
                    if (fort is not null)
                    {
                        Rect2 fortMarker = new(TileTopLeft(x, y, origin), new Vector2(TilePx, TilePx));
                        canvas.DrawRect(fortMarker, owner);
                        canvas.DrawTextureRect(fort, fortMarker, false, new Color(0.52f, 0.52f, 0.52f, 0.62f));
                    }
                    else
                    {
                        DrawDiamond(canvas, center, CityMarkerHalf * 0.72f, owner);
                    }
                    continue;
                }

                if (remembered == TileType.Capital)
                {
                    float half = CapitalMarkerHalf * 0.78f;
                    Rect2 marker = new(TileTopLeft(x, y, origin), new Vector2(TilePx, TilePx));
                    canvas.DrawRect(marker, owner);
                    Texture2D? overlay = CapitalOverlayTexture();
                    if (overlay is not null)
                    {
                        canvas.DrawTextureRect(overlay, marker, false, new Color(0.52f, 0.52f, 0.52f, 0.62f));
                    }
                    else
                    {
                        DrawStar(canvas, center, half * 0.76f, half * 0.33f,
                            Dim(Theme.ForPlayerDim(ownerId), 0.62f));
                    }
                    continue;
                }

                Texture2D? city = CityOverlayTexture();
                if (city is not null)
                {
                    Rect2 cityMarker = new(TileTopLeft(x, y, origin), new Vector2(TilePx, TilePx));
                    canvas.DrawRect(cityMarker, owner);
                    canvas.DrawTextureRect(city, cityMarker, false, new Color(0.52f, 0.52f, 0.52f, 0.62f));
                }
                else
                {
                    Rect2 marker = new(TileTopLeft(x, y, origin), new Vector2(TilePx, TilePx));
                    canvas.DrawRect(marker, owner);
                }
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
            if (shipVisual)
            {
                DrawShip(canvas, center, radius * 1.15f, faction);
            }
            else if (u.Type == UnitType.Heavy)
            {
                DrawHexagon(canvas, center, radius, faction);
            }
            else
            {
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

    private static Texture2D? PlainsTileTexture()
        => LoadFirstTexture(PlainsTilePaths, ref _plainsTile, ref _plainsTileLoadAttempted);

    private static Texture2D? WaterTileTexture()
        => LoadFirstTexture(WaterTilePaths, ref _waterTile, ref _waterTileLoadAttempted);

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

    private static Texture2D? TerrainTileTexture(TileType tile) => tile switch
    {
        TileType.Plains or TileType.City or TileType.Capital => PlainsTileTexture(),
        TileType.Forest => ForestTileTexture(),
        TileType.Water or TileType.River or TileType.Bridge => WaterTileTexture(),
        TileType.Mountain => MountainTileTexture(),
        TileType.MountainPeak => PeakTileTexture(),
        _ => null,
    };

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
