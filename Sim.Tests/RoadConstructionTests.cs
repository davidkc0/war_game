using System.Collections.Generic;
using WarGame.Sim;
using WarGame.Sim.Commands;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class RoadConstructionTests
{
    [Fact]
    public void EngineeringPath_RefusesWaterAndMountainPeaks()
    {
        var b = new MapState.Builder(5, 1);
        b.Set(1, 0, TileType.Water);
        b.Set(3, 0, TileType.MountainPeak);
        MapState map = b.Build();

        Assert.Empty(Pathfinding.FindRoadBuildPath(map, 0, 0, 2, 0));
        Assert.Empty(Pathfinding.FindRoadBuildPath(map, 2, 0, 4, 0));
    }

    [Fact]
    public void RoadConstruction_ConvertsRiverToBridge()
    {
        var s = BuildRoadMap(TileType.River);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(100);

        s = GameSim.Step(s, new List<Command>
        {
            new BuildRoadCommand(0, 1, 0) { PlayerId = (int)PlayerId.Player1 }
        });
        s = GameSim.StepN(s, RoadConstruction.BridgeBuildTicks);

        Assert.Equal(TileType.Bridge, s.Map.GetTileUnchecked(1, 0));
        Assert.Equal(1, s.Units[0].TileX);
        Assert.Empty(s.PendingRoads);
    }

    [Fact]
    public void RoadConstruction_ConvertsLandToRoad()
    {
        var s = BuildRoadMap(TileType.Forest);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(100);

        s = GameSim.Step(s, new List<Command>
        {
            new BuildRoadCommand(0, 1, 0) { PlayerId = (int)PlayerId.Player1 }
        });
        s = GameSim.StepN(s, RoadConstruction.RoadBuildTicks);

        Assert.Equal(TileType.Road, s.Map.GetTileUnchecked(1, 0));
        Assert.Equal(1, s.Units[0].TileX);
        Assert.Empty(s.PendingRoads);
    }

    [Fact]
    public void BuildRoadCommand_StoresSamePathAsPreviewPlanner()
    {
        var s = BuildRoadMap(TileType.Forest, width: 4);
        List<int> preview = Pathfinding.FindRoadBuildPath(s.Map, 0, 0, 3, 0);

        s = GameSim.Step(s, new List<Command>
        {
            new BuildRoadCommand(0, 3, 0) { PlayerId = (int)PlayerId.Player1 }
        });

        Assert.Single(s.PendingRoads);
        Assert.Equal(preview, s.PendingRoads[0].Path);
    }

    [Fact]
    public void ManualMove_CancelsRoadOrder()
    {
        var s = BuildRoadMap(TileType.Forest, width: 4);

        s = GameSim.Step(s, new List<Command>
        {
            new BuildRoadCommand(0, 3, 0) { PlayerId = (int)PlayerId.Player1 }
        });
        Assert.Single(s.PendingRoads);

        s = GameSim.Step(s, new List<Command>
        {
            new MoveUnitCommand(0, 2, 0) { PlayerId = (int)PlayerId.Player1 }
        });

        Assert.Empty(s.PendingRoads);
    }

    private static GameState BuildRoadMap(TileType middle, int width = 2)
    {
        var s = GameState.Initial(seed: 1);
        var b = new MapState.Builder(width, 1);
        for (int x = 0; x < width; x++) b.Set(x, 0, TileType.Plains);
        b.Set(1, 0, middle);
        s.Map = b.Build();
        s.TileOwner = new byte[width];
        s.TileSupplyOwner = new byte[width];
        s.TileRoadSupplyOwner = new byte[width];
        s.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0));
        return s;
    }
}
