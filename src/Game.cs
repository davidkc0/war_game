namespace WarGame;

using Godot;
using WarGame.Sim;
using WarGame.Sim.State;

// Phase 0 render scene. Drives the deterministic sim at a fixed 30 Hz via an
// accumulator (see PLAN.md §1) and draws the single dot tracked in GameState.
//
// This file is the only place where sim state is *read* by Godot. The sim
// itself never touches Godot types — that boundary is the foundation of
// lockstep netcode and replay.
public partial class Game : Node2D
{
    private const ulong InitialSeed = 42;
    private const float SimStepSeconds = 1f / GameSim.TicksPerSecond;
    // World units are arbitrary in Phase 0; this scale just makes the dot
    // visible against a 720x720 viewport.
    private const float WorldToScreen = 60f;

    private GameState _state;
    private float _accumulator;
    private Vector2 _origin;

    public override void _Ready()
    {
        _state = GameState.Initial(InitialSeed);
        // Center the world origin in the viewport so we can see negative
        // coordinates if the dot ever wanders that way.
        var size = GetViewportRect().Size;
        _origin = size * 0.5f;
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
        Vector2 dot = new(
            _state.DotPos.X.ToFloatUnsafe() * WorldToScreen,
            _state.DotPos.Y.ToFloatUnsafe() * WorldToScreen);
        DrawCircle(_origin + dot, 12f, new Color(0.4f, 0.7f, 1f));
    }
}
