using System.Collections.Generic;
using WarGame.Sim;
using WarGame.Sim.Commands;
using WarGame.Sim.State;
using Xunit;

namespace WarGame.Sim.Tests;

public class NoStackingTests
{
    private static GameState BuildLane(int len = 10)
    {
        var s = GameState.Initial(seed: 1);
        s.Map = new MapState.Builder(len, 1).Build();
        s.TileOwner = new byte[len];
        s.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
        return s;
    }

    [Fact]
    public void FriendlyUnitCanPassThroughFriendly()
    {
        // Two friendly units on a 1-wide lane. Unit 0 is parked at (5,0).
        // Unit 1 at (3,0) is ordered to (7,0). With friendly pass-through
        // enabled, unit 1 should be able to walk through unit 0.
        var s = BuildLane(10);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player1, UnitType.Light, 3, 0));

        s = GameSim.Step(s, new List<Command> {
            new MoveUnitCommand(1, 7, 0) { PlayerId = (int)PlayerId.Player1 }
        });

        // Plenty of time to walk 4 tiles uncontested.
        s = GameSim.StepN(s, 200);
        Assert.Equal(5, s.Units[0].TileX);  // blocker didn't move
        Assert.Equal(7, s.Units[1].TileX);  // unit 1 passed through and arrived
    }

    [Fact]
    public void UnitResumesAfterBlockerMoves()
    {
        // Unit 0 is in the way; ordering it forward frees the path for
        // unit 1, which should then proceed and eventually arrive.
        var s = BuildLane(20);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player1, UnitType.Light, 3, 0));

        s = GameSim.Step(s, new List<Command> {
            new MoveUnitCommand(1, 15, 0) { PlayerId = (int)PlayerId.Player1 },
            new MoveUnitCommand(0, 19, 0) { PlayerId = (int)PlayerId.Player1 },
        });

        // Light at 4 tiles/sec → 16 tiles in 4 sec → 120 ticks. Add slack
        // for waiting behind the blocker briefly.
        s = GameSim.StepN(s, 240);
        Assert.Equal(15, s.Units[1].TileX);
    }

    [Fact]
    public void EnemyUnitsCannotShareATile()
    {
        // Two opposing units approach the same tile from opposite sides.
        // The enemy should block movement (no stacking with enemies).
        var s = BuildLane(20);
        // Add a city for P2 so the game doesn't end.
        s.Cities.Add(City.Create(1, 19, 0, PlayerId.Player2, isCapital: true));
        s.Map = new MapState.Builder(20, 1).Build();
        s.TileOwner = new byte[20];
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 19, 0));

        var cmds = new List<Command>
        {
            new MoveUnitCommand(0, 10, 0) { PlayerId = (int)PlayerId.Player1 },
            new MoveUnitCommand(1, 10, 0) { PlayerId = (int)PlayerId.Player2 },
        };
        s = GameSim.Step(s, cmds);

        // Run until combat resolves.
        for (int t = 0; t < 300; t++)
        {
            s = GameSim.Step(s, null);
            // At no point should both living enemy units share a tile.
            if (s.Units[0].IsAlive && s.Units[1].IsAlive)
            {
                Assert.False(
                    s.Units[0].TileX == s.Units[1].TileX
                    && s.Units[0].TileY == s.Units[1].TileY,
                    $"tick {t}: enemy units 0 and 1 both at ({s.Units[0].TileX},{s.Units[0].TileY})");
            }
        }
    }

    [Fact]
    public void Production_StallsWhenCityTileIsOccupied()
    {
        // City tile is occupied by an existing unit. Issuing Build Light
        // should stall the spawn — the order accumulates progress to the
        // cost cap, but no new unit appears until the blocker leaves.
        var s = BuildLane(10);
        // Park a unit on the capital tile (0, 0) — the city's own tile.
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0));

        s = GameSim.Step(s, new List<Command> {
            new BuildUnitCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player1 },
        });

        // Plenty of ECO to finish the build (capital produces 3/sec).
        s = GameSim.StepN(s, 200);
        // Still only the original unit — no spawn while tile blocked.
        Assert.Single(s.Units);

        // Move the blocker off the tile; spawn should land soon after.
        s = GameSim.Step(s, new List<Command> {
            new MoveUnitCommand(0, 5, 0) { PlayerId = (int)PlayerId.Player1 },
        });
        s = GameSim.StepN(s, 80);
        Assert.True(s.Units.Count >= 2,
            $"expected a spawn after blocker moved; got {s.Units.Count}");
    }
}
