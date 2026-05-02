using System.Diagnostics;
using WarGame.Sim;
using WarGame.Sim.State;
using Xunit;
using Xunit.Abstractions;

namespace WarGame.Sim.Tests;

// Phase 1 acceptance bar (PLAN.md Phase 1):
//   "Game runs at 60 FPS with 200 units on screen."
// At 30 Hz sim, that means the sim must be able to do 1 tick in well under
// (1/60) sec on average — call it < 8 ms with margin for the renderer.
// We measure 1000 ticks with 200 units; total wall time / 1000 = avg.
//
// Test reports the number rather than failing on a hard threshold so we
// don't break CI on a noisy runner. A regression > 2x what we measure
// today should be investigated; we hard-fail at 16ms/tick (one full
// 60-Hz frame, which is the actual unplayability boundary).
public class PerformanceTests
{
    private readonly ITestOutputHelper _output;
    public PerformanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TwoHundredUnits_OneThousandTicks_IsFast()
    {
        var s = GameState.Initial(seed: 42);
        var b = new MapState.Builder(60, 60);
        s.Map = b.Build();
        s.TileOwner = new byte[60 * 60];

        // Two cities per player, evenly distributed so encirclement
        // doesn't kill everyone immediately.
        s.Cities.Add(City.Create(0, 5,  5,  PlayerId.Player1, isCapital: true));
        s.Cities.Add(City.Create(1, 15, 5,  PlayerId.Player1, isCapital: false));
        s.Cities.Add(City.Create(2, 45, 55, PlayerId.Player2, isCapital: true));
        s.Cities.Add(City.Create(3, 55, 55, PlayerId.Player2, isCapital: false));

        // 100 units per player. Spread across friendly territory so they
        // survive encirclement long enough to stress the systems.
        for (int i = 0; i < 100; i++)
        {
            int x = 5 + i % 10;
            int y = 5 + i / 10;
            s.Units.Add(Unit.Create(i, PlayerId.Player1,
                i % 2 == 0 ? UnitType.Light : UnitType.Heavy, x, y));
        }
        for (int i = 0; i < 100; i++)
        {
            int x = 45 + i % 10;
            int y = 50 + i / 10;
            s.Units.Add(Unit.Create(100 + i, PlayerId.Player2,
                i % 2 == 0 ? UnitType.Light : UnitType.Heavy, x, y));
        }

        // Warm-up: JIT + first allocations are not what we want to measure.
        for (int i = 0; i < 30; i++) s = GameSim.Step(s, null);

        const int ticks = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++) s = GameSim.Step(s, null);
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / ticks;
        _output.WriteLine($"avg tick: {avgMs:F3} ms ({ticks} ticks in {sw.ElapsedMilliseconds} ms, {s.Units.Count} units)");

        // Hard ceiling: any single tick averaging more than 16 ms means we
        // can't sustain 60 FPS render even at this unit count. CI runners
        // are slower than dev hardware, so 16 ms is generous.
        Assert.True(avgMs < 16.0, $"sim too slow: {avgMs:F2} ms/tick avg");
    }
}
