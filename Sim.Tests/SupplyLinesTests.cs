using WarGame.Sim;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class SupplyLinesTests
{
    [Fact]
    public void FriendlyTerritory_CarriesNormalSupply()
    {
        var s = BuildStrip(TileType.Plains);
        for (int i = 0; i < s.TileOwner.Length; i++)
            s.TileOwner[i] = (byte)PlayerId.Player1;
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 4, 0));

        SupplyLines.Tick(ref s);

        Assert.Equal(SupplyStatus.Supplied, SupplyLines.GetUnitStatus(s, 0));
        Assert.Equal((byte)PlayerId.Player1, s.TileSupplyOwner[4]);
    }

    [Fact]
    public void RoadOutsideFriendlyTerritory_CarriesRoadSupply()
    {
        var s = BuildStrip(TileType.Road);
        s.TileOwner[0] = (byte)PlayerId.Player1;
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 4, 0));

        SupplyLines.Tick(ref s);

        Assert.Equal(SupplyStatus.RoadSupplied, SupplyLines.GetUnitStatus(s, 0));
        Assert.Equal((byte)PlayerId.Player1, s.TileRoadSupplyOwner[4]);
    }

    [Fact]
    public void EnemyOnRoad_InterdictsRoadSupplyPastThatTile()
    {
        var s = BuildStrip(TileType.Road);
        s.TileOwner[0] = (byte)PlayerId.Player1;
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 4, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 2, 0));

        SupplyLines.Tick(ref s);

        Assert.Equal(SupplyStatus.CutOff, SupplyLines.GetUnitStatus(s, 0));
        Assert.Equal((byte)PlayerId.None, s.TileRoadSupplyOwner[4]);
    }

    [Fact]
    public void EnemyOnFriendlyRoad_RemovesRoadBonusPastBlocker()
    {
        var s = BuildStrip(TileType.Road);
        for (int i = 0; i < s.TileOwner.Length; i++)
            s.TileOwner[i] = (byte)PlayerId.Player1;
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 4, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 2, 0));

        SupplyLines.Tick(ref s);

        Assert.Equal(SupplyStatus.Supplied, SupplyLines.GetUnitStatus(s, 0));
        Assert.Equal((byte)PlayerId.None, s.TileRoadSupplyOwner[4]);
    }

    [Fact]
    public void RoadSupply_DoesNotAllowHealingAwayFromFriendlyShelter()
    {
        var s = BuildStrip(TileType.Road);
        s.TileOwner[0] = (byte)PlayerId.Player1;
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 4, 0);
        u.Hp = FP.FromInt(10);
        s.Units.Add(u);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(50);

        SupplyLines.Tick(ref s);
        Healing.Tick(ref s);

        Assert.Equal(SupplyStatus.RoadSupplied, SupplyLines.GetUnitStatus(s, 0));
        Assert.Equal(FP.FromInt(10), s.Units[0].Hp);
    }

    [Fact]
    public void HealingRequiresFriendlyControlledShelterTile()
    {
        var s = BuildStrip(TileType.Plains);
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0);
        u.Hp = FP.FromInt(10);
        s.Units.Add(u);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(50);

        s.TileOwner[0] = (byte)PlayerId.Player2;
        SupplyLines.Tick(ref s);
        Healing.Tick(ref s);
        Assert.Equal(FP.FromInt(10), s.Units[0].Hp);

        s.TileOwner[0] = (byte)PlayerId.Player1;
        SupplyLines.Tick(ref s);
        Healing.Tick(ref s);
        Assert.True(s.Units[0].Hp > FP.FromInt(10));
    }

    [Fact]
    public void Maintenance_UsesRoadDiscountAndCutOffPenalty()
    {
        var road = BuildStrip(TileType.Road);
        road.TileOwner[0] = (byte)PlayerId.Player1;
        road.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 4, 0));
        road.Players[(int)PlayerId.Player1].Eco = FP.FromInt(10);
        SupplyLines.Tick(ref road);
        Maintenance.Tick(ref road);
        FP roadCost = FP.FromInt(10) - road.Players[(int)PlayerId.Player1].Eco;

        var cut = BuildStrip(TileType.Plains);
        cut.TileOwner[0] = (byte)PlayerId.Player1;
        cut.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 4, 0));
        cut.Players[(int)PlayerId.Player1].Eco = FP.FromInt(10);
        SupplyLines.Tick(ref cut);
        Maintenance.Tick(ref cut);
        FP cutCost = FP.FromInt(10) - cut.Players[(int)PlayerId.Player1].Eco;

        Assert.Equal(Maintenance.MaintenancePerTick * Maintenance.RoadSupplyMultiplier, roadCost);
        Assert.Equal(Maintenance.MaintenancePerTick * Maintenance.CutOffMultiplier, cutCost);
    }

    [Fact]
    public void FriendlyTerritorySupply_DoesNotFloodAcrossBroadWater()
    {
        var s = GameState.Initial(seed: 8);
        var b = new MapState.Builder(6, 1);
        b.Set(0, 0, TileType.Capital);
        b.Set(2, 0, TileType.Water);
        b.Set(3, 0, TileType.Water);
        s.Map = b.Build();
        s.TileOwner = new byte[6];
        s.TileSupplyOwner = new byte[6];
        s.TileRoadSupplyOwner = new byte[6];
        for (int i = 0; i < s.TileOwner.Length; i++)
            s.TileOwner[i] = (byte)PlayerId.Player1;
        s.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));

        SupplyLines.Tick(ref s);

        Assert.Equal(SupplyStatus.CutOff, SupplyLines.GetUnitStatus(s, 0));
        Assert.Equal((byte)PlayerId.None, s.TileSupplyOwner[5]);
    }

    private static GameState BuildStrip(TileType fill)
    {
        var s = GameState.Initial(seed: 1);
        var b = new MapState.Builder(5, 1, fill);
        b.Set(0, 0, TileType.Capital);
        s.Map = b.Build();
        s.TileOwner = new byte[5];
        s.TileSupplyOwner = new byte[5];
        s.TileRoadSupplyOwner = new byte[5];
        s.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
        return s;
    }
}
