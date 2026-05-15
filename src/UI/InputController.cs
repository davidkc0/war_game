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
    public Rect2 MenuAutoBuildToggleRect { get; private set; }
    public Rect2 MenuUpgradeButtonRect { get; private set; }
    public Rect2 MenuCancelUpgradeButtonRect { get; private set; }
    public Rect2 MenuCancelButtonRect { get; private set; }
    public Rect2 MenuEditNameButtonRect { get; private set; }
    public Rect2 MenuNameInputRect { get; private set; }
    public UnitType MenuSelectedBuildType { get; private set; } = UnitType.Light;
    public bool RenameMode { get; private set; }
    public string RenameDraft { get; private set; } = "";
    public bool RoadBuildMode { get; private set; }

    public bool PromotionMenuVisible { get; private set; }
    public int PromotionUnitId { get; private set; } = -1;
    public Rect2 PromotionMenuRect { get; private set; }
    public Rect2[] PromotionPerkButtonRects { get; } = new Rect2[6];

    // City hover state — set each frame based on mouse position. The
    // renderer reads this to draw a highlight ring.
    public int HoveredCityId { get; private set; } = -1;

    // 6 px was too tight — small mouse jitter on click was triggering drag
    // mode and selecting nothing. 14 px gives a forgiving single-click.
    private const float DragThresholdPx = 14f;
    private static readonly byte[] LightPromotionPerks =
    {
        (byte)UnitPerk.LightOptics,
        (byte)UnitPerk.LightPathfinder,
        (byte)UnitPerk.LightQuickMarch,
        (byte)UnitPerk.LightRoadRunner,
        (byte)UnitPerk.LightPackTactics,
        (byte)UnitPerk.LightScreenLine,
    };
    private static readonly byte[] HeavyPromotionPerks =
    {
        (byte)UnitPerk.HeavyPlating,
        (byte)UnitPerk.HeavyHullDown,
        (byte)UnitPerk.HeavyGunnery,
        (byte)UnitPerk.HeavyBreacher,
        (byte)UnitPerk.HeavyStabilizers,
        (byte)UnitPerk.HeavySpotterCrew,
    };

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
        RefreshPromotionMenuRects();
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
                if (PromotionMenuVisible && TryHitPromotionMenu(mb.Position)) return;
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
            if (PromotionMenuVisible)
            {
                ClosePromotionMenu();
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
        ClosePromotionMenu();
        ref readonly GameState st = ref Game.State;

        int bestId = -1;
        float bestDistSq = float.PositiveInfinity;
        Vector2 mapPos = Game.ScreenToMap(screenPos);
        for (int i = 0; i < st.Units.Count; i++)
        {
            Unit u = st.Units[i];
            if (!u.IsAlive) continue;
            if (u.Owner != Game.ActivePlayer) continue;
            if (!FogOfWar.IsVisible(st, Game.ActivePlayer, u.TileX, u.TileY)) continue;
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
        ClosePromotionMenu();
        Vector2 tl = new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y));
        Vector2 br = new(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y));
        var box = new Rect2(tl, br - tl);

        ref readonly GameState st = ref Game.State;
        for (int i = 0; i < st.Units.Count; i++)
        {
            Unit u = st.Units[i];
            if (!u.IsAlive) continue;
            if (u.Owner != Game.ActivePlayer) continue;
            if (!FogOfWar.IsVisible(st, Game.ActivePlayer, u.TileX, u.TileY)) continue;
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
        if (!FogOfWar.IsKnown(st, Game.ActivePlayer, tx, ty)) return;

        // Stable iteration order: ascending Id. Required for replay
        // determinism if multiple commands are issued in the same frame.
        var ids = new List<int>(Game.SelectedUnitIds);
        ids.Sort();
        var reservedDestinations = new HashSet<int>();
        var previewPath = new List<int>();
        var previewDestinations = new List<int>();

        foreach (int id in ids)
        {
            if ((uint)id >= (uint)st.Units.Count) continue;
            Unit u = st.Units[id];
            if (!u.IsAlive || u.Owner != Game.ActivePlayer) continue;

            if (!TryFindMoveDestination(st, u, tx, ty, reservedDestinations,
                    out int dx, out int dy, out List<int> path))
                continue;

            int destFlat = dy * st.Map.Width + dx;
            reservedDestinations.Add(destFlat);
            previewDestinations.Add(destFlat);
            previewPath.AddRange(path);

            if (u.TileX == dx && u.TileY == dy) continue;
            Game.EnqueueCommand(new MoveUnitCommand(id, dx, dy)
            {
                PlayerId = (int)Game.ActivePlayer
            });
        }

        if (previewDestinations.Count > 0)
            Game.SetMovePreview(previewPath, previewDestinations);
    }

    private bool TryFindMoveDestination(
        in GameState st,
        Unit u,
        int desiredX,
        int desiredY,
        HashSet<int> reservedDestinations,
        out int targetX,
        out int targetY,
        out List<int> previewPath)
    {
        targetX = targetY = -1;
        previewPath = new List<int>();
        bool isHeavy = u.Type == UnitType.Heavy;

        const int maxRadius = 6;
        for (int radius = 0; radius <= maxRadius; radius++)
        {
            int bestFlat = int.MaxValue;
            int bestCost = int.MaxValue;
            List<int>? bestPath = null;
            int bestX = -1, bestY = -1;

            for (int oy = -radius; oy <= radius; oy++)
            {
                for (int ox = -radius; ox <= radius; ox++)
                {
                    if (Manhattan(0, 0, ox, oy) != radius) continue;
                    int cx = desiredX + ox, cy = desiredY + oy;
                    if (!st.Map.InBounds(cx, cy)) continue;
                    int flat = cy * st.Map.Width + cx;
                    if (reservedDestinations.Contains(flat)) continue;
                    if (!FogOfWar.IsKnown(st, Game.ActivePlayer, cx, cy)) continue;

                    TileType tile = st.Map.GetTileUnchecked(cx, cy);
                    if (!tile.IsPassable(isHeavy)) continue;
                    if (IsTileOccupiedByOtherUnit(st, u.Id, cx, cy)) continue;

                    List<int> path = FindCommandPreviewPath(st, u, cx, cy);
                    if (path.Count == 0 && (u.TileX != cx || u.TileY != cy)) continue;
                    int cost = path.Count;
                    if (cost > bestCost) continue;
                    if (cost == bestCost && flat >= bestFlat) continue;

                    bestFlat = flat;
                    bestCost = cost;
                    bestPath = path;
                    bestX = cx;
                    bestY = cy;
                }
            }

            if (bestPath is not null)
            {
                targetX = bestX;
                targetY = bestY;
                previewPath = bestPath;
                return true;
            }
        }

        return false;
    }

    private static List<int> FindCommandPreviewPath(in GameState st, Unit u, int targetX, int targetY)
    {
        bool isHeavy = u.Type == UnitType.Heavy;
        bool wasMoving = u.Path is { Count: > 0 };
        int firstStep = wasMoving ? u.Path[0] : -1;
        int startX = wasMoving ? firstStep % st.Map.Width : u.TileX;
        int startY = wasMoving ? firstStep / st.Map.Width : u.TileY;

        List<int> path = Pathfinding.FindPath(st.Map, startX, startY, targetX, targetY, isHeavy);
        if (wasMoving)
            path.Insert(0, firstStep);
        return path;
    }

    // ---- Production menu --------------------------------------------------
    private bool TryOpenProductionMenuAt(Vector2 screenPos)
    {
        var (tx, ty) = MapRenderer.ScreenToTile(Game.ScreenToMap(screenPos), Vector2.Zero);
        ref readonly GameState st = ref Game.State;
        if (!st.Map.InBounds(tx, ty)) return false;
        if (!FogOfWar.IsVisible(st, Game.ActivePlayer, tx, ty)) return false;
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
        ClosePromotionMenu();
        RenameMode = false;
        MenuCityId = cityId;
        if ((uint)cityId < (uint)Game.State.Cities.Count)
            MenuSelectedBuildType = SelectedBuildTypeFor(Game.State.Cities[cityId]);
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
        if (!FogOfWar.IsVisible(Game.State, Game.ActivePlayer, menuCity.TileX, menuCity.TileY))
        {
            CloseMenu();
            return;
        }
        if (Game.State.Map.GetTileUnchecked(menuCity.TileX, menuCity.TileY).IsFortTile())
        {
            CloseMenu();
            return;
        }
        // Recompute rects using the current menu position (keep anchor).
        if (MenuVisible && MenuCityId >= 0)
        {
            City c = Game.State.Cities[MenuCityId];
            if (c.IsProducing)
                MenuSelectedBuildType = (UnitType)(c.ProductionOrder - 1);
            else if (c.AutoBuildOrder != 0)
                MenuSelectedBuildType = (UnitType)(c.AutoBuildOrder - 1);
            Vector2 mapPos = MapRenderer.TileCenter(c.TileX, c.TileY, Vector2.Zero);
            ComputeMenuRects(Game.MapToScreen(mapPos));
        }
    }

    private void ComputeMenuRects(Vector2 rawPos)
    {
        bool valid = (uint)MenuCityId < (uint)Game.State.Cities.Count;
        bool producing = valid && Game.State.Cities[MenuCityId].IsProducing;
        bool upgrading = valid && Game.State.Cities[MenuCityId].IsUpgrading;

        const float w = 286f;
        const float bw = 262f, bh = 32f;
        const float pad = 12f;

        float renameExtra = RenameMode ? 36f : 0f;
        float h = 292f + renameExtra;

        // Clamp to viewport so the menu stays fully visible.
        Vector2 vp = Game.GetViewportRect().Size;
        float x = Mathf.Clamp(rawPos.X, 4f, vp.X - w - 4f);
        float y = Mathf.Clamp(rawPos.Y, 4f, vp.Y - h - 4f);
        Vector2 pos = new(x, y);

        MenuRect = new Rect2(pos, new Vector2(w, h));

        // Close "x" button — always top-right.
        MenuCancelButtonRect = new Rect2(pos + new Vector2(w - 28, 4), new Vector2(24, 24));
        MenuEditNameButtonRect = new Rect2(pos + new Vector2(w - 56, 4), new Vector2(24, 24));
        MenuNameInputRect = new Rect2(pos + new Vector2(pad, 34), new Vector2(w - 24, 26));
        float contentY = RenameMode ? 36f : 0f;

        MenuLightButtonRect = HiddenRect();
        MenuHeavyButtonRect = HiddenRect();
        MenuAutoBuildToggleRect = HiddenRect();
        MenuUpgradeButtonRect = HiddenRect();
        MenuCancelUpgradeButtonRect = HiddenRect();

        if (upgrading)
        {
            MenuCancelUpgradeButtonRect = new Rect2(pos + new Vector2(pad, 150 + contentY), new Vector2(bw, bh));
            MenuAutoBuildToggleRect = new Rect2(pos + new Vector2(pad, 192 + contentY), new Vector2(bw, bh));
        }
        else if (producing)
        {
            // Single "Cancel order" button, placed below the progress bar.
            MenuLightButtonRect = new Rect2(pos + new Vector2(pad, 150 + contentY), new Vector2(bw, bh));
            MenuAutoBuildToggleRect = new Rect2(pos + new Vector2(pad, 192 + contentY), new Vector2(bw, bh));
        }
        else
        {
            // Two build buttons stacked vertically.
            MenuLightButtonRect = new Rect2(pos + new Vector2(pad, 116 + contentY), new Vector2(bw, bh));
            MenuHeavyButtonRect = new Rect2(pos + new Vector2(pad, 154 + contentY), new Vector2(bw, bh));
            MenuAutoBuildToggleRect = new Rect2(pos + new Vector2(pad, 192 + contentY), new Vector2(bw, bh));
            MenuUpgradeButtonRect = new Rect2(pos + new Vector2(pad, 234 + contentY), new Vector2(bw, bh));
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
        if (!RenameMode && MenuEditNameButtonRect.HasPoint(screenPos))
        {
            BeginRenameCity();
            return true;
        }
        if (RenameMode)
        {
            if (!MenuRect.HasPoint(screenPos))
            {
                CloseMenu();
                return false;
            }
            return true;
        }

        // The main buttons swap meaning by city state:
        // idle = build/unit/upgrade controls, producing = cancel order,
        // upgrading = cancel upgrade.
        bool valid = (uint)MenuCityId < (uint)Game.State.Cities.Count;
        bool producing = valid && Game.State.Cities[MenuCityId].IsProducing;
        bool upgrading = valid && Game.State.Cities[MenuCityId].IsUpgrading;

        if (MenuAutoBuildToggleRect.HasPoint(screenPos))
        {
            ToggleAutoBuild(MenuSelectedBuildType);
            return true;
        }

        if (MenuCancelUpgradeButtonRect.HasPoint(screenPos))
        {
            if (upgrading)
            {
                Game.EnqueueCommand(new CancelCityUpgradeCommand(MenuCityId)
                { PlayerId = (int)Game.ActivePlayer });
            }
            return true;
        }

        if (MenuUpgradeButtonRect.HasPoint(screenPos))
        {
            if (valid && !producing && !upgrading)
            {
                City c = Game.State.Cities[MenuCityId];
                byte level = UnitStats.NormalizeDevelopmentLevel(c.DevelopmentLevel);
                if (UnitStats.UpgradeCost(level) > 0)
                {
                    Game.EnqueueCommand(new UpgradeCityCommand(MenuCityId)
                    { PlayerId = (int)Game.ActivePlayer });
                }
            }
            return true;
        }

        if (MenuLightButtonRect.HasPoint(screenPos))
        {
            if (producing)
            {
                Game.EnqueueCommand(new BuildUnitCommand(MenuCityId, null)
                { PlayerId = (int)Game.ActivePlayer });
                Game.EnqueueCommand(new SetAutoBuildCommand(MenuCityId, null)
                { PlayerId = (int)Game.ActivePlayer });
            }
            else if (!upgrading)
            {
                MenuSelectedBuildType = UnitType.Light;
                Game.EnqueueCommand(new BuildUnitCommand(MenuCityId, UnitType.Light)
                { PlayerId = (int)Game.ActivePlayer });
            }
            return true;
        }
        if (MenuHeavyButtonRect.HasPoint(screenPos))
        {
            if (!producing && !upgrading)
            {
                MenuSelectedBuildType = UnitType.Heavy;
                Game.EnqueueCommand(new BuildUnitCommand(MenuCityId, UnitType.Heavy)
                { PlayerId = (int)Game.ActivePlayer });
            }
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

    private void ToggleAutoBuild(UnitType type)
    {
        if ((uint)MenuCityId >= (uint)Game.State.Cities.Count) return;
        City c = Game.State.Cities[MenuCityId];
        byte current = c.AutoBuildOrder;
        byte want = (byte)((byte)type + 1);
        UnitType? next = current == want ? null : type;
        Game.EnqueueCommand(new SetAutoBuildCommand(MenuCityId, next)
        { PlayerId = (int)Game.ActivePlayer });
    }

    private static UnitType SelectedBuildTypeFor(City c)
    {
        if (c.ProductionOrder != 0) return (UnitType)(c.ProductionOrder - 1);
        if (c.AutoBuildOrder != 0) return (UnitType)(c.AutoBuildOrder - 1);
        return UnitType.Light;
    }

    private static Rect2 HiddenRect() => new(new Vector2(-9999, -9999), Vector2.Zero);

    public void CloseMenu()
    {
        MenuVisible = false;
        MenuCityId = -1;
        RenameMode = false;
        RenameDraft = "";
    }

    private void BeginRenameCity()
    {
        if ((uint)MenuCityId >= (uint)Game.State.Cities.Count) return;
        City c = Game.State.Cities[MenuCityId];
        RenameDraft = c.Name ?? "";
        RenameMode = true;
        ComputeMenuRects(Game.MapToScreen(MapRenderer.TileCenter(c.TileX, c.TileY, Vector2.Zero)));
    }

    private void CommitRenameCity()
    {
        if (!MenuVisible || !RenameMode) return;
        if ((uint)MenuCityId >= (uint)Game.State.Cities.Count) return;
        Game.EnqueueCommand(new RenameCityCommand(MenuCityId, RenameDraft)
        { PlayerId = (int)Game.ActivePlayer });
        RenameMode = false;
        RenameDraft = "";
    }

    // ---- City hover -------------------------------------------------------
    private void UpdateHoveredCity()
    {
        HoveredCityId = -1;
        var (tx, ty) = MapRenderer.ScreenToTile(Game.ScreenToMap(_mousePos), Vector2.Zero);
        ref readonly GameState st = ref Game.State;
        if (!st.Map.InBounds(tx, ty)) return;
        if (!FogOfWar.IsVisible(st, Game.ActivePlayer, tx, ty)) return;
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
        if (RenameMode)
        {
            HandleRenameKey(ek);
            return;
        }

        switch (ek.Keycode)
        {
            case Key.Tab:
                if (!Game.CanSwitchActivePlayer) break;
                ExitRoadBuildMode();
                Game.SelectedUnitIds.Clear();
                CloseMenu();
                ClosePromotionMenu();
                Game.SwitchActivePlayer();
                break;
            case Key.Escape:
                if (RoadBuildMode)
                {
                    ExitRoadBuildMode();
                    break;
                }
                if (PromotionMenuVisible)
                {
                    ClosePromotionMenu();
                    break;
                }
                Game.SelectedUnitIds.Clear();
                CloseMenu();
                break;
            case Key.F11:
                ToggleFullscreen();
                break;

            // Production shortcuts — active when a production menu is open
            // and the city is not upgrading.
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
            case Key.P:
                TogglePromotionMenu();
                break;
        }
    }

    private void HandleRenameKey(InputEventKey ek)
    {
        switch (ek.Keycode)
        {
            case Key.Enter:
            case Key.KpEnter:
                CommitRenameCity();
                return;
            case Key.Escape:
                RenameMode = false;
                RenameDraft = "";
                return;
            case Key.Backspace:
                if (RenameDraft.Length > 0)
                    RenameDraft = RenameDraft[..^1];
                return;
            case Key.Delete:
                RenameDraft = "";
                return;
        }

        if (ek.Unicode >= 32 && ek.Unicode <= 126 && RenameDraft.Length < 24)
            RenameDraft += (char)ek.Unicode;
    }

    // ---- Promotion menu --------------------------------------------------
    private void TogglePromotionMenu()
    {
        if (PromotionMenuVisible)
        {
            ClosePromotionMenu();
            return;
        }

        int unitId = SingleSelectedPromotableUnitId();
        if (unitId < 0) return;
        CloseMenu();
        ExitRoadBuildMode();
        PromotionUnitId = unitId;
        PromotionMenuVisible = true;
        RefreshPromotionMenuRects();
    }

    private int SingleSelectedPromotableUnitId()
    {
        if (Game.SelectedUnitIds.Count != 1) return -1;
        ref readonly GameState st = ref Game.State;
        foreach (int id in Game.SelectedUnitIds)
        {
            if ((uint)id >= (uint)st.Units.Count) return -1;
            Unit u = st.Units[id];
            if (!u.IsAlive || u.Owner != Game.ActivePlayer) return -1;
            if (u.PromotionPoints == 0) return -1;
            if (!FogOfWar.IsVisible(st, Game.ActivePlayer, u.TileX, u.TileY)) return -1;
            return id;
        }
        return -1;
    }

    private void RefreshPromotionMenuRects()
    {
        if (!PromotionMenuVisible) return;
        ref readonly GameState st = ref Game.State;
        if ((uint)PromotionUnitId >= (uint)st.Units.Count)
        {
            ClosePromotionMenu();
            return;
        }

        Unit u = st.Units[PromotionUnitId];
        if (!u.IsAlive || u.Owner != Game.ActivePlayer || u.PromotionPoints == 0
            || !FogOfWar.IsVisible(st, Game.ActivePlayer, u.TileX, u.TileY))
        {
            ClosePromotionMenu();
            return;
        }

        Vector2 mapPos = MapRenderer.UnitVisualCenter(u, st.Map, Vector2.Zero);
        ComputePromotionRects(Game.MapToScreen(mapPos));
    }

    private void ComputePromotionRects(Vector2 rawPos)
    {
        const float w = 260f;
        const float h = 252f;
        const float pad = 12f;
        const float bh = 28f;
        Vector2 vp = Game.GetViewportRect().Size;
        float x = Mathf.Clamp(rawPos.X + 18f, 4f, vp.X - w - 4f);
        float y = Mathf.Clamp(rawPos.Y - h * 0.5f, 52f, vp.Y - h - 44f);
        Vector2 pos = new(x, y);
        PromotionMenuRect = new Rect2(pos, new Vector2(w, h));
        for (int i = 0; i < PromotionPerkButtonRects.Length; i++)
            PromotionPerkButtonRects[i] = new Rect2(pos + new Vector2(pad, 38 + i * 34), new Vector2(w - pad * 2, bh));
    }

    private bool TryHitPromotionMenu(Vector2 screenPos)
    {
        if (!PromotionMenuVisible) return false;
        if ((uint)PromotionUnitId >= (uint)Game.State.Units.Count)
        {
            ClosePromotionMenu();
            return false;
        }

        Unit u = Game.State.Units[PromotionUnitId];
        byte[] perks = u.Type == UnitType.Heavy ? HeavyPromotionPerks : LightPromotionPerks;
        for (int i = 0; i < PromotionPerkButtonRects.Length; i++)
        {
            if (!PromotionPerkButtonRects[i].HasPoint(screenPos)) continue;
            Game.EnqueueCommand(new ChoosePromotionCommand(PromotionUnitId, perks[i])
            { PlayerId = (int)Game.ActivePlayer });
            ClosePromotionMenu();
            return true;
        }

        if (!PromotionMenuRect.HasPoint(screenPos))
        {
            ClosePromotionMenu();
            return false;
        }
        return true;
    }

    public void ClosePromotionMenu()
    {
        PromotionMenuVisible = false;
        PromotionUnitId = -1;
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
        ClosePromotionMenu();
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
            if (TerrainRules.IsBroadWater(st.Map, u.TileX, u.TileY)) continue;
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

        if (TryFindRoadBuildTarget(st, u, tx, ty, out _, out _, out List<int> path))
        {
            Game.SetRoadPreview(path, true);
        }
        else
        {
            var invalid = new List<int>();
            if (FogOfWar.IsKnown(st, Game.ActivePlayer, tx, ty))
                invalid.Add(ty * st.Map.Width + tx);
            Game.SetRoadPreview(invalid, false);
        }
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
        if (!TryFindRoadBuildTarget(st, u, tx, ty, out int targetX, out int targetY, out _))
            return;

        Game.EnqueueCommand(new BuildRoadCommand(unitId, targetX, targetY)
        { PlayerId = (int)Game.ActivePlayer });
        ExitRoadBuildMode();
    }

    private bool TryFindRoadBuildTarget(
        in GameState st,
        Unit u,
        int desiredX,
        int desiredY,
        out int targetX,
        out int targetY,
        out List<int> path)
    {
        targetX = targetY = -1;
        path = new List<int>();

        const int maxRadius = 5;
        for (int radius = 0; radius <= maxRadius; radius++)
        {
            int bestFlat = int.MaxValue;
            int bestCost = int.MaxValue;
            int bestX = -1, bestY = -1;
            List<int>? bestPath = null;

            for (int oy = -radius; oy <= radius; oy++)
            {
                for (int ox = -radius; ox <= radius; ox++)
                {
                    if (Manhattan(0, 0, ox, oy) != radius) continue;
                    int cx = desiredX + ox, cy = desiredY + oy;
                    if (!st.Map.InBounds(cx, cy)) continue;
                    if (!FogOfWar.IsKnown(st, Game.ActivePlayer, cx, cy)) continue;

                    List<int> candidatePath = Pathfinding.FindRoadBuildPath(st.Map, u.TileX, u.TileY, cx, cy);
                    if (candidatePath.Count == 0) continue;
                    if (!RoadPathFullyKnown(st, candidatePath)) continue;
                    if (RoadPathHasVisibleBlocker(st, u.Id, candidatePath)) continue;

                    int flat = cy * st.Map.Width + cx;
                    int cost = candidatePath.Count;
                    if (cost > bestCost) continue;
                    if (cost == bestCost && flat >= bestFlat) continue;

                    bestFlat = flat;
                    bestCost = cost;
                    bestX = cx;
                    bestY = cy;
                    bestPath = candidatePath;
                }
            }

            if (bestPath is not null)
            {
                targetX = bestX;
                targetY = bestY;
                path = bestPath;
                return true;
            }
        }

        return false;
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
        if (!FogOfWar.IsVisible(st, Game.ActivePlayer, tx, ty)) return;
        if (Game.SelectedUnitIds.Count > 0 && !HasSelectedLandBuilder(st)) return;

        Game.EnqueueCommand(new BuildFortCommand(tx, ty)
        { PlayerId = (int)Game.ActivePlayer });
    }

    private bool HasSelectedLandBuilder(in GameState st)
    {
        foreach (int id in Game.SelectedUnitIds)
        {
            if ((uint)id >= (uint)st.Units.Count) continue;
            Unit u = st.Units[id];
            if (!u.IsAlive || u.Owner != Game.ActivePlayer) continue;
            if (!TerrainRules.IsBroadWater(st.Map, u.TileX, u.TileY)) return true;
        }
        return false;
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
        if (!FogOfWar.IsVisible(st, Game.ActivePlayer, tx, ty)) return;

        Game.EnqueueCommand(new RazeFortCommand(tx, ty)
        { PlayerId = (int)Game.ActivePlayer });
    }

    private void TryKeyboardBuild(UnitType type)
    {
        if (!MenuVisible) return;
        if ((uint)MenuCityId >= (uint)Game.State.Cities.Count) return;

        City c = Game.State.Cities[MenuCityId];
        if (!FogOfWar.IsVisible(Game.State, Game.ActivePlayer, c.TileX, c.TileY))
        {
            CloseMenu();
            return;
        }
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
                Game.EnqueueCommand(new SetAutoBuildCommand(MenuCityId, null)
                { PlayerId = (int)Game.ActivePlayer });
            }
            return;
        }
        if (c.IsUpgrading) return;

        MenuSelectedBuildType = type;
        Game.EnqueueCommand(new BuildUnitCommand(MenuCityId, type)
        { PlayerId = (int)Game.ActivePlayer });
    }

    private bool RoadPathFullyKnown(in GameState st, List<int> path)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int flat = path[i];
            int x = flat % st.Map.Width, y = flat / st.Map.Width;
            if (!FogOfWar.IsKnown(st, Game.ActivePlayer, x, y)) return false;
        }
        return true;
    }

    private bool RoadPathHasVisibleBlocker(in GameState st, int builderId, List<int> path)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int flat = path[i];
            int x = flat % st.Map.Width, y = flat / st.Map.Width;
            if (!FogOfWar.IsVisible(st, Game.ActivePlayer, x, y)) continue;
            if (IsTileOccupiedByOtherUnit(st, builderId, x, y)) return true;
        }
        return false;
    }

    private static bool IsTileOccupiedByOtherUnit(in GameState st, int selfId, int x, int y)
    {
        for (int i = 0; i < st.Units.Count; i++)
        {
            if (i == selfId) continue;
            Unit other = st.Units[i];
            if (!other.IsAlive) continue;
            if (other.TileX == x && other.TileY == y) return true;
        }
        return false;
    }

    private static int Manhattan(int ax, int ay, int bx, int by)
        => System.Math.Abs(ax - bx) + System.Math.Abs(ay - by);

    private static void ToggleFullscreen()
    {
        DisplayServer.WindowMode mode = DisplayServer.WindowGetMode();
        DisplayServer.WindowSetMode(
            mode == DisplayServer.WindowMode.Fullscreen
                ? DisplayServer.WindowMode.Windowed
                : DisplayServer.WindowMode.Fullscreen);
    }
}
