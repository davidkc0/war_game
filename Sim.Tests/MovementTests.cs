using System.Collections.Generic;
using WarGame.Sim;
using WarGame.Sim.Commands;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class MovementTests
{
    // 30x1 strip on plains. A light unit moving 4 tiles/sec @ 30 Hz =
    // 0.133 tiles/tick. Walking 10 tiles takes ~75 ticks (2.5 sec). Test
    // gives a generous upper bound; the lower bound catches "unit teleports".
    private static GameState BuildStripWithLight(int strip, int startX, PlayerId owner = PlayerId.Player1)
    {
        var s = GameState.Initial(seed: 1);
        s.Map = new MapState.Builder(strip, 1).Build();
        // A city at the unit's starting tile keeps the encirclement system
        // happy — the unit has a supply source. (Phase 3a turns this into
        // proper supply lines; for Phase 1 a city under each test's units
        // is enough.)
        s.Cities.Add(City.Create(0, startX, 0, owner, isCapital: true));
        s.Units.Add(Unit.Create(0, owner, UnitType.Light, startX, 0));
        return s;
    }

    [Fact]
    public void NoCommands_NoUnitsMove()
    {
        var s = BuildStripWithLight(20, 5);
        s = GameSim.StepN(s, 100);
        Assert.Equal(5, s.Units[0].TileX);
        Assert.Equal(0, s.Units[0].ProgressRaw);
        Assert.Empty(s.Units[0].Path);
    }

    [Fact]
    public void MoveCommand_QueuesPath_AndUnitWalks()
    {
        var s = BuildStripWithLight(20, 0);
        var cmds = new List<Command> {
            new MoveUnitCommand(0, 10, 0) { PlayerId = (int)PlayerId.Player1 }
        };
        // First tick processes the command and starts the walk.
        s = GameSim.Step(s, cmds);
        Assert.NotEmpty(s.Units[0].Path);

        // 200 ticks is 6.66 seconds at 30 Hz; light at 4 tiles/sec covers
        // ~26 tiles. 10 tiles will be reached well within.
        s = GameSim.StepN(s, 200);
        Assert.Equal(10, s.Units[0].TileX);
        Assert.Empty(s.Units[0].Path);
        Assert.Equal(0, s.Units[0].ProgressRaw);
    }

    [Fact]
    public void Light_TakesAtLeastTilesOverSpeedTicks()
    {
        // Lower bound: a light unit cannot cover 10 tiles in fewer than
        // ceil(10 / TilesPerTick) ticks. With TilesPerTick = 4/30, that is
        // ceil(10 / 0.1333...) = ceil(75.0...) = 75 ticks. We assert at
        // least that many ticks are needed.
        var s = BuildStripWithLight(20, 0);
        var cmds = new List<Command> {
            new MoveUnitCommand(0, 10, 0) { PlayerId = (int)PlayerId.Player1 }
        };
        s = GameSim.Step(s, cmds);

        for (int t = 0; t < 70; t++)
            s = GameSim.Step(s, null);
        // After 71 ticks of walking (1 setup + 70 walk), a light unit should
        // not yet have covered 10 plains tiles.
        Assert.True(s.Units[0].TileX < 10,
            $"unit at {s.Units[0].TileX} after 71 ticks — moving too fast");
    }

    [Fact]
    public void Heavy_SlowerThanLight_OverSameDistance()
    {
        var sLight = BuildStripWithLight(30, 0);
        var sHeavy = GameState.Initial(seed: 1);
        sHeavy.Map = new MapState.Builder(30, 1).Build();
        sHeavy.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
        sHeavy.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Heavy, 0, 0));

        var moveLight = new List<Command> { new MoveUnitCommand(0, 20, 0) { PlayerId = (int)PlayerId.Player1 } };
        var moveHeavy = new List<Command> { new MoveUnitCommand(0, 20, 0) { PlayerId = (int)PlayerId.Player1 } };

        sLight = GameSim.Step(sLight, moveLight);
        sHeavy = GameSim.Step(sHeavy, moveHeavy);

        // 60 ticks of walking. Light at 4/sec covers ~8 tiles; heavy at
        // 1.5/sec covers ~3. Heavy must trail strictly behind light.
        for (int t = 0; t < 60; t++)
        {
            sLight = GameSim.Step(sLight, null);
            sHeavy = GameSim.Step(sHeavy, null);
        }
        Assert.True(sHeavy.Units[0].TileX < sLight.Units[0].TileX,
            $"heavy {sHeavy.Units[0].TileX} >= light {sLight.Units[0].TileX}");
    }

    [Fact]
    public void RoadIsFasterThanPlains()
    {
        // Two parallel strips: top is plains, bottom is road. Same start /
        // same goal x. The road unit must be at-or-ahead of the plains unit
        // at every tick.
        var s = GameState.Initial(seed: 7);
        var b = new MapState.Builder(20, 2);
        for (int x = 0; x < 20; x++) b.Set(x, 1, TileType.Road);
        s.Map = b.Build();
        s.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0)); // plains
        s.Units.Add(Unit.Create(1, PlayerId.Player1, UnitType.Light, 0, 1)); // road

        var cmds = new List<Command> {
            new MoveUnitCommand(0, 19, 0) { PlayerId = (int)PlayerId.Player1 },
            new MoveUnitCommand(1, 19, 1) { PlayerId = (int)PlayerId.Player1 },
        };
        s = GameSim.Step(s, cmds);

        bool roadEverAhead = false;
        for (int t = 0; t < 200; t++)
        {
            s = GameSim.Step(s, null);
            // Compute fractional position so we don't lose to integer-only
            // tile snapping at the start of every edge.
            FP plainsPos = ResolvePosition(s, 0);
            FP roadPos   = ResolvePosition(s, 1);
            Assert.True(roadPos >= plainsPos,
                $"tick {t}: road {roadPos} fell behind plains {plainsPos}");
            if (roadPos > plainsPos) roadEverAhead = true;
            if (s.Units[0].TileX == 19) break;
        }
        Assert.True(roadEverAhead, "road never gained on plains");
    }

    [Fact]
    public void UnitsCanPathThroughBroadWater()
    {
        var s = GameState.Initial(seed: 4);
        var b = new MapState.Builder(6, 1);
        b.Set(2, 0, TileType.Water);
        b.Set(3, 0, TileType.Water);
        s.Map = b.Build();
        s.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0));

        s = GameSim.Step(s, new List<Command> {
            new MoveUnitCommand(0, 5, 0) { PlayerId = (int)PlayerId.Player1 },
        });

        Assert.NotEmpty(s.Units[0].Path);
        s = GameSim.StepN(s, 220);
        Assert.Equal(5, s.Units[0].TileX);
        Assert.Empty(s.Units[0].Path);
    }

    [Fact]
    public void BroadWaterMovementIsSlowerThanPlains()
    {
        var plains = BuildStripWithLight(20, 0);

        var water = GameState.Initial(seed: 5);
        var b = new MapState.Builder(20, 1, TileType.Water);
        b.Set(0, 0, TileType.Capital);
        water.Map = b.Build();
        water.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
        water.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0));

        var cmd = new List<Command> {
            new MoveUnitCommand(0, 10, 0) { PlayerId = (int)PlayerId.Player1 },
        };
        plains = GameSim.Step(plains, cmd);
        water = GameSim.Step(water, cmd);

        for (int t = 0; t < 60; t++)
        {
            plains = GameSim.Step(plains, null);
            water = GameSim.Step(water, null);
        }

        Assert.True(ResolvePosition(water, 0) < ResolvePosition(plains, 0));
    }

    [Fact]
    public void Heavy_CannotCrossMountain_PathDropped()
    {
        var s = GameState.Initial(seed: 1);
        var b = new MapState.Builder(5, 1);
        b.Set(2, 0, TileType.Mountain);
        s.Map = b.Build();
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Heavy, 0, 0));

        var cmds = new List<Command> {
            new MoveUnitCommand(0, 4, 0) { PlayerId = (int)PlayerId.Player1 }
        };
        s = GameSim.Step(s, cmds);
        // No path should have been queued — heavy can't reach (4, 0).
        Assert.Empty(s.Units[0].Path);
        Assert.Equal(0, s.Units[0].TileX);
    }

    [Fact]
    public void MoveCommand_RejectedWhenIssuedByWrongPlayer()
    {
        var s = BuildStripWithLight(10, 0, owner: PlayerId.Player1);
        var cmds = new List<Command> {
            // Player 2 trying to move Player 1's unit.
            new MoveUnitCommand(0, 5, 0) { PlayerId = (int)PlayerId.Player2 }
        };
        s = GameSim.Step(s, cmds);
        Assert.Empty(s.Units[0].Path);
    }

    [Fact]
    public void Determinism_SameSeedSameCommands_SameFinalState()
    {
        // End-to-end: build two identical sims, issue identical command
        // streams, run for many ticks, assert their serializations are
        // byte-identical. This is the integration version of the unit-level
        // determinism tests.
        GameState Build()
        {
            var x = GameState.Initial(seed: 99);
            x.Map = new MapState.Builder(20, 20).Build();
            x.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
            x.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0));
            x.Units.Add(Unit.Create(1, PlayerId.Player1, UnitType.Heavy, 0, 1));
            return x;
        }

        var cmds = new List<Command> {
            new MoveUnitCommand(0, 19, 19) { PlayerId = (int)PlayerId.Player1 },
            new MoveUnitCommand(1, 10, 10) { PlayerId = (int)PlayerId.Player1 },
        };

        var a = Build();
        var b = Build();
        a = GameSim.Step(a, cmds);
        b = GameSim.Step(b, cmds);
        for (int i = 0; i < 500; i++)
        {
            a = GameSim.Step(a, null);
            b = GameSim.Step(b, null);
        }
        byte[] sa = StateSerializer.ToBytes(a);
        byte[] sb = StateSerializer.ToBytes(b);
        Assert.Equal(sa, sb);
    }

    // Position with sub-tile precision: TileX + ProgressRaw / OneRaw. Used
    // only inside tests; the sim itself never needs this representation.
    private static FP ResolvePosition(GameState s, int unitId)
    {
        Unit u = s.Units[unitId];
        FP whole = FP.FromInt(u.TileX);
        return whole + FP.FromRaw(u.ProgressRaw);
    }
}
