namespace WarGame;

using Godot;
using WarGame.Sim;
using WarGame.Sim.State;

// Placeholder render scene during Phase 1. The Phase 0 dot was removed when
// the schema bumped to v3 (movement state on Unit replaces the standalone
// debug dot). Step 9 introduces the real terrain + unit + border renderer
// along with the committed visual palette per PLAN.md §1.6.
//
// Until then, this scene proves the deterministic sim is ticking by:
//   - drawing a high-contrast banner that's hard to miss,
//   - drawing a small "Phase 0 indicator dot" that orbits the screen so
//     you can verify the tick loop is actually running (not just starting
//     once and freezing).
//
// Sim/render boundary is preserved: this file reads `_state` but never
// writes to it.
public partial class Game : Node2D
{
    private const ulong InitialSeed = 42;
    private const float SimStepSeconds = 1f / GameSim.TicksPerSecond;

    private GameState _state;
    private float _accumulator;
    private Vector2 _viewportSize;

    public override void _Ready()
    {
        _state = GameState.Initial(InitialSeed);
        _viewportSize = GetViewportRect().Size;
    }

    public override void _Process(double delta)
    {
        _accumulator += (float)delta;
        while (_accumulator >= SimStepSeconds)
        {
            _state = GameSim.Step(_state, null);
            _accumulator -= SimStepSeconds;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Solid background — proves _Draw is firing even if fonts are
        // misconfigured. Two-tone slab makes it obvious which half is
        // "alive."
        DrawRect(new Rect2(Vector2.Zero, _viewportSize),
            new Color(0.10f, 0.13f, 0.18f));
        DrawRect(new Rect2(0, 0, _viewportSize.X, 64),
            new Color(0.20f, 0.40f, 0.65f));

        // Heartbeat dot — orbits a 100px circle, one full revolution every
        // ~2 sec. If the sim is ticking, the dot is moving.
        float t = _state.Tick * (Mathf.Tau / (GameSim.TicksPerSecond * 2f));
        Vector2 center = _viewportSize * 0.5f;
        Vector2 dot = center + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * 100f;
        DrawCircle(dot, 16f, new Color(1f, 0.6f, 0.3f));
        DrawArc(center, 100f, 0, Mathf.Tau, 64,
            new Color(0.4f, 0.5f, 0.6f), 1.5f);

        // Status text. ThemeDB.FallbackFont is always non-null in Godot 4.6,
        // so the explicit fallback is safe.
        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(20, 44), "WarGame — Phase 1 sim",
            HorizontalAlignment.Left, -1, 28, new Color(1f, 1f, 1f));
        DrawString(font, new Vector2(20, _viewportSize.Y - 24),
            $"tick {_state.Tick}    units {_state.Units.Count}    cities {_state.Cities.Count}",
            HorizontalAlignment.Left, -1, 18, new Color(0.85f, 0.9f, 1f));
    }
}
