namespace WarGame.UI;

using Godot;
using System.Collections.Generic;
using WarGame.Sim.Commands;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using MapRenderer = WarGame.Render.MapRenderer;
using Theme = WarGame.Render.Theme;

// Hot-seat input controller for Phase 1. Attached as a child of the Game
// scene; relies on the public hooks Game.MapOrigin / Game.State /
// Game.EnqueueCommand / Game.SelectedUnitIds / Game.ActivePlayer.
//
// Bindings (PLAN.md / approved):
//   Left mouse button (down)        — start drag-select OR pick a unit
//   Left mouse button (up)          — finalize drag-select
//   Right mouse button              — issue MoveUnitCommand for selection
//   Click on owned city             — open production menu (popup)
//   Tab                             — switch active player (hot-seat)
//   Esc                             — clear selection / dismiss menus
//   Q                               — build Light unit (when city selected)
//   W                               — build Heavy unit (when city selected)
//
// The controller never mutates GameState directly. Selection state lives
// on Game (so the renderer can read it); commands flow through
// Game.EnqueueCommand which buffers them for the next sim tick.
public partial class InputController : Node
{
    [Export] public Node2D? GameOwner;     // assign to the Game scene root
    private Game Game => (GameOwner as Game)!;

    private Vector2? _dragStart;
    private bool _draggingBox;
    private Vector2 _dragCurrent;
    public bool DraggingBox => _draggingBox;
    public Vector2 DragStart => _dragStart ?? Vector2.Zero;
    public Vector2 DragCurrent => _dragCurrent;

    // Production menu visibility / target city. The Game scene draws the
    // menu in its _Draw if MenuVisible is true.
    public bool MenuVisible { get; private set; }
    public int MenuCityId { get; private set; } = -1;
    public Rect2 MenuRect { get; private set; }
    public Rect2 MenuLightButtonRect { get; private set; }
    public Rect2 MenuHeavyButtonRect { get; private set; }
    public Rect2 MenuCancelButtonRect { get; private set; }
    public bool RoadBuildMode { get; private set; }

    // City hover state — set each frame based on mouse position. The
    // renderer reads this to draw a highlight ring.
    public int HoveredCityId { get; private set; } = -1;

    // 6 px was too tight — small mouse jitter on click was triggering drag
    // mode and selecting nothing. 14 px gives a forgiving single-click.
    private const float DragThresholdPx = 14f;

    // Cached mouse position for hover-testing in _Process.
    private Vector2 _mousePos;

    public override void _Ready()
    {
        // Belt and braces: in some Godot setups `_UnhandledInput` does not
        // fire on a freshly-coded `Node` until processing is explicitly
        // enabled. Setting it here is a no-op when defaults already cover
        // it, but eliminates a class of "input doesn't fire" debugging.
        SetProcessUnhandledInput(true);
        SetProcessInput(true);
    }

    public override void _Process(double delta)
    {
        _mousePos = Game.GetViewport().GetMousePosition();
        UpdateHoveredCity();
        UpdateRoadPreview();
        RefreshMenuRects();
    }

    public override void _Input(InputEvent @event)
    {
        // We mostly use _UnhandledInput so we don't intercept GUI clicks,
        // but we need _Input for keyboard (Tab / Esc) which can otherwise
        // be swallowed by editor focus on some platforms.
        if (@event is InputEventKey ek && ek.Pressed && !ek.Echo)
            HandleKey(ek);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Keyboard handled in _Input above; mouse events handled here so
        // GUI controls (none in Phase 1, but reserving the contract)
        // can still consume clicks before us.
        switch (@event)
        {
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                break;
            case InputEventMouseMotion mm:
                HandleMouseMotion(mm);
                break;
        }
    }

    // ---- Mouse ------------------------------------------------------------
    private void HandleMouseButton(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (MenuVisible && TryHitProductionMenu(mb.Position)) return;
                if (RoadBuildMode)
                {
                    CommitRoadBuildAt(mb.Position);
                    return;
                }
                _dragStart = Game.ScreenToMap(mb.Position);
                _dragCurrent = Game.ScreenToMap(mb.Position);
                _draggingBox = false;
            }
            else
            {
                if (_dragStart is null) return;
                FinishLeftClickOrDrag(mb.Position);
                _dragStart = null;
                _draggingBox = false;
            }
            return;
        }

        if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
        {
            if (RoadBuildMode)
            {
                ExitRoadBuildMode();
                return;
            }
            IssueMoveOrderToSelection(mb.Position);
            return;
        }

        if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
        {
            Game.ZoomMap(mb.Position, 1.1f);
            return;
        }

        if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
        {
            Game.ZoomMap(mb.Position, 1.0f / 1.1f);
            return;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        if (_dragStart is null) return;
        Vector2 mapPos = Game.ScreenToMap(mm.Position);
        _dragCurrent = mapPos;
        if (!_draggingBox && mapPos.DistanceTo(_dragStart.Value) > DragThresholdPx / Game.MapZoom)
            _draggingBox = true;
    }

    private void FinishLeftClickOrDrag(Vector2 endPos)
    {
        if (_draggingBox && _dragStart.HasValue)
        {
            SelectUnitsInBox(_dragStart.Value, Game.ScreenToMap(endPos));
            return;
        }

        // Single click: try city first (production), then unit (select).
        if (TryOpenProductionMenuAt(endPos)) return;
        SingleClickSelectAt(endPos);
    }

    private void SingleClickSelectAt(Vector2 screenPos)
    {
        // Distance-to-visual-center test rather than tile equality, so a
        // moving unit (rendered between tiles) is still clickable. Pick
        // the unit whose visual center is closest to the click and within
        // its own radius + a small grab-zone.
        Game.SelectedUnitIds.Clear();
        ref readonly GameState st = ref Game.State;

        int bestId = -1;
        float bestDistSq = float.PositiveInfinity;
        Vector2 mapPos = Game.ScreenToMap(screenPos);
        for (int i = 0; i < st.Units.Count; i++)
        {
            Unit u = st.Units[i];
            if (!u.IsAlive) continue;
            if (u.Owner != Game.ActivePlayer) continue;
            Vector2 c = MapRenderer.UnitVisualCenter(u, st.Map, Vector2.Zero);
            float r = MapRenderer.UnitRadius(u.Type) + 6f;     // 6-px grab tolerance
            float distSq = c.DistanceSquaredTo(mapPos);
            if (distSq <= r * r && distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestId = i;
            }
        }
        if (bestId >= 0) Game.SelectedUnitIds.Add(bestId);
    }

    private void SelectUnitsInBox(Vector2 a, Vector2 b)
    {
        Game.SelectedUnitIds.Clear();
        Vector2 tl = new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y));
        Vector2 br = new(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y));
        var box = new Rect2(tl, br - tl);

        ref readonly GameState st = ref Game.State;
        for (int i = 0; i < st.Units.Count; i++)
        {
            Unit u = st.Units[i];
            if (!u.IsAlive) continue;
            if (u.Owner != Game.ActivePlayer) continue;
            // Use visual center so moving units can be box-selected at their
            // rendered position, not their anchor tile. Matches single-click
            // hit-testing behavior.
            Vector2 c = MapRenderer.UnitVisualCenter(u, st.Map, Vector2.Zero);
            if (box.HasPoint(c)) Game.SelectedUnitIds.Add(i);
        }
    }

    private void IssueMoveOrderToSelection(Vector2 screenPos)
    {
        if (Game.SelectedUnitIds.Count == 0) return;
        var (tx, ty) = MapRenderer.ScreenToTile(Game.ScreenToMap(screenPos), Vector2.Zero);
        ref readonly GameState st = ref Game.State;
        if (!st.Map.InBounds(tx, ty)) return;

        // Stable iteration order: ascending Id. Required for replay
        // determinism if multiple commands are issued in the same frame.
        var ids = new List<int>(Game.SelectedUnitIds);
        ids.Sort();
        foreach (int id in ids)
        {
            Game.EnqueueCommand(new MoveUnitCommand(id, tx, ty)
            {
                PlayerId = (int)Game.ActivePlayer
            });
        }

        // Record the move target for the destination marker VFX.
        Game.SetMoveTarget(tx, ty);
    }

    // ---- Production menu --------------------------------------------------
    private bool TryOpenProductionMenuAt(Vector2 screenPos)
    {
        var (tx, ty) = MapRenderer.ScreenToTile(Game.ScreenToMap(screenPos), Vector2.Zero);
        ref readonly GameState st = ref Game.State;
        if (!st.Map.InBounds(tx, ty)) return false;
        if (st.Map.GetTileUnchecked(tx, ty).IsFortTile()) return false;

        for (int i = 0; i < st.Cities.Count; i++)
        {
            City c = st.Cities[i];
            if (c.TileX != tx || c.TileY != ty) continue;
            if (c.Owner != Game.ActivePlayer) return false;     // can't order an enemy city
            OpenProductionMenu(i, screenPos);
            return true;
        }
        return false;
    }

    private void OpenProductionMenu(int cityId, Vector2 anchor)
    {
        MenuCityId = cityId;
        ComputeMenuRects(anchor);
        MenuVisible = true;
    }

    /// <summary>
    /// Recomputes menu rectangles based on current city production state.
    /// Called every frame from _Process so the layout adapts live when
    /// production starts, completes, or is cancelled while the menu is open.
    /// Also clamps the menu to the viewport so it never spawns off-screen.
    /// </summary>
    private void RefreshMenuRects()
    {
        if (!MenuVisible) return;
        if ((uint)MenuCityId >= (uint)Game.State.Cities.Count)
        {
            CloseMenu();
            return;
        }
        City menuCity = Game.State.Cities[MenuCityId];
        if (Game.State.Map.GetTileUnchecked(menuCity.TileX, menuCity.TileY).IsFortTile())
        {
            CloseMenu();
            return;
        }
        // Recompute rects using the current menu position (keep anchor).
        if (MenuVisible && MenuCityId >= 0)
        {
            City c = Game.State.Cities[MenuCityId];
            Vector2 mapPos = MapRenderer.TileCenter(c.TileX, c.TileY, Vector2.Zero);
            ComputeMenuRects(Game.MapToScreen(mapPos));
        }
    }

    private void ComputeMenuRects(Vector2 rawPos)
    {
        bool producing = (uint)MenuCityId < (uint)Game.State.Cities.Count
                         && Game.State.Cities[MenuCityId].IsProducing;

        const float w = 220f;
        const float bw = 196f, bh = 32f;
        const float pad = 12f;

        // Idle: title (28px) + gap(8) + lightBtn(32) + gap(8) + heavyBtn(32) + pad(12) = 120
        // Producing: title (28px) + gap(8) + label(18) + gap(6) + bar(10) + gap(10) + cancelBtn(32) + pad(12) = 124
        float h = producing ? 124f : 120f;

        // Clamp to viewport so the menu stays fully visible.
        Vector2 vp = Game.GetViewportRect().Size;
        float x = Mathf.Clamp(rawPos.X, 4f, vp.X - w - 4f);
        float y = Mathf.Clamp(rawPos.Y, 4f, vp.Y - h - 4f);
        Vector2 pos = new(x, y);

        MenuRect = new Rect2(pos, new Vector2(w, h));

        // Close "x" button — always top-right.
        MenuCancelButtonRect = new Rect2(pos + new Vector2(w - 28, 4), new Vector2(24, 24));

        if (producing)
        {
            // Single "Cancel order" button, placed below the progress bar.
            // Title area: 0–28, gap: 28–36, label: 36–54, gap: 54–60,
            // bar: 60–70, gap: 70–80, cancel button: 80–112.
            MenuLightButtonRect = new Rect2(pos + new Vector2(pad, 80), new Vector2(bw, bh));
            // Heavy button rect is unused during production. Place it
            // off-screen so hit-tests never match.
            MenuHeavyButtonRect = new Rect2(new Vector2(-999, -999), Vector2.Zero);
        }
        else
        {
            // Two build buttons stacked vertically.
            // Title: 0–28, gap: 28–36, light: 36–68, gap: 68–76, heavy: 76–108.
            MenuLightButtonRect = new Rect2(pos + new Vector2(pad, 36), new Vector2(bw, bh));
            MenuHeavyButtonRect = new Rect2(pos + new Vector2(pad, 76), new Vector2(bw, bh));
        }
    }

    private bool TryHitProductionMenu(Vector2 screenPos)
    {
        if (!MenuVisible) return false;
        if (MenuCancelButtonRect.HasPoint(screenPos))
        {
            CloseMenu();
            return true;
        }

        // The two main buttons swap meaning when the city is already
        // building something. Idle: [Build Light] [Build Heavy].
        // Producing: [Cancel order] [(hidden)].
        bool producing = (uint)MenuCityId < (uint)Game.State.Cities.Count
                         && Game.State.Cities[MenuCityId].IsProducing;

        if (MenuLightButtonRect.HasPoint(screenPos))
        {
            if (producing)
            {
                Game.EnqueueCommand(new BuildUnitCommand(MenuCityId, null)
                { PlayerId = (int)Game.ActivePlayer });
            }
            else
            {
                Game.EnqueueCommand(new BuildUnitCommand(MenuCityId, UnitType.Light)
                { PlayerId = (int)Game.ActivePlayer });
            }
            CloseMenu();
            return true;
        }
        if (MenuHeavyButtonRect.HasPoint(screenPos))
        {
            if (!producing)
            {
                Game.EnqueueCommand(new BuildUnitCommand(MenuCityId, UnitType.Heavy)
                { PlayerId = (int)Game.ActivePlayer });
                CloseMenu();
            }
            // Producing: button is hidden, swallow click without acting.
            return true;
        }
        // Click outside menu = pass-through to normal handling, but close.
        if (!MenuRect.HasPoint(screenPos))
        {
            CloseMenu();
            return false;
        }
        return true;     // click inside menu but on no button: swallow
    }

    public void CloseMenu()
    {
        MenuVisible = false;
        MenuCityId = -1;
    }

    // ---- City hover -------------------------------------------------------
    private void UpdateHoveredCity()
    {
        HoveredCityId = -1;
        var (tx, ty) = MapRenderer.ScreenToTile(Game.ScreenToMap(_mousePos), Vector2.Zero);
        ref readonly GameState st = ref Game.State;
        if (!st.Map.InBounds(tx, ty)) return;
        if (st.Map.GetTileUnchecked(tx, ty).IsFortTile()) return;

        for (int i = 0; i < st.Cities.Count; i++)
        {
            City c = st.Cities[i];
            if (c.TileX == tx && c.TileY == ty && c.Owner == Game.ActivePlayer)
            {
                HoveredCityId = i;
                return;
            }
        }
    }

    // ---- Keyboard ---------------------------------------------------------
    private void HandleKey(InputEventKey ek)
    {
        switch (ek.Keycode)
        {
            case Key.Tab:
                ExitRoadBuildMode();
                Game.SelectedUnitIds.Clear();
                CloseMenu();
                Game.SwitchActivePlayer();
                break;
            case Key.Escape:
                if (RoadBuildMode)
                {
                    ExitRoadBuildMode();
                    break;
                }
                Game.SelectedUnitIds.Clear();
                CloseMenu();
                break;
            case Key.F11:
                ToggleFullscreen();
                break;

            // Production shortcuts — only active when a production menu is
            // open and the city is idle (not already building).
            case Key.Q:
                TryKeyboardBuild(UnitType.Light);
                break;
            case Key.W:
                TryKeyboardBuild(UnitType.Heavy);
                break;

            // Fort shortcuts — work anywhere on the map.
            case Key.F:
                TryBuildFort();
                break;
            case Key.R:
                TryRazeFort();
                break;
            case Key.B:
                ToggleRoadBuildMode();
                break;
        }
    }

    private void ToggleRoadBuildMode()
    {
        if (RoadBuildMode)
        {
            ExitRoadBuildMode();
            return;
        }

        int unitId = SelectedRoadBuilderId();
        if (unitId < 0) return;
        CloseMenu();
        RoadBuildMode = true;
        UpdateRoadPreview();
    }

    private void ExitRoadBuildMode()
    {
        RoadBuildMode = false;
        Game.ClearRoadPreview();
    }

    private int SelectedRoadBuilderId()
    {
        if (Game.SelectedUnitIds.Count == 0) return -1;
        int best = int.MaxValue;
        ref readonly GameState st = ref Game.State;
        foreach (int id in Game.SelectedUnitIds)
        {
            if ((uint)id >= (uint)st.Units.Count) continue;
            Unit u = st.Units[id];
            if (!u.IsAlive || u.Owner != Game.ActivePlayer) continue;
            if (id < best) best = id;
        }
        return best == int.MaxValue ? -1 : best;
    }

    private void UpdateRoadPreview()
    {
        if (!RoadBuildMode) return;
        int unitId = SelectedRoadBuilderId();
        if (unitId < 0)
        {
            ExitRoadBuildMode();
            return;
        }

        ref readonly GameState st = ref Game.State;
        Unit u = st.Units[unitId];
        var (tx, ty) = MapRenderer.ScreenToTile(Game.ScreenToMap(_mousePos), Vector2.Zero);
        if (!st.Map.InBounds(tx, ty))
        {
            Game.SetRoadPreview(new List<int>(), false);
            return;
        }

        List<int> path = Pathfinding.FindRoadBuildPath(st.Map, u.TileX, u.TileY, tx, ty);
        Game.SetRoadPreview(path, path.Count > 0);
    }

    private void CommitRoadBuildAt(Vector2 screenPos)
    {
        int unitId = SelectedRoadBuilderId();
        if (unitId < 0)
        {
            ExitRoadBuildMode();
            return;
        }

        var (tx, ty) = MapRenderer.ScreenToTile(Game.ScreenToMap(screenPos), Vector2.Zero);
        ref readonly GameState st = ref Game.State;
        if (!st.Map.InBounds(tx, ty)) return;

        Unit u = st.Units[unitId];
        List<int> path = Pathfinding.FindRoadBuildPath(st.Map, u.TileX, u.TileY, tx, ty);
        if (path.Count == 0) return;

        Game.EnqueueCommand(new BuildRoadCommand(unitId, tx, ty)
        { PlayerId = (int)Game.ActivePlayer });
        ExitRoadBuildMode();
    }

    /// <summary>
    /// Build a fort at the tile under the mouse cursor. The sim validates
    /// all preconditions (Plains, territory, ECO, cap).
    /// </summary>
    private void TryBuildFort()
    {
        // Use screen coordinates so the HUD layout behaves correctly.
        Vector2 mouseMapPos = Game.ScreenToMap(_mousePos);
        var (tx, ty) = MapRenderer.ScreenToTile(mouseMapPos, Vector2.Zero);
        ref readonly GameState st = ref Game.State;
        if (!st.Map.InBounds(tx, ty)) return;

        Game.EnqueueCommand(new BuildFortCommand(tx, ty)
        { PlayerId = (int)Game.ActivePlayer });
    }

    /// <summary>
    /// Raze (destroy) a fort at the tile under the mouse cursor.
    /// Only works on Fort tiles the active player owns.
    /// </summary>
    private void TryRazeFort()
    {
        var (tx, ty) = MapRenderer.ScreenToTile(Game.ScreenToMap(_mousePos), Vector2.Zero);
        ref readonly GameState st = ref Game.State;
        if (!st.Map.InBounds(tx, ty)) return;

        Game.EnqueueCommand(new RazeFortCommand(tx, ty)
        { PlayerId = (int)Game.ActivePlayer });
    }

    private void TryKeyboardBuild(UnitType type)
    {
        if (!MenuVisible) return;
        if ((uint)MenuCityId >= (uint)Game.State.Cities.Count) return;

        City c = Game.State.Cities[MenuCityId];
        if (Game.State.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile())
        {
            CloseMenu();
            return;
        }
        if (c.IsProducing)
        {
            // If already producing, Q acts as cancel (same as clicking the
            // cancel button). W does nothing.
            if (type == UnitType.Light)
            {
                Game.EnqueueCommand(new BuildUnitCommand(MenuCityId, null)
                { PlayerId = (int)Game.ActivePlayer });
                CloseMenu();
            }
            return;
        }

        Game.EnqueueCommand(new BuildUnitCommand(MenuCityId, type)
        { PlayerId = (int)Game.ActivePlayer });
        CloseMenu();
    }

    private static void ToggleFullscreen()
    {
        DisplayServer.WindowMode mode = DisplayServer.WindowGetMode();
        DisplayServer.WindowSetMode(
            mode == DisplayServer.WindowMode.Fullscreen
                ? DisplayServer.WindowMode.Windowed
                : DisplayServer.WindowMode.Fullscreen);
    }
}
