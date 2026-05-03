namespace WarGame;

using Godot;
using System.Collections.Generic;
using WarGame.Sim;
using WarGame.Sim.Commands;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using WarGame.UI;
using Theme = WarGame.Render.Theme;
using MapRenderer = WarGame.Render.MapRenderer;
using MapGenerator = WarGame.Sim.Generation.MapGenerator;

// Phase 1 game scene. Owns the deterministic GameState, drives the sim at
// 30 Hz, and delegates drawing to /Render. Input handling is added in Step
// 8; this file currently exposes a small selection-state that the input
// controller will populate.
//
// Sim/render boundary: this scene reads `_state` heavily but only writes
// to it via the Command queue (which goes through GameSim.Step).
public partial class Game : Node2D
{
    private const float SimStepSeconds = 1f / GameSim.TicksPerSecond;

    private GameState _state;
    private float _accumulator;
    private Vector2 _viewportSize;
    private Vector2 _mapOrigin;

    // Cached fonts — loaded once from Theme, not rebuilt per frame.
    private Font _fontPrimary = null!;
    private Font _fontSemiBold = null!;

    // Pending commands queued by input. Drained on the next sim tick.
    private readonly List<Command> _pendingCommands = new();

    // Whose turn is it (hot-seat). Phase 1 PvP is real-time, so "active
    // player" is just the seat whose orders the keyboard/mouse currently
    // controls. Tab toggles in Step 8.
    public PlayerId ActivePlayer { get; private set; } = PlayerId.Player1;

    // Selection set — unit Ids the active player has selected. Phase 1
    // step 9 displays a yellow ring around each. Step 8 wires selection.
    public readonly HashSet<int> SelectedUnitIds = new();

    private InputController? _input;

    // ---- Combat flash state (render-only) --------------------------------
    // Tracks HP from the previous frame to detect damage events. Dictionary
    // key is unit Id. This is purely render state — never touches the sim.
    private readonly Dictionary<int, double> _lastHp = new();
    // Active combat flashes: unit Id → remaining seconds.
    private readonly Dictionary<int, float> _combatFlashes = new();
    private const float CombatFlashDuration = 0.25f;

    // ---- Move destination marker (render-only) ---------------------------
    // When the player right-clicks, we briefly show a pulsing ring at the
    // target tile. Decays over time.
    private int _moveTargetX = -1, _moveTargetY = -1;
    private float _moveMarkerLife;
    private const float MoveMarkerDuration = 0.8f;

    // ---- Road build preview (render-only) -------------------------------
    private readonly List<int> _roadPreviewPath = new();
    private bool _roadPreviewValid;

    public void SetMoveTarget(int tx, int ty)
    {
        _moveTargetX = tx;
        _moveTargetY = ty;
        _moveMarkerLife = MoveMarkerDuration;
    }

    public override void _Ready()
    {
        // Generate a procedural map from a random seed. In multiplayer,
        // both clients will share the same seed to produce identical maps.
        ulong mapSeed = (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = MapGenerator.Generate(mapSeed);
        _state = GameState.Initial(seed: mapSeed);
        _state.Map = result.Map;
        _state.TileOwner = new byte[result.Map.Width * result.Map.Height];
        _state.TileSupplyOwner = new byte[result.Map.Width * result.Map.Height];
        _state.TileRoadSupplyOwner = new byte[result.Map.Width * result.Map.Height];
        foreach (var city in result.Cities)
            _state.Cities.Add(city);
        // Each side starts with one light unit on its capital.
        _state.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light,
            result.Cities[0].TileX, result.Cities[0].TileY));
        _state.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light,
            result.Cities[1].TileX, result.Cities[1].TileY));
        // Populate derived territory before the first rendered frame. Without
        // this, startup can briefly show empty/non-authoritative territory
        // until the first sim tick runs.
        PowerProjection.Tick(ref _state);
        SupplyLines.Tick(ref _state);
        FogOfWar.Tick(ref _state);
        _fontPrimary = Theme.BuildPrimaryFont();
        _fontSemiBold = Theme.BuildSemiBoldFont();
        RecalculateLayout();

        // Spawn the input controller as a child node. Code-instantiated to
        // keep the scene .tscn dependency-free (and so Game.cs is the
        // single entry point for Phase 1).
        _input = new InputController { GameOwner = this };
        AddChild(_input);

        // React to window resize / fullscreen toggle by recentering the map.
        GetViewport().SizeChanged += RecalculateLayout;
    }

    public float MapZoom { get; private set; } = 1.0f;
    public Vector2 ScreenToMap(Vector2 screen) => (screen - _mapOrigin) / MapZoom;
    public Vector2 MapToScreen(Vector2 map) => (map * MapZoom) + _mapOrigin;

    public void ZoomMap(Vector2 screenFocus, float factor)
    {
        Vector2 mapFocusBefore = ScreenToMap(screenFocus);
        MapZoom = Mathf.Clamp(MapZoom * factor, 0.25f, 3.0f);
        Vector2 screenFocusAfter = mapFocusBefore * MapZoom + _mapOrigin;
        _mapOrigin += screenFocus - screenFocusAfter;
        QueueRedraw();
    }

    private void RecalculateLayout()
    {
        _viewportSize = GetViewportRect().Size;
        if (_mapOrigin == Vector2.Zero && MapZoom == 1.0f)
        {
            // Initial center on first load
            float mapPxW = _state.Map.Width * MapRenderer.TilePx;
            float mapPxH = _state.Map.Height * MapRenderer.TilePx;
            const float topReserve = 52f, bottomReserve = 40f;
            float availH = _viewportSize.Y - topReserve - bottomReserve;
            _mapOrigin = new Vector2(
                Mathf.Max(0, (_viewportSize.X - mapPxW) * 0.5f),
                topReserve + Mathf.Max(0, (availH - mapPxH) * 0.5f));
        }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        
        // Handle Camera Panning
        float panSpeed = 600f * dt / MapZoom;
        bool panned = false;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) { _mapOrigin.Y += panSpeed; panned = true; }
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) { _mapOrigin.Y -= panSpeed; panned = true; }
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) { _mapOrigin.X += panSpeed; panned = true; }
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) { _mapOrigin.X -= panSpeed; panned = true; }
        if (panned) QueueRedraw();

        _accumulator += dt;
        while (_accumulator >= SimStepSeconds)
        {
            // Snapshot HP for combat flash detection before the sim tick.
            SnapshotHp();

            // Drain any commands queued since last tick. The list is
            // cleared after dispatch so each command runs exactly once.
            List<Command>? cmds = _pendingCommands.Count > 0 ? new List<Command>(_pendingCommands) : null;
            _pendingCommands.Clear();
            _state = GameSim.Step(_state, cmds);
            _accumulator -= SimStepSeconds;

            // Detect damage events by comparing to snapshot.
            DetectCombatFlashes();
        }

        // Decay combat flashes. Build a new dict rather than mutating
        // during iteration — modifying values while enumerating a Dictionary
        // throws InvalidOperationException (the freeze-on-death bug).
        var surviving = new Dictionary<int, float>();
        foreach (var kv in _combatFlashes)
        {
            float remaining = kv.Value - dt;
            if (remaining > 0f)
                surviving[kv.Key] = remaining;
        }
        _combatFlashes.Clear();
        foreach (var kv in surviving)
            _combatFlashes[kv.Key] = kv.Value;

        // Decay move marker.
        if (_moveMarkerLife > 0f)
            _moveMarkerLife -= dt;

        QueueRedraw();
    }

    private void SnapshotHp()
    {
        _lastHp.Clear();
        for (int i = 0; i < _state.Units.Count; i++)
        {
            Unit u = _state.Units[i];
            if (u.IsAlive)
                _lastHp[i] = u.Hp.ToDoubleUnsafe();
        }
    }

    private void DetectCombatFlashes()
    {
        for (int i = 0; i < _state.Units.Count; i++)
        {
            Unit u = _state.Units[i];
            if (!u.IsAlive) continue;
            double currentHp = u.Hp.ToDoubleUnsafe();
            if (_lastHp.TryGetValue(i, out double prevHp) && currentHp < prevHp)
            {
                // Unit took damage — trigger flash.
                _combatFlashes[i] = CombatFlashDuration;
            }
        }
    }

    public override void _Draw()
    {
        if (_fontPrimary == null) return;

        // Background slab.
        DrawRect(new Rect2(Vector2.Zero, _viewportSize), Theme.BgVoid);

        // Draw Map with Transform
        DrawSetTransform(_mapOrigin, 0f, new Vector2(MapZoom, MapZoom));

        MapRenderer.Draw(this, _state, Vector2.Zero, ActivePlayer);

        // Road/bridge preview from B build mode.
        DrawRoadPreview();

        // City hover highlight.
        DrawCityHover();

        // Selection rings. We do this here (instead of MapRenderer) because
        // it uses Game's transient Selection state. Draw in map space.
        // track moving units pixel-for-pixel rather than snapping to the
        // anchor tile.
        foreach (int id in SelectedUnitIds)
        {
            if ((uint)id >= (uint)_state.Units.Count) continue;
            Unit u = _state.Units[id];
            if (!u.IsAlive) continue;
            if (!FogOfWar.IsVisible(_state, ActivePlayer, u.TileX, u.TileY)) continue;
            Vector2 c = MapRenderer.UnitVisualCenter(u, _state.Map, Vector2.Zero);
            float r = MapRenderer.UnitRadius(u.Type) + 4f;
            DrawArc(c, r, 0, Mathf.Tau, 32, Theme.SelectRing, 2f);
        }

        // Combat flashes — radial pulses on damaged units.
        DrawCombatFlashes();

        // Move destination marker.
        DrawMoveMarker();

        // Drag-select box (drawn over selection rings, under HUD).
        if (_input is { DraggingBox: true })
        {
            Vector2 a = _input.DragStart, b = _input.DragCurrent;
            Vector2 tl = new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y));
            Vector2 br = new(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y));
            var box = new Rect2(tl, br - tl);
            DrawRect(box, Theme.BoxSelect, filled: true);
            DrawRect(box, Theme.SelectRing, filled: false, width: 1.5f / MapZoom); // Unscaled width
        }

        // Reset Transform for HUD elements
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // HUD bars.
        DrawHudTop();
        DrawHudBottom();
        DrawSelectedUnitPanel();

        if (_input is { PromotionMenuVisible: true })
            DrawPromotionMenu(_input);

        // Production menu (on top of everything).
        if (_input is { MenuVisible: true })
            DrawProductionMenu(_input);

        // Victory banner — drawn last so it sits above all gameplay.
        if (_state.Winner != PlayerId.None)
            DrawVictoryBanner(_state.Winner);
    }

    public void SetRoadPreview(List<int> path, bool valid)
    {
        _roadPreviewPath.Clear();
        _roadPreviewPath.AddRange(path);
        _roadPreviewValid = valid;
        QueueRedraw();
    }

    public void ClearRoadPreview()
    {
        if (_roadPreviewPath.Count == 0 && !_roadPreviewValid) return;
        _roadPreviewPath.Clear();
        _roadPreviewValid = false;
        QueueRedraw();
    }

    // ---- Combat flash rendering ------------------------------------------
    private void DrawCombatFlashes()
    {
        foreach (var kv in _combatFlashes)
        {
            int id = kv.Key;
            float remaining = kv.Value;
            if ((uint)id >= (uint)_state.Units.Count) continue;
            Unit u = _state.Units[id];
            if (!u.IsAlive) continue;
            if (!FogOfWar.IsVisible(_state, ActivePlayer, u.TileX, u.TileY)) continue;

            Vector2 c = MapRenderer.UnitVisualCenter(u, _state.Map, Vector2.Zero);
            float frac = remaining / CombatFlashDuration; // 1 → 0
            float radius = MapRenderer.UnitRadius(u.Type) + 8f + (1f - frac) * 12f;
            Color flash = Theme.CombatFlash;
            flash.A *= frac * 0.7f;
            DrawArc(c, radius, 0, Mathf.Tau, 24, flash, 2.5f / MapZoom);
        }
    }

    // ---- Move marker rendering -------------------------------------------
    private void DrawMoveMarker()
    {
        if (_moveMarkerLife <= 0f) return;
        if (_moveTargetX < 0 || _moveTargetY < 0) return;
        if (!FogOfWar.IsKnown(_state, ActivePlayer, _moveTargetX, _moveTargetY)) return;

        Vector2 center = MapRenderer.TileCenter(_moveTargetX, _moveTargetY, Vector2.Zero);
        float frac = _moveMarkerLife / MoveMarkerDuration; // 1 → 0
        // Pulse: small ring that expands and fades.
        float radius = MapRenderer.TilePx * 0.3f + (1f - frac) * MapRenderer.TilePx * 0.2f;
        Color c = Theme.MoveMarker;
        c.A *= frac;
        DrawArc(center, radius, 0, Mathf.Tau, 24, c, 2f);
        // Inner dot.
        Color dot = Theme.MoveMarker;
        dot.A *= frac * 0.5f;
        DrawCircle(center, 3f / MapZoom, dot);
    }

    private void DrawRoadPreview()
    {
        if (_roadPreviewPath.Count == 0) return;

        for (int i = 0; i < _roadPreviewPath.Count; i++)
        {
            int flat = _roadPreviewPath[i];
            int x = flat % _state.Map.Width, y = flat / _state.Map.Width;
            if (!FogOfWar.IsVisible(_state, ActivePlayer, x, y)) continue;
            TileType t = _state.Map.GetTileUnchecked(x, y);
            Color c = !_roadPreviewValid
                ? Theme.InvalidPreview
                : Pathfinding.IsBridgeTerrain(t) ? Theme.BridgePreview : Theme.RoadPreview;
            c.A *= i == _roadPreviewPath.Count - 1 ? 1.0f : 0.72f;
            Rect2 r = new(
                MapRenderer.TileTopLeft(x, y, Vector2.Zero) + new Vector2(MapRenderer.TilePx * 0.12f, MapRenderer.TilePx * 0.12f),
                new Vector2(MapRenderer.TilePx * 0.76f, MapRenderer.TilePx * 0.76f));
            DrawRect(r, c);
            Color edge = _roadPreviewValid ? Theme.SelectRing : Theme.HpBarLow;
            edge.A = 0.9f;
            DrawRect(r, edge, filled: false, width: 1.5f / MapZoom);
        }
    }

    // ---- City hover highlight --------------------------------------------
    private void DrawCityHover()
    {
        if (_input is null) return;
        int hovId = _input.HoveredCityId;
        if (hovId < 0 || (uint)hovId >= (uint)_state.Cities.Count) return;

        // Don't draw hover if the production menu is already open for this city.
        if (_input.MenuVisible && _input.MenuCityId == hovId) return;

        City city = _state.Cities[hovId];
        Vector2 center = MapRenderer.TileCenter(city.TileX, city.TileY, Vector2.Zero);
        float half = city.IsCapital ? MapRenderer.CapitalMarkerHalf : MapRenderer.CityMarkerHalf;

        // Subtle pulsing glow around the city marker.
        float time = (float)(Time.GetTicksMsec() / 1000.0);
        float pulse = 0.5f + 0.5f * Mathf.Sin(time * 4f);
        Color glow = Theme.CityHover;
        glow.A *= (0.4f + pulse * 0.3f);
        DrawArc(center, half + 4f, 0, Mathf.Tau, 32, glow, 2f / MapZoom);
    }

    private void DrawVictoryBanner(PlayerId winner)
    {
        // Translucent vignette over everything to drop the gameplay back.
        Color veil = Theme.BgVoid; veil.A = 0.55f;
        DrawRect(new Rect2(Vector2.Zero, _viewportSize), veil);

        // Centered banner.
        const int titleSize = 64;
        const int subSize = 22;

        string label = winner == PlayerId.Player1 ? "Player 1 wins" : "Player 2 wins";
        Color faction = Theme.ForPlayer(winner);

        Vector2 mid = _viewportSize * 0.5f;
        // Banner panel.
        var panel = new Rect2(mid - new Vector2(360, 110), new Vector2(720, 220));
        DrawRect(panel, Theme.HudPanel);
        DrawRect(panel, faction, filled: false, width: 3f);

        // DrawString's centered alignment centers within the width starting
        // at Position.X; passing the panel midpoint shifts text half a panel
        // too far right.
        Vector2 titlePos = new(panel.Position.X, panel.Position.Y + 90);
        DrawString(_fontSemiBold, titlePos,
            label, HorizontalAlignment.Center, (int)panel.Size.X, titleSize, faction);

        // Subline — also centered.
        Vector2 subPos = new(panel.Position.X, panel.Position.Y + 150);
        DrawString(_fontPrimary, subPos,
            "B = Build Road · F = Build Fort · R = Raze Fort · Esc = Clear selection",
            HorizontalAlignment.Center, (int)panel.Size.X, subSize, Theme.HudTextDim);
    }

    private void DrawProductionMenu(InputController ic)
    {
        // Panel background.
        DrawRect(ic.MenuRect, Theme.MenuBg, filled: true);
        DrawRect(ic.MenuRect, Theme.MenuBorder, filled: false, width: 1.5f);

        // Title — show city status.
        bool validCity = (uint)ic.MenuCityId < (uint)_state.Cities.Count;
        City c = validCity ? _state.Cities[ic.MenuCityId] : default;
        if (validCity && _state.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile())
            validCity = false;

        string title = !validCity ? "Production" : c.DisplayName;
        DrawString(_fontSemiBold, ic.MenuRect.Position + new Vector2(12, 22),
            title, HorizontalAlignment.Left, 150, 14, Theme.HudText);

        if (validCity)
        {
            DrawRect(ic.MenuEditNameButtonRect, Theme.HudPanelEdge, filled: false, width: 1f);
            DrawString(_fontPrimary, ic.MenuEditNameButtonRect.Position + new Vector2(8, 16),
                "E", HorizontalAlignment.Left, -1, 12, Theme.HudTextDim);
        }

        float contentY = ic.RenameMode ? 36f : 0f;
        if (validCity && ic.RenameMode)
        {
            DrawRect(ic.MenuNameInputRect, Theme.HudPanel, filled: true);
            DrawRect(ic.MenuNameInputRect, Theme.SelectRing, filled: false, width: 1f);
            string draft = string.IsNullOrEmpty(ic.RenameDraft) ? "Enter city name" : ic.RenameDraft;
            Color draftColor = string.IsNullOrEmpty(ic.RenameDraft) ? Theme.HudTextDim : Theme.HudText;
            DrawString(_fontPrimary, ic.MenuNameInputRect.Position + new Vector2(8, 18),
                draft, HorizontalAlignment.Left, (int)ic.MenuNameInputRect.Size.X - 16, 13, draftColor);
        }

        if (validCity && c.IsProducing)
        {
            // ---- Producing layout ----
            // The text and progress bar live ABOVE the single Cancel button.
            // Layout (relative to menu top):
            //   0–28:  title row
            //  28–36:  gap
            //  36–54:  progress label
            //  54–60:  gap
            //  60–70:  progress bar
            //  70–80:  gap
            //  80–112: cancel button
            // 112–124: bottom padding
            UnitType type = (UnitType)(c.ProductionOrder - 1);
            int costEco = UnitStats.EcoCost(type);
            float frac = Mathf.Clamp((float)(c.ProductionProgress.ToDoubleUnsafe() / costEco), 0f, 1f);

            // Progress label.
            string label = $"Building {type}   {(int)(frac * 100f)}%";
            DrawString(_fontPrimary, ic.MenuRect.Position + new Vector2(12, 50 + contentY),
                label, HorizontalAlignment.Left, -1, 13, Theme.HudText);

            // Progress bar.
            float barW = ic.MenuRect.Size.X - 24f;
            Vector2 barTl = ic.MenuRect.Position + new Vector2(12, 60 + contentY);
            DrawRect(new Rect2(barTl, new Vector2(barW, 8)), Theme.ProgressBarBg);
            DrawRect(new Rect2(barTl, new Vector2(barW * frac, 8)), Theme.ForPlayer(c.Owner));

            // Cancel order button.
            DrawButton(ic.MenuLightButtonRect, "Cancel order  [Q]", warning: true);
        }
        else
        {
            // ---- Idle layout ----
            // Layout (relative to menu top):
            //   0–28:  title row
            //  28–36:  gap
            //  36–68:  light button
            //  68–76:  gap
            //  76–108: heavy button
            // 108–120: bottom padding
            DrawButton(ic.MenuLightButtonRect, $"Build Light  ({UnitStats.LightEcoCost} ECO)  [Q]");
            DrawButton(ic.MenuHeavyButtonRect, $"Build Heavy ({UnitStats.HeavyEcoCost} ECO)  [W]");
        }

        // Close "x" button.
        DrawRect(ic.MenuCancelButtonRect, Theme.HudPanelEdge, filled: false, width: 1f);
        DrawString(_fontPrimary, ic.MenuCancelButtonRect.Position + new Vector2(8, 16),
            "✕", HorizontalAlignment.Left, -1, 12, Theme.HudTextDim);
    }

    private void DrawButton(Rect2 r, string label, bool warning = false)
    {
        Color edge = warning ? Theme.CancelBtnEdge : Theme.HudPanelEdge;
        Color bg = Theme.HudPanel;
        bg.A = 0.5f;
        DrawRect(r, bg, filled: true);
        DrawRect(r, edge, filled: false, width: 1f);

        Color text = warning ? Theme.WarningText : Theme.HudText;
        // Vertically center text inside the button.
        DrawString(_fontPrimary, r.Position + new Vector2(10, 21),
            label, HorizontalAlignment.Left, -1, 13, text);
    }

    private void DrawSelectedUnitPanel()
    {
        if (SelectedUnitIds.Count != 1) return;
        int unitId = -1;
        foreach (int id in SelectedUnitIds) { unitId = id; break; }
        if ((uint)unitId >= (uint)_state.Units.Count) return;

        Unit u = _state.Units[unitId];
        if (!u.IsAlive || u.Owner != ActivePlayer) return;
        if (!FogOfWar.IsVisible(_state, ActivePlayer, u.TileX, u.TileY)) return;

        const float w = 380f;
        const float h = 104f;
        float y = _viewportSize.Y - 36f - h - 10f;
        var panel = new Rect2(new Vector2(12, y), new Vector2(w, h));
        DrawRect(panel, Theme.MenuBg);
        DrawRect(panel, Theme.MenuBorder, filled: false, width: 1f);

        Color faction = Theme.ForPlayer(u.Owner);
        DrawCircle(panel.Position + new Vector2(18, 21), 5f, faction);
        string title = $"{u.Type} #{u.Id}   Rank {u.Rank}";
        DrawString(_fontSemiBold, panel.Position + new Vector2(30, 25),
            title, HorizontalAlignment.Left, -1, 14, Theme.HudText);

        int hp = Mathf.Max(0, u.Hp.ToInt());
        int maxHp = UnitStats.MaxHp(u.Type).ToInt();
        string supply = SupplyLines.GetUnitStatus(_state, unitId).ToString();
        DrawString(_fontPrimary, panel.Position + new Vector2(12, 48),
            $"HP {hp}/{maxHp}   {XpProgressText(u)}   Supply: {supply}",
            HorizontalAlignment.Left, -1, 12, Theme.HudText);

        string perks = PerksText(u);
        DrawString(_fontPrimary, panel.Position + new Vector2(12, 70),
            string.IsNullOrEmpty(perks) ? "Perks: none" : $"Perks: {perks}",
            HorizontalAlignment.Left, (int)w - 24, 12, Theme.HudTextDim);

        if (u.PromotionPoints > 0)
        {
            DrawString(_fontSemiBold, panel.Position + new Vector2(12, 92),
                $"Promotion ready [{u.PromotionPoints}]   Press P",
                HorizontalAlignment.Left, -1, 12, Theme.SelectRing);
        }
    }

    private void DrawPromotionMenu(InputController ic)
    {
        if ((uint)ic.PromotionUnitId >= (uint)_state.Units.Count) return;
        Unit u = _state.Units[ic.PromotionUnitId];
        if (!u.IsAlive) return;

        DrawRect(ic.PromotionMenuRect, Theme.MenuBg);
        DrawRect(ic.PromotionMenuRect, Theme.MenuBorder, filled: false, width: 1f);

        DrawString(_fontSemiBold, ic.PromotionMenuRect.Position + new Vector2(12, 24),
            $"Promote {u.Type} #{u.Id}", HorizontalAlignment.Left, -1, 14, Theme.HudText);

        byte[] perks = PromotionPerksFor(u.Type);
        for (int i = 0; i < ic.PromotionPerkButtonRects.Length; i++)
        {
            byte perkId = perks[i];
            Rect2 r = ic.PromotionPerkButtonRects[i];
            bool taken = HasPerk(u, perkId);

            Color bg = Theme.HudPanel;
            bg.A = taken ? 0.32f : 0.62f;
            Color edge = taken ? Theme.HudPanelEdge : Theme.SelectRing;
            DrawRect(r, bg);
            DrawRect(r, edge, filled: false, width: 1f);

            Color nameColor = taken ? Theme.HudTextDim : Theme.HudText;
            DrawString(_fontSemiBold, r.Position + new Vector2(8, 14),
                UnitProgression.PerkName(perkId),
                HorizontalAlignment.Left, 105, 12, nameColor);
            DrawString(_fontPrimary, r.Position + new Vector2(112, 14),
                taken ? "taken" : PromotionEffectText(perkId),
                HorizontalAlignment.Left, (int)r.Size.X - 120, 11,
                taken ? Theme.HudTextDim : Theme.HudText);
        }
    }

    private static string XpProgressText(in Unit u)
    {
        if (u.Rank >= UnitProgression.MaxRank)
            return $"XP {RawToInt(u.XpRaw)} max";

        long prev = u.Rank switch
        {
            <= 1 => 0,
            2 => UnitProgression.Rank2Xp.Raw,
            3 => UnitProgression.Rank3Xp.Raw,
            _ => UnitProgression.Rank4Xp.Raw,
        };
        long next = UnitProgression.CurrentRankThreshold(u).Raw;
        return $"XP {RawToInt(u.XpRaw - prev)}/{RawToInt(next - prev)}";
    }

    private static int RawToInt(long raw) => (int)(raw >> FP.FractionalBits);

    private static string PerksText(in Unit u)
    {
        byte[] perks = PromotionPerksFor(u.Type);
        var names = new List<string>();
        for (int i = 0; i < perks.Length; i++)
            if (HasPerk(u, perks[i])) names.Add(UnitProgression.PerkName(perks[i]));
        return string.Join(", ", names);
    }

    private static byte[] PromotionPerksFor(UnitType type) => type == UnitType.Heavy
        ? new byte[]
        {
            (byte)UnitPerk.HeavyPlating,
            (byte)UnitPerk.HeavyHullDown,
            (byte)UnitPerk.HeavyGunnery,
            (byte)UnitPerk.HeavyBreacher,
            (byte)UnitPerk.HeavyStabilizers,
            (byte)UnitPerk.HeavySpotterCrew,
        }
        : new byte[]
        {
            (byte)UnitPerk.LightOptics,
            (byte)UnitPerk.LightPathfinder,
            (byte)UnitPerk.LightQuickMarch,
            (byte)UnitPerk.LightRoadRunner,
            (byte)UnitPerk.LightPackTactics,
            (byte)UnitPerk.LightScreenLine,
        };

    private static bool HasPerk(in Unit u, byte perkId)
        => (u.PerkMask & (1u << (perkId - 1))) != 0;

    private static string PromotionEffectText(byte perkId) => ((UnitPerk)perkId) switch
    {
        UnitPerk.LightOptics => "+1 vision",
        UnitPerk.LightPathfinder => "better rough speed",
        UnitPerk.LightQuickMarch => "+10% land speed",
        UnitPerk.LightRoadRunner => "faster roads",
        UnitPerk.LightPackTactics => "+7% focus fire",
        UnitPerk.LightScreenLine => "-7% adjacent defense",
        UnitPerk.HeavyPlating => "-7% stationary damage",
        UnitPerk.HeavyHullDown => "better cover defense",
        UnitPerk.HeavyGunnery => "+8% stationary attack",
        UnitPerk.HeavyBreacher => "+10% vs built tiles",
        UnitPerk.HeavyStabilizers => "lower move penalty",
        UnitPerk.HeavySpotterCrew => "+vision/support damage",
        _ => "",
    };

    // ---- HUD — Top bar ---------------------------------------------------
    private void DrawHudTop()
    {
        const int titleSize = 18;
        const int bodySize = 13;

        // Top bar panel.
        var topBar = new Rect2(0, 0, _viewportSize.X, 48);
        DrawRect(topBar, Theme.HudPanel);
        DrawLine(new Vector2(0, 48), new Vector2(_viewportSize.X, 48), Theme.HudPanelEdge, 1);

        // Title left.
        DrawString(_fontSemiBold, new Vector2(20, 30), "WarGame",
            HorizontalAlignment.Left, -1, titleSize, Theme.HudText);

        // Tick counter (dim, next to title).
        string tickStr = $"tick {_state.Tick}";
        DrawString(_fontPrimary, new Vector2(110, 30), tickStr,
            HorizontalAlignment.Left, -1, 12, Theme.HudTextDim);

        // Active-player indicator — larger, more prominent.
        Color chip = Theme.ForPlayer(ActivePlayer);
        float chipX = _viewportSize.X - 160;
        Vector2 chipCenter = new(chipX, 24);

        // Background pill.
        Color pillBg = chip;
        pillBg.A = 0.15f;
        Rect2 pill = new(chipCenter - new Vector2(8, 12), new Vector2(150, 24));
        DrawRect(pill, pillBg);
        DrawRect(pill, chip, filled: false, width: 1f);

        // Dot + label inside the pill.
        DrawCircle(chipCenter + new Vector2(4, 0), 5, chip);
        string chipLabel = ActivePlayer == PlayerId.Player1 ? "Player 1" : "Player 2";
        DrawString(_fontSemiBold, chipCenter + new Vector2(14, 5), chipLabel,
            HorizontalAlignment.Left, -1, bodySize, Theme.HudText);

        // Tab hint far right.
        DrawString(_fontPrimary, new Vector2(_viewportSize.X - 14, 30), "[Tab]",
            HorizontalAlignment.Right, -1, 11, Theme.HudTextDim);
    }

    // ---- HUD — Bottom resource bar ---------------------------------------
    private void DrawHudBottom()
    {
        const int bodySize = 13;
        const float barH = 36f;
        float barY = _viewportSize.Y - barH;

        // Full-width panel.
        var bottomBar = new Rect2(0, barY, _viewportSize.X, barH);
        DrawRect(bottomBar, Theme.HudBottomBar);
        DrawLine(new Vector2(0, barY), new Vector2(_viewportSize.X, barY), Theme.HudPanelEdge, 1);

        float halfW = _viewportSize.X * 0.5f;

        // ---- Player 1 (left half) ----
        Rect2 p1Bg = new(0, barY, halfW - 1, barH);
        DrawRect(p1Bg, Theme.P1BgTint);

        DrawCircle(new Vector2(16, barY + barH * 0.5f), 4, Theme.P1);
        string p1Status = HudStatusFor(PlayerId.Player1);
        DrawString(_fontPrimary, new Vector2(28, barY + 23), p1Status,
            HorizontalAlignment.Left, -1, bodySize, Theme.HudText);

        // ---- Player 2 (right half) ----
        Rect2 p2Bg = new(halfW + 1, barY, halfW - 1, barH);
        DrawRect(p2Bg, Theme.P2BgTint);

        DrawCircle(new Vector2(halfW + 16, barY + barH * 0.5f), 4, Theme.P2);
        string p2Status = HudStatusFor(PlayerId.Player2);
        DrawString(_fontPrimary, new Vector2(halfW + 28, barY + 23), p2Status,
            HorizontalAlignment.Left, -1, bodySize, Theme.HudText);
    }

    private string HudStatusFor(PlayerId p)
    {
        string label = p == PlayerId.Player1 ? "P1" : "P2";
        if (p == ActivePlayer)
        {
            FP eco = _state.Players[(int)p].Eco;
            return $"{label}   ECO: {eco.ToInt()}   Units: {CountPlayerUnits(p)}   Cut off: {CountCutOffUnits(p)}   Cities: {CountPlayerCities(p)}";
        }

        return $"{label}   ECO: ?   Visible units: {CountVisibleUnits(p)}   Cut off: ?   Known cities: {CountKnownCities(p)}";
    }

    private int CountPlayerUnits(PlayerId p)
    {
        int count = 0;
        for (int i = 0; i < _state.Units.Count; i++)
            if (_state.Units[i].IsAlive && _state.Units[i].Owner == p) count++;
        return count;
    }

    private int CountPlayerCities(PlayerId p)
    {
        int count = 0;
        for (int i = 0; i < _state.Cities.Count; i++)
        {
            City c = _state.Cities[i];
            if (c.Owner != p) continue;
            if (_state.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile()) continue;
            count++;
        }
        return count;
    }

    private int CountVisibleUnits(PlayerId p)
    {
        int count = 0;
        for (int i = 0; i < _state.Units.Count; i++)
        {
            Unit u = _state.Units[i];
            if (!u.IsAlive || u.Owner != p) continue;
            if (FogOfWar.IsVisible(_state, ActivePlayer, u.TileX, u.TileY)) count++;
        }
        return count;
    }

    private int CountKnownCities(PlayerId p)
    {
        int count = 0;
        for (int i = 0; i < _state.Cities.Count; i++)
        {
            City c = _state.Cities[i];
            if (_state.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile()) continue;
            if (!FogOfWar.IsKnown(_state, ActivePlayer, c.TileX, c.TileY)) continue;
            if (FogOfWar.GetKnownTileOwner(_state, ActivePlayer, c.TileX, c.TileY) == p) count++;
        }
        return count;
    }

    private int CountCutOffUnits(PlayerId p)
    {
        int count = 0;
        for (int i = 0; i < _state.Units.Count; i++)
        {
            Unit u = _state.Units[i];
            if (!u.IsAlive || u.Owner != p) continue;
            if (SupplyLines.GetUnitStatus(_state, i) == SupplyStatus.CutOff) count++;
        }
        return count;
    }

    // ---- Public hooks for InputController (Step 8) -----------------------
    public Vector2 MapOrigin => _mapOrigin;
    public ref readonly GameState State => ref _state;
    public void EnqueueCommand(Command cmd) => _pendingCommands.Add(cmd);
    public void SwitchActivePlayer()
    {
        ActivePlayer = ActivePlayer == PlayerId.Player1 ? PlayerId.Player2 : PlayerId.Player1;
        QueueRedraw();
    }
}
