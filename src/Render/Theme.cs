namespace WarGame.Render;

using Godot;
using WarGame.Sim.State;

// Phase 1 visual palette per PLAN.md §1.6. Committed values — change them
// here, not at call sites. Every tile, unit, border, and HUD reads from
// this single source of truth.
//
// Colorblindness check: the P1 (teal #2DD4BF) and P2 (coral #FB7185) pair
// passes deuteranopia and protanopia simulation; both shift to distinguishable
// blue-grey vs orange-grey. Tritanopia is mildly compressed but still
// distinguishable by lightness.
public static class Theme
{
    // ----- Faction colors --------------------------------------------------
    public static readonly Color P1     = new("#2DD4BF");
    public static readonly Color P2     = new("#FB7185");
    public static readonly Color P1Dim  = new("#0F766E");
    public static readonly Color P2Dim  = new("#9F1239");
    public static readonly Color Neutral = new("#64748B");   // slate-500

    // ----- Terrain (desaturated so faction colors stay visually dominant) -
    public static readonly Color BgVoid    = new("#0F172A");   // slate-900
    public static readonly Color Plains    = new("#BCC77E");
    public static readonly Color PlainsHighlight = new("#C8D08A");
    public static readonly Color Forest    = new("#656D39");
    public static readonly Color ForestHighlight = new("#7A8348");
    public static readonly Color Water     = new("#3A6B8C");
    public static readonly Color WaterHighlight = new("#4A7E9F");
    public static readonly Color River     = Water;
    public static readonly Color Road      = new("#A88B5C");
    public static readonly Color Bridge    = new("#6B4423");
    public static readonly Color Mountain  = new("#666666");
    public static readonly Color MountainShadow = new("#4D4D4D");
    public static readonly Color MountainPeak = new("#FFFFFF");
    public static readonly Color Fort      = new("#8B6914");   // dark amber/brown
    public static readonly Color FogHidden = new("#020617");   // near-black unknown
    public static readonly Color FogGrid   = new("#1E293B");   // subtle hidden tile grid
    public static readonly Color GridLine  = new("#0F172A33"); // faint tile readability grid
    public static readonly Color TerrainTransition = new("#263018");
    public static readonly Color TerrainShadow = new("#111827");
    public static readonly Color WaterWave = WaterHighlight;
    public const float TerrainTextureAlpha = 0.07f;
    public const float TerrainTextureAlphaExplored = 0.03f;
    public const float TerrainTransitionAlpha = 0.22f;
    public const float TerrainShadowAlpha = 0.18f;
    public const float WaterWaveAlpha = 0.18f;
    public const float WaterWaveAlphaExplored = 0.07f;

    // ----- UI -------------------------------------------------------------
    public static readonly Color HudText      = new("#E2E8F0");
    public static readonly Color HudTextDim   = new("#94A3B8");
    public static readonly Color HudPanel     = new("#1E293B");
    public static readonly Color HudPanelEdge = new("#334155");
    public static readonly Color SelectRing   = new("#FBBF24");   // amber-400
    public static readonly Color BoxSelect    = new("#FBBF2433"); // amber w/ alpha
    // Behind-enemy-lines warning for units standing on enemy-owned tiles.
    public static readonly Color HostileRing  = new("#F87171");   // rose-400
    // HP bar colors. Background is a dim slab; fill colored by faction.
    public static readonly Color HpBarBg      = new("#0F172AAA"); // slate-900 + alpha
    public static readonly Color HpBarFill    = new("#22C55E");   // green-500 (full)
    public static readonly Color HpBarLow     = new("#EF4444");   // red-500 (<33%)

    // Bottom resource bar — slightly lighter than HudPanel so the two bars
    // are visually distinct layers.
    public static readonly Color HudBottomBar = new("#1A2537");
    // Very low-alpha faction tints for bottom bar per-player backgrounds.
    public static readonly Color P1BgTint     = new("#2DD4BF18");
    public static readonly Color P2BgTint     = new("#FB718518");

    // Combat flash — brief radial pulse when a unit takes damage.
    public static readonly Color CombatFlash  = new("#FF6B35CC"); // warm orange
    // Move destination marker.
    public static readonly Color MoveMarker   = new("#FBBF24CC"); // amber, semi-transparent
    // City hover highlight.
    public static readonly Color CityHover    = new("#FBBF2499"); // amber, visible hover
    public static readonly Color TileHover    = new("#FBBF2440");
    public static readonly Color RoadPreview  = new("#A88B5C99");
    public static readonly Color BridgePreview = new("#6B4423CC");
    public static readonly Color InvalidPreview = new("#EF444499");

    // Production menu colors.
    public static readonly Color MenuBg       = new("#1E293BF0"); // panel with slight transparency
    public static readonly Color MenuBorder   = new("#475569");   // slate-600, crisper edge
    public static readonly Color ProgressBarBg = new("#334155");
    public static readonly Color CancelBtnEdge = new("#F59E0B");  // amber-500
    public static readonly Color WarningText   = new("#F97316");  // orange-500

    // ----- Border tinting --------------------------------------------------
    // Friendly territory is barely-tinted; enemy edges show a stronger
    // contour. The opacity values below are chosen to feel "claimed" without
    // overwhelming terrain readability.
    public const float TerritoryFillAlpha = 0.18f;
    public const float BorderEdgeAlpha    = 0.85f;

    public static Color ForPlayer(PlayerId p) => p switch
    {
        PlayerId.Player1 => P1,
        PlayerId.Player2 => P2,
        _                => Neutral,
    };

    public static Color ForPlayerDim(PlayerId p) => p switch
    {
        PlayerId.Player1 => P1Dim,
        PlayerId.Player2 => P2Dim,
        _                => Neutral,
    };

    public static Color ForPlayerBgTint(PlayerId p) => p switch
    {
        PlayerId.Player1 => P1BgTint,
        PlayerId.Player2 => P2BgTint,
        _                => new Color(0, 0, 0, 0),
    };

    public static Color ForTile(TileType t) => t switch
    {
        TileType.Plains   => Plains,
        TileType.Forest   => Forest,
        TileType.Mountain => Mountain,
        TileType.MountainPeak => Mountain,
        TileType.Water    => Water,
        TileType.River    => River,
        TileType.Road     => Road,
        TileType.Bridge   => Bridge,
        TileType.Fort     => Fort,
        TileType.City     => Plains,    // city marker drawn on top of plains tone
        TileType.Capital  => Plains,
        _                 => Plains,
    };

    /// <summary>
    /// Returns a slightly lighter version of the tile color for the top-edge
    /// highlight. Gives subtle depth per PLAN.md §1.6 rule #3 without the
    /// ugly horizontal band that a two-strip gradient creates.
    /// </summary>
    public static Color ForTileEdgeHighlight(TileType t)
        => t switch
        {
            TileType.Plains or TileType.City or TileType.Capital => PlainsHighlight,
            TileType.Forest => ForestHighlight,
            TileType.Water or TileType.River or TileType.Bridge => WaterHighlight,
            TileType.Road => PlainsHighlight,
            TileType.Mountain => Mountain.Lightened(0.10f),
            TileType.MountainPeak => Mountain.Lightened(0.10f),
            _ => ForTile(t).Lightened(0.14f),
        };

    public static Color ForTileEdgeShadow(TileType t)
        => t switch
        {
            TileType.Mountain => MountainShadow,
            TileType.MountainPeak => MountainShadow,
            _ => new Color(0, 0, 0, 1),
        };

    // ----- Font management ------------------------------------------------
    // Bundled Inter Regular + SemiBold. Loaded once, cached forever.
    // Phase 6 was supposed to do this; moved up because SystemFont fallback
    // produces inconsistent rendering across machines.
    private static Font? _cachedPrimary;
    private static Font? _cachedSemiBold;

    /// <summary>
    /// Inter Regular — the primary UI font. Loaded from bundled .ttf on first
    /// call, then cached. Thread-safe via Godot's single-threaded render loop.
    /// </summary>
    public static Font BuildPrimaryFont()
    {
        if (_cachedPrimary is not null) return _cachedPrimary;
        _cachedPrimary = LoadFontOrFallback("res://assets/fonts/Inter-Regular.ttf");
        return _cachedPrimary;
    }

    /// <summary>
    /// Inter SemiBold — for headings and emphasis.
    /// </summary>
    public static Font BuildSemiBoldFont()
    {
        if (_cachedSemiBold is not null) return _cachedSemiBold;
        _cachedSemiBold = LoadFontOrFallback("res://assets/fonts/Inter-SemiBold.ttf");
        return _cachedSemiBold;
    }

    private static Font LoadFontOrFallback(string resPath)
    {
        var loaded = GD.Load<Font>(resPath);
        if (loaded is not null) return loaded;

        // Fallback: if the .ttf isn't importable (e.g. running headless tests
        // without a Godot editor import pass), use a SystemFont chain.
        GD.PushWarning($"Theme: could not load {resPath}, falling back to SystemFont.");
        return new SystemFont
        {
            FontNames = new[]
            {
                "Inter", "Inter UI", "Helvetica Neue",
                "Helvetica", "Segoe UI", "Arial",
            }
        };
    }
}
