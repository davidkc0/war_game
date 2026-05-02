using System.Collections.Generic;
using WarGame.Sim;
using WarGame.Sim.Commands;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class WinConditionsTests
{
    private static GameState BuildScenario(int w = 30, int h = 1)
    {
        var s = GameState.Initial(seed: 1);
        s.Map = new MapState.Builder(w, h).Build();
        s.TileOwner = new byte[w * h];
        return s;
    }

    [Fact]
    public void Threshold_ExactlyAt80PercentTriggersHold()
    {
        Assert.True(WinConditions.MeetsCityThreshold(8, 10));
        Assert.True(WinConditions.MeetsCityThreshold(4, 5));
        Assert.False(WinConditions.MeetsCityThreshold(3, 5));
    }

    [Fact]
    public void GameStartsWithNoWinner()
    {
        var s = GameState.Initial(seed: 1);
        Assert.Equal(PlayerId.None, s.Winner);
    }

    [Fact]
    public void CapturingEnemyCapital_TriggersVictory()
    {
        var s = BuildScenario(w: 20, h: 1);
        // P2 capital, plus a stack of P1 heavies sitting on top of it.
        s.Cities.Add(City.Create(0, 5, 0, PlayerId.Player2, isCapital: true));
        for (int i = 0; i < 5; i++)
            s.Units.Add(Unit.Create(i, PlayerId.Player1, UnitType.Heavy, 5, 0));
        // P1 also needs a city or all their heavies die from encirclement
        // before the capture lands.
        s.Cities.Add(City.Create(1, 0, 0, PlayerId.Player1, isCapital: true));

        // Capital CaptureHp = 200. 5 heavies on tile = 15 damage/tick.
        // Flip at ~14 ticks. Give plenty of margin.
        s = GameSim.StepN(s, 30);
        Assert.Equal(PlayerId.Player1, s.Winner);
    }

    [Fact]
    public void OwnCapitalDoesNotCountAsCaptured()
    {
        // No capture, no owner change → no winner.
        var s = BuildScenario(w: 20, h: 1);
        s.Cities.Add(City.Create(0, 5,  0, PlayerId.Player1, isCapital: true));
        s.Cities.Add(City.Create(1, 15, 0, PlayerId.Player2, isCapital: true));
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 15, 0));

        s = GameSim.StepN(s, 30);
        Assert.Equal(PlayerId.None, s.Winner);
    }

    [Fact]
    public void HoldingAllCities_For30Sec_TriggersVictory()
    {
        // P1 owns both cities from start. Threshold (≥80%) met every tick.
        // 30 sec * 30 ticks = 900 ticks.
        var s = BuildScenario(w: 20, h: 1);
        s.Cities.Add(City.Create(0, 5,  0, PlayerId.Player1, isCapital: true));
        s.Cities.Add(City.Create(1, 15, 0, PlayerId.Player1, isCapital: false));
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 30 + 5);
        Assert.Equal(PlayerId.Player1, s.Winner);
    }

    [Fact]
    public void HoldingTimer_ResetsWhenOwnershipDrops()
    {
        // Hold for 29s, then lose a city, then hold again — timer must
        // restart, so victory does *not* fire at the original 30s mark.
        var s = BuildScenario(w: 20, h: 1);
        s.Cities.Add(City.Create(0, 5,  0, PlayerId.Player1, isCapital: true));
        s.Cities.Add(City.Create(1, 15, 0, PlayerId.Player1, isCapital: false));
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));

        // Run for 29 seconds — almost there.
        s = GameSim.StepN(s, GameSim.TicksPerSecond * 29);
        Assert.Equal(PlayerId.None, s.Winner);

        // Hand a city to P2 to drop P1 below threshold.
        var c = s.Cities[1];
        c.Owner = PlayerId.Player2;
        s.Cities[1] = c;
        // Now P1 owns 1/2 = 50% of cities, P2 owns 1/2 = 50%. Neither at
        // ≥80%. Timer should reset. Also clear TileOwner so PowerProjection
        // recomputes fresh.
        s.TileOwner = new byte[s.Map.Width * s.Map.Height];

        // Tick a bit; both timers should be at zero or near-zero by tick 5.
        s = GameSim.StepN(s, 5);
        Assert.Equal(0, s.CityHoldTicks[(int)PlayerId.Player1]);

        // No one should have won yet.
        s = GameSim.StepN(s, 30);
        Assert.Equal(PlayerId.None, s.Winner);
    }

    [Fact]
    public void AfterVictory_SimFreezes()
    {
        // Once Winner is set, ticks should not advance unit positions or
        // production. Verify by checking that a moving unit's path doesn't
        // advance after the win lands.
        var s = BuildScenario(w: 20, h: 1);
        s.Cities.Add(City.Create(0, 5, 0, PlayerId.Player1, isCapital: true));
        // Force-win by setting the flag manually.
        s.Winner = PlayerId.Player1;
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));

        var before = StateSerializer.ToBytes(s);
        // Issue a move command and tick — it should be ignored.
        var cmd = new List<Command> {
            new MoveUnitCommand(0, 10, 0) { PlayerId = (int)PlayerId.Player1 }
        };
        s = GameSim.Step(s, cmd);
        // Tick advanced, but everything else is frozen — the only diff
        // should be the Tick field. Check the unit didn't get a path queued.
        Assert.Empty(s.Units[0].Path);
        Assert.Equal(5, s.Units[0].TileX);
    }
}
