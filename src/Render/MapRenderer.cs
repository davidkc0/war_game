namespace WarGame.Render;

using Godot;
using System.Collections.Generic;
using WarGame.Sim.State;

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
    {
        DrawTerrain(canvas, state, origin);
        DrawTerritoryFill(canvas, state, origin);
        DrawBorders(canvas, state, origin);
        DrawCities(canvas, state, origin);
        DrawPendingForts(canvas, state, origin);
        DrawPendingRoads(canvas, state, origin);
        DrawUnits(canvas, state, origin);
    }

    // -------- 4c) Pending road/bridge construction ----------------------
    private static void DrawPendingRoads(CanvasItem canvas, in GameState state, Vector2 origin)
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
    private static void DrawTerrain(CanvasItem canvas, in GameState state, Vector2 origin)
    {
        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                TileType t = state.Map.GetTileUnchecked(x, y);
                Color baseColor = Theme.ForTile(t);
                Vector2 tl = TileTopLeft(x, y, origin);

                // Base fill.
                Rect2 full = new(tl, new Vector2(TilePx, TilePx));
                canvas.DrawRect(full, baseColor);

                // Subtle depth: 1-px top/left edge highlight and 1-px
                // bottom/right edge shadow to complete the grid look.
                Color highlight = Theme.ForTileEdgeHighlight(t);
                highlight.A = 0.30f;
                canvas.DrawLine(tl, tl + new Vector2(TilePx, 0), highlight, 1f); // Top
                canvas.DrawLine(tl, tl + new Vector2(0, TilePx), highlight, 1f); // Left

                Color shadow = new(0, 0, 0, 0.15f);
                Vector2 bl = tl + new Vector2(0, TilePx - 1);
                canvas.DrawLine(bl, bl + new Vector2(TilePx, 0), shadow, 1f); // Bottom
                Vector2 tr = tl + new Vector2(TilePx - 1, 0);
                canvas.DrawLine(tr, tr + new Vector2(0, TilePx), shadow, 1f); // Right
            }
        }
    }

    // -------- 2) Territory fill (tint each tile by its owner) ----------
    private static void DrawTerritoryFill(CanvasItem canvas, in GameState state, Vector2 origin)
    {
        if (state.TileOwner is null) return;
        int w = state.Map.Width;
        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var owner = (PlayerId)state.TileOwner[y * w + x];
                if (owner == PlayerId.None) continue;
                Color tint = Theme.ForPlayer(owner);
                tint.A = Theme.TerritoryFillAlpha;
                Rect2 r = new(TileTopLeft(x, y, origin), new Vector2(TilePx, TilePx));
                canvas.DrawRect(r, tint);
            }
        }
    }

    // -------- 3) Border edges between owners ---------------------------
    private static void DrawBorders(CanvasItem canvas, in GameState state, Vector2 origin)
    {
        if (state.TileOwner is null) return;
        int w = state.Map.Width, h = state.Map.Height;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int self = state.TileOwner[y * w + x];
                if (self == (int)PlayerId.None) continue;
                Color edge = Theme.ForPlayer((PlayerId)self);
                edge.A = Theme.BorderEdgeAlpha;

                Vector2 tl = TileTopLeft(x, y, origin);
                // Right edge.
                if (x + 1 < w && state.TileOwner[y * w + (x + 1)] != self)
                    canvas.DrawLine(tl + new Vector2(TilePx, 0), tl + new Vector2(TilePx, TilePx), edge, 2f);
                // Bottom edge.
                if (y + 1 < h && state.TileOwner[(y + 1) * w + x] != self)
                    canvas.DrawLine(tl + new Vector2(0, TilePx), tl + new Vector2(TilePx, TilePx), edge, 2f);
                // Left edge (only when neighbor differs and isn't already
                // covered by *its* right edge — avoid double draw).
                if (x == 0 || state.TileOwner[y * w + (x - 1)] == (int)PlayerId.None)
                    canvas.DrawLine(tl, tl + new Vector2(0, TilePx), edge, 2f);
                // Top edge.
                if (y == 0 || state.TileOwner[(y - 1) * w + x] == (int)PlayerId.None)
                    canvas.DrawLine(tl, tl + new Vector2(TilePx, 0), edge, 2f);
            }
        }
    }

    // -------- 4) Cities & Forts -----------------------------------------
    private static void DrawCities(CanvasItem canvas, in GameState state, Vector2 origin)
    {
        for (int i = 0; i < state.Cities.Count; i++)
        {
            City c = state.Cities[i];
            Vector2 center = TileCenter(c.TileX, c.TileY, origin);
            Color owner = Theme.ForPlayer(c.Owner);

            // Forts (cities sitting on Fort tiles) render as diamonds.
            TileType tileTy = state.Map.GetTileUnchecked(c.TileX, c.TileY);
            bool isFort = tileTy == WarGame.Sim.State.TileType.Fort;

            float half = c.IsCapital ? CapitalMarkerHalf : CityMarkerHalf;
            if (isFort) half = CityMarkerHalf * 0.85f;

            if (isFort)
            {
                // Diamond = rotated square. Draw as 4-point polygon.
                DrawDiamond(canvas, center, half, owner);
            }
            else
            {
                // Subtle drop shadow.
                Rect2 shadow = new(center - new Vector2(half, half) + new Vector2(0, 2),
                                   new Vector2(half * 2, half * 2));
                canvas.DrawRect(shadow, new Color(0, 0, 0, 0.35f));

                Rect2 marker = new(center - new Vector2(half, half),
                                   new Vector2(half * 2, half * 2));
                canvas.DrawRect(marker, owner);

                // Capital marked with a 5-pointed star.
                if (c.IsCapital)
                    DrawStar(canvas, center, half * 0.78f, half * 0.34f,
                        Theme.ForPlayerDim(c.Owner));
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
    private static void DrawPendingForts(CanvasItem canvas, in GameState state, Vector2 origin)
    {
        if (state.PendingForts is null || state.PendingForts.Count == 0) return;

        for (int i = 0; i < state.PendingForts.Count; i++)
        {
            var f = state.PendingForts[i];
            Vector2 center = TileCenter(f.TileX, f.TileY, origin);
            Color ghost = Theme.ForPlayer(f.Owner);
            ghost.A = 0.4f; // Semi-transparent ghost

            float half = CityMarkerHalf * 0.85f;
            DrawDiamond(canvas, center, half, ghost);

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

    // -------- 5) Units -------------------------------------------------
    private static void DrawUnits(CanvasItem canvas, in GameState state, Vector2 origin)
    {
        // Pre-pass: build per-unit (idxInStack, stackSize) so units sharing a
        // tile + owner can be fanned in a small cluster. Without this, two
        // stationary units on the same square render at the exact same pixel
        // and look like one.
        int n = state.Units.Count;
        if (n == 0) return;
        var stackIdx  = new int[n];
        var stackSize = new int[n];
        ComputeStackLayout(state, stackIdx, stackSize);

        int w = state.Map.Width;

        for (int i = 0; i < n; i++)
        {
            Unit u = state.Units[i];
            if (!u.IsAlive) continue;

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
            if (state.TileOwner is not null
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

            // Drop shadow.
            canvas.DrawCircle(center + new Vector2(0, 2), radius, new Color(0, 0, 0, 0.40f));

            // Body — circles for light, hexagons for heavy.
            if (u.Type == UnitType.Heavy)
                DrawHexagon(canvas, center, radius, faction);
            else
                canvas.DrawCircle(center, radius, faction);

            // Faint inner mark to differentiate at a glance.
            Color inner = Theme.ForPlayerDim(u.Owner);
            canvas.DrawCircle(center, radius * 0.4f, inner);

            // HP bar — only when wounded so unscathed units stay clean.
            DrawHpBar(canvas, u, center, radius);
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

    private static void ComputeStackLayout(in GameState s, int[] outIdx, int[] outSize)
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
