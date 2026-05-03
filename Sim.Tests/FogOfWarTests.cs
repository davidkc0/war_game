using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class FogOfWarTests
{
    [Fact]
    public void Tick_InitializesPerPlayerFogArrays()
    {
        var s = BuildState(5, 5);

        FogOfWar.Tick(ref s);

        int expectedLen = s.Map.TileCount * 3;
        Assert.Equal(GameState.CurrentVersion, s.Version);
        Assert.Equal(expectedLen, s.TileVisibility.Length);
        Assert.Equal(expectedLen, s.LastSeenTileType.Length);
        Assert.Equal(expectedLen, s.LastSeenTileOwner.Length);
    }

    [Fact]
    public void LightUnit_RevealsFiveTileDiamond()
    {
        var s = BuildState(20, 20);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 10, 10));

        FogOfWar.Tick(ref s);

        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 15, 10));
        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 12, 13));
        Assert.Equal(VisibilityState.Hidden, FogOfWar.GetVisibility(s, PlayerId.Player1, 16, 10));
        Assert.Equal(VisibilityState.Hidden, FogOfWar.GetVisibility(s, PlayerId.Player2, 10, 10));
    }

    [Fact]
    public void HeavyUnit_RevealsFourTileDiamond()
    {
        var s = BuildState(20, 20);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Heavy, 10, 10));

        FogOfWar.Tick(ref s);

        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 14, 10));
        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 12, 12));
        Assert.Equal(VisibilityState.Hidden, FogOfWar.GetVisibility(s, PlayerId.Player1, 15, 10));
    }

    [Fact]
    public void OwnedStructures_RevealEightTileDiamond()
    {
        var s = BuildState(24, 24);
        s.Map.SetTile(10, 10, TileType.Capital);
        s.Cities.Add(City.Create(0, 10, 10, PlayerId.Player1, isCapital: true));

        FogOfWar.Tick(ref s);

        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 18, 10));
        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 14, 14));
        Assert.Equal(VisibilityState.Hidden, FogOfWar.GetVisibility(s, PlayerId.Player1, 19, 10));
    }

    [Fact]
    public void VisibleTiles_DowngradeToExploredAndKeepLastSeenState()
    {
        var s = BuildState(20, 1);
        s.Map.SetTile(10, 0, TileType.Road);
        s.TileOwner[10] = (byte)PlayerId.Player2;
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));

        FogOfWar.Tick(ref s);
        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 10, 0));
        Assert.Equal(TileType.Road, FogOfWar.GetKnownTileType(s, PlayerId.Player1, 10, 0));
        Assert.Equal(PlayerId.Player2, FogOfWar.GetKnownTileOwner(s, PlayerId.Player1, 10, 0));

        var u = s.Units[0];
        u.TileX = 0;
        s.Units[0] = u;
        s.Map.SetTile(10, 0, TileType.Bridge);
        s.TileOwner[10] = (byte)PlayerId.Player1;

        FogOfWar.Tick(ref s);

        Assert.Equal(VisibilityState.Explored, FogOfWar.GetVisibility(s, PlayerId.Player1, 10, 0));
        Assert.Equal(TileType.Road, FogOfWar.GetKnownTileType(s, PlayerId.Player1, 10, 0));
        Assert.Equal(PlayerId.Player2, FogOfWar.GetKnownTileOwner(s, PlayerId.Player1, 10, 0));
    }

    [Fact]
    public void LastSeenState_UpdatesAgainWhenTileBecomesVisible()
    {
        var s = BuildState(20, 1);
        s.Map.SetTile(10, 0, TileType.Road);
        s.TileOwner[10] = (byte)PlayerId.Player2;
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));
        FogOfWar.Tick(ref s);

        var u = s.Units[0];
        u.TileX = 0;
        s.Units[0] = u;
        s.Map.SetTile(10, 0, TileType.Bridge);
        s.TileOwner[10] = (byte)PlayerId.Player1;
        FogOfWar.Tick(ref s);

        u = s.Units[0];
        u.TileX = 10;
        s.Units[0] = u;
        FogOfWar.Tick(ref s);

        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 10, 0));
        Assert.Equal(TileType.Bridge, FogOfWar.GetKnownTileType(s, PlayerId.Player1, 10, 0));
        Assert.Equal(PlayerId.Player1, FogOfWar.GetKnownTileOwner(s, PlayerId.Player1, 10, 0));
    }

    [Fact]
    public void FriendlyScoutDeath_ClearsKnownFriendlyControlWithoutRevealingEnemyOwner()
    {
        var s = BuildState(20, 1);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0));
        PowerProjection.Tick(ref s);
        FogOfWar.Tick(ref s);

        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 9, 0));
        Assert.Equal(PlayerId.Player1, FogOfWar.GetKnownTileOwner(s, PlayerId.Player1, 9, 0));

        var scout = s.Units[0];
        scout.Hp = FP.Zero;
        s.Units[0] = scout;
        s.TileOwner[9] = (byte)PlayerId.Player2;

        FogOfWar.Tick(ref s);

        Assert.Equal(VisibilityState.Explored, FogOfWar.GetVisibility(s, PlayerId.Player1, 9, 0));
        Assert.Equal(PlayerId.None, FogOfWar.GetKnownTileOwner(s, PlayerId.Player1, 9, 0));
    }

    [Fact]
    public void GameSimStep_RefreshesFogAfterSystems()
    {
        var s = BuildState(10, 10);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 4, 4));

        s = GameSim.Step(s, null);

        Assert.Equal(VisibilityState.Visible, FogOfWar.GetVisibility(s, PlayerId.Player1, 4, 4));
        Assert.Equal(VisibilityState.Hidden, FogOfWar.GetVisibility(s, PlayerId.Player2, 4, 4));
    }

    private static GameState BuildState(int width, int height)
    {
        var s = GameState.Initial(seed: 1);
        s.Map = new MapState.Builder(width, height, TileType.Plains).Build();
        s.TileOwner = new byte[width * height];
        s.TileSupplyOwner = new byte[width * height];
        s.TileRoadSupplyOwner = new byte[width * height];
        return s;
    }
}
