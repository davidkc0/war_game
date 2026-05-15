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
    public void EngineeringPath_RefusesBroadWaterAndMountainPeaks()
    {
        var b = new MapState.Builder(4, 3);
        for (int y = 0; y < 3; y++)
        {
            b.Set(1, y, TileType.Water);
            b.Set(2, y, TileType.Water);
        }
        MapState map = b.Build();

        Assert.Empty(Pathfinding.FindRoadBuildPath(map, 0, 0, 3, 0));

        var peaks = new MapState.Builder(3, 1)
            .Set(1, 0, TileType.MountainPeak)
            .Build();
        Assert.Empty(Pathfinding.FindRoadBuildPath(peaks, 0, 0, 2, 0));
    }

    [Fact]
    public void EngineeringPath_AllowsBridgeAcrossOneTileWaterway()
    {
        var b = new MapState.Builder(3, 3);
        for (int y = 0; y < 3; y++)
            b.Set(1, y, TileType.Water);
        MapState map = b.Build();

        List<int> path = Pathfinding.FindRoadBuildPath(map, 0, 1, 2, 1);

        Assert.Equal(new[] { 4, 5 }, path);
    }

    [Fact]
    public void EngineeringPath_AllowsBuildingCurrentTile()
    {
        MapState map = new MapState.Builder(1, 1)
            .Set(0, 0, TileType.Forest)
            .Build();

        List<int> path = Pathfinding.FindRoadBuildPath(map, 0, 0, 0, 0);

        Assert.Equal(new[] { 0 }, path);
    }

    [Fact]
    public void EngineeringPath_BlocksRoadAlongSkinnyLandCauseway()
    {
        var b = new MapState.Builder(5, 3);
        for (int x = 1; x <= 3; x++)
        {
            b.Set(x, 0, TileType.Water);
            b.Set(x, 2, TileType.Water);
        }
        MapState map = b.Build();

        Assert.Empty(Pathfinding.FindRoadBuildPath(map, 0, 1, 4, 1));
    }

    [Fact]
    public void RoadConstruction_ConvertsRiverToBridge()
    {
        var s = BuildWaterwayMap(TileType.River);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(100);

        s = GameSim.Step(s, new List<Command>
        {
            new BuildRoadCommand(0, 1, 1) { PlayerId = (int)PlayerId.Player1 }
        });
        s = GameSim.StepN(s, RoadConstruction.BridgeBuildTicks);

        Assert.Equal(TileType.Bridge, s.Map.GetTileUnchecked(1, 1));
        Assert.Equal(1, s.Units[0].TileX);
        Assert.Equal(1, s.Units[0].TileY);
        Assert.Empty(s.PendingRoads);
    }

    [Fact]
    public void RoadConstruction_ConvertsOneTileWaterwayToBridge()
    {
        var s = GameState.Initial(seed: 1);
        var b = new MapState.Builder(3, 3);
        for (int y = 0; y < 3; y++)
            b.Set(1, y, TileType.Water);
        s.Map = b.Build();
        s.TileOwner = new byte[9];
        s.TileSupplyOwner = new byte[9];
        s.TileRoadSupplyOwner = new byte[9];
        s.Cities.Add(City.Create(0, 0, 1, PlayerId.Player1, isCapital: true));
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 1));
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(100);

        s = GameSim.Step(s, new List<Command>
        {
            new BuildRoadCommand(0, 1, 1) { PlayerId = (int)PlayerId.Player1 }
        });
        s = GameSim.StepN(s, RoadConstruction.BridgeBuildTicks);

        Assert.Equal(TileType.Bridge, s.Map.GetTileUnchecked(1, 1));
        Assert.Equal(1, s.Units[0].TileX);
        Assert.Equal(1, s.Units[0].TileY);
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
    public void RoadConstruction_ConvertsCurrentTileToRoad()
    {
        var s = BuildRoadMap(TileType.Forest);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(100);

        s = GameSim.Step(s, new List<Command>
        {
            new BuildRoadCommand(0, 0, 0) { PlayerId = (int)PlayerId.Player1 }
        });
        s = GameSim.StepN(s, RoadConstruction.RoadBuildTicks);

        Assert.Equal(TileType.Road, s.Map.GetTileUnchecked(0, 0));
        Assert.Equal(0, s.Units[0].TileX);
        Assert.Empty(s.PendingRoads);
    }

    [Fact]
    public void RoadConstruction_CancelsWhenUnitOccupiesNextSegment()
    {
        var s = BuildRoadMap(TileType.Forest, width: 3);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(100);
        s.Cities.Add(City.Create(1, 2, 0, PlayerId.Player2, isCapital: true));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 1, 0));

        s = GameSim.Step(s, new List<Command>
        {
            new BuildRoadCommand(0, 2, 0) { PlayerId = (int)PlayerId.Player1 }
        });

        Assert.Empty(s.PendingRoads);
        Assert.Equal(TileType.Forest, s.Map.GetTileUnchecked(1, 0));
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

    private static GameState BuildWaterwayMap(TileType waterway)
    {
        var s = GameState.Initial(seed: 1);
        var b = new MapState.Builder(3, 3);
        for (int y = 0; y < 3; y++)
            b.Set(1, y, waterway);
        s.Map = b.Build();
        s.TileOwner = new byte[9];
        s.TileSupplyOwner = new byte[9];
        s.TileRoadSupplyOwner = new byte[9];
        s.Cities.Add(City.Create(0, 0, 1, PlayerId.Player1, isCapital: true));
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 1));
        return s;
    }
}
