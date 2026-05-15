namespace WarGame;

using Godot;
using WarGame.Sim.AI;
using Theme = WarGame.Render.Theme;

public partial class MatchSetup : Node2D
{
    private Font _fontPrimary = null!;
    private Font _fontSemiBold = null!;
    private Vector2 _viewportSize;

    private MatchMode _mode = MatchMode.HumanVsAi;
    private AiDifficulty _difficulty = AiDifficulty.Medium;

    private Rect2 _humanVsHumanRect;
    private Rect2 _humanVsAiRect;
    private Rect2 _easyRect;
    private Rect2 _mediumRect;
    private Rect2 _hardRect;
    private Rect2 _startRect;

    public override void _Ready()
    {
        _fontPrimary = Theme.BuildPrimaryFont();
        _fontSemiBold = Theme.BuildSemiBoldFont();
        SetProcessInput(true);
        GetViewport().SizeChanged += RecalculateLayout;
        RecalculateLayout();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, _viewportSize), Theme.BgVoid);

        float panelW = 560f;
        float panelH = 390f;
        Rect2 panel = new(
            new Vector2((_viewportSize.X - panelW) * 0.5f, (_viewportSize.Y - panelH) * 0.5f),
            new Vector2(panelW, panelH));
        DrawRect(panel, Theme.MenuBg);
        DrawRect(panel, Theme.MenuBorder, filled: false, width: 1.5f);

        DrawString(_fontSemiBold, panel.Position + new Vector2(24, 56),
            "WarGame", HorizontalAlignment.Left, -1, 34, Theme.HudText);
        DrawString(_fontPrimary, panel.Position + new Vector2(24, 88),
            "Match setup", HorizontalAlignment.Left, -1, 15, Theme.HudTextDim);

        DrawString(_fontSemiBold, panel.Position + new Vector2(24, 132),
            "Mode", HorizontalAlignment.Left, -1, 15, Theme.HudText);
        DrawChoice(_humanVsHumanRect, "Human vs Human", _mode == MatchMode.HumanVsHuman);
        DrawChoice(_humanVsAiRect, "Human vs AI", _mode == MatchMode.HumanVsAi);

        DrawString(_fontSemiBold, panel.Position + new Vector2(24, 224),
            "AI Difficulty", HorizontalAlignment.Left, -1, 15,
            _mode == MatchMode.HumanVsAi ? Theme.HudText : Theme.HudTextDim);
        DrawChoice(_easyRect, "Easy", _difficulty == AiDifficulty.Easy, _mode == MatchMode.HumanVsAi);
        DrawChoice(_mediumRect, "Medium", _difficulty == AiDifficulty.Medium, _mode == MatchMode.HumanVsAi);
        DrawChoice(_hardRect, "Hard", _difficulty == AiDifficulty.Hard, _mode == MatchMode.HumanVsAi);

        Color startBg = Theme.SelectRing;
        startBg.A = 0.18f;
        DrawRect(_startRect, startBg);
        DrawRect(_startRect, Theme.SelectRing, filled: false, width: 1.5f);
        DrawString(_fontSemiBold, _startRect.Position + new Vector2(0, 25),
            "Start Match", HorizontalAlignment.Center, (int)_startRect.Size.X, 15, Theme.HudText);

        DrawString(_fontPrimary, panel.Position + new Vector2(24, panelH - 20),
            "Enter = start   Esc = Human vs Human", HorizontalAlignment.Left, -1, 12, Theme.HudTextDim);
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb:
                HandleClick(mb.Position);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key);
                break;
        }
    }

    private void RecalculateLayout()
    {
        _viewportSize = GetViewportRect().Size;
        float panelW = 560f;
        float panelH = 390f;
        Vector2 panel = new((_viewportSize.X - panelW) * 0.5f, (_viewportSize.Y - panelH) * 0.5f);

        _humanVsHumanRect = new Rect2(panel + new Vector2(24, 146), new Vector2(244, 44));
        _humanVsAiRect = new Rect2(panel + new Vector2(292, 146), new Vector2(244, 44));
        _easyRect = new Rect2(panel + new Vector2(24, 238), new Vector2(156, 42));
        _mediumRect = new Rect2(panel + new Vector2(202, 238), new Vector2(156, 42));
        _hardRect = new Rect2(panel + new Vector2(380, 238), new Vector2(156, 42));
        _startRect = new Rect2(panel + new Vector2(24, 316), new Vector2(512, 44));
        QueueRedraw();
    }

    private void DrawChoice(Rect2 rect, string label, bool selected, bool enabled = true)
    {
        Color bg = Theme.HudPanel;
        bg.A = selected ? 0.78f : 0.42f;
        if (!enabled) bg.A = 0.22f;
        Color edge = selected ? Theme.SelectRing : Theme.HudPanelEdge;
        if (!enabled) edge = Theme.HudPanelEdge;
        Color text = enabled ? Theme.HudText : Theme.HudTextDim;

        DrawRect(rect, bg);
        DrawRect(rect, edge, filled: false, width: selected ? 1.8f : 1f);
        DrawString(selected ? _fontSemiBold : _fontPrimary, rect.Position + new Vector2(0, 27),
            label, HorizontalAlignment.Center, (int)rect.Size.X, 14, text);
    }

    private void HandleClick(Vector2 position)
    {
        if (_humanVsHumanRect.HasPoint(position))
        {
            _mode = MatchMode.HumanVsHuman;
            QueueRedraw();
            return;
        }

        if (_humanVsAiRect.HasPoint(position))
        {
            _mode = MatchMode.HumanVsAi;
            QueueRedraw();
            return;
        }

        if (_mode == MatchMode.HumanVsAi)
        {
            if (_easyRect.HasPoint(position)) _difficulty = AiDifficulty.Easy;
            else if (_mediumRect.HasPoint(position)) _difficulty = AiDifficulty.Medium;
            else if (_hardRect.HasPoint(position)) _difficulty = AiDifficulty.Hard;
        }

        if (_startRect.HasPoint(position))
            StartMatch();

        QueueRedraw();
    }

    private void HandleKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Enter:
            case Key.KpEnter:
                StartMatch();
                break;
            case Key.Escape:
                _mode = MatchMode.HumanVsHuman;
                QueueRedraw();
                break;
            case Key.H:
                _mode = MatchMode.HumanVsHuman;
                QueueRedraw();
                break;
            case Key.A:
                _mode = MatchMode.HumanVsAi;
                QueueRedraw();
                break;
            case Key.Key1:
                _difficulty = AiDifficulty.Easy;
                _mode = MatchMode.HumanVsAi;
                QueueRedraw();
                break;
            case Key.Key2:
                _difficulty = AiDifficulty.Medium;
                _mode = MatchMode.HumanVsAi;
                QueueRedraw();
                break;
            case Key.Key3:
                _difficulty = AiDifficulty.Hard;
                _mode = MatchMode.HumanVsAi;
                QueueRedraw();
                break;
        }
    }

    private void StartMatch()
    {
        MatchConfig.Mode = _mode;
        MatchConfig.AiDifficulty = _difficulty;
        GetTree().ChangeSceneToFile("res://src/Game.tscn");
    }
}
