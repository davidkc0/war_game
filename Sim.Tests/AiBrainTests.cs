using System.Collections.Generic;
using WarGame.Sim.AI;
using WarGame.Sim.Commands;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class AiBrainTests
{
    [Fact]
    public void Decide_IsDeterministic_ForSameState()
    {
        GameState s = BasicState();
        s.Tick = 90;

        List<Command> a = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);
        List<Command> b = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);

        Assert.Equal(CommandText(a), CommandText(b));
    }

    [Fact]
    public void Decide_OnlyEmitsCommandsForAiOwnedObjects()
    {
        GameState s = BasicState();
        s.Tick = 90;
        RevealAll(ref s, PlayerId.Player2);

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);

        Assert.NotEmpty(commands);
        foreach (Command cmd in commands)
        {
            Assert.Equal((int)PlayerId.Player2, cmd.PlayerId);
            switch (cmd)
            {
                case MoveUnitCommand m:
                    Assert.Equal(PlayerId.Player2, s.Units[m.UnitId].Owner);
                    break;
                case BuildUnitCommand b:
                    Assert.Equal(PlayerId.Player2, s.Cities[b.CityId].Owner);
                    break;
                case UpgradeCityCommand u:
                    Assert.Equal(PlayerId.Player2, s.Cities[u.CityId].Owner);
                    break;
                case BuildRoadCommand r:
                    Assert.Equal(PlayerId.Player2, s.Units[r.UnitId].Owner);
                    Assert.True(FogOfWar.IsVisible(s, PlayerId.Player2, r.TargetX, r.TargetY));
                    break;
                case BuildFortCommand f:
                    Assert.True(FogOfWar.IsVisible(s, PlayerId.Player2, f.TargetX, f.TargetY));
                    break;
                case ChoosePromotionCommand p:
                    Assert.Equal(PlayerId.Player2, s.Units[p.UnitId].Owner);
                    break;
            }
        }
    }

    [Fact]
    public void Production_BuildsFromIdleOwnedCity()
    {
        GameState s = BasicState();
        s.Tick = 0;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Medium);

        Assert.Contains(commands, c => c is BuildUnitCommand b
            && b.CityId == 0
            && b.Type == UnitType.Light
            && b.PlayerId == (int)PlayerId.Player2);
    }

    [Fact]
    public void Production_UpgradesCapitalWhenSupplyPressuredAndAffordable()
    {
        GameState s = BasicState();
        AddAiLightUnits(ref s, 4);
        s.Players[(int)PlayerId.Player2].Eco = FP.FromInt(70);
        PrepareDerived(ref s);
        s.Tick = 0;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Medium);

        Assert.Contains(commands, c => c is UpgradeCityCommand u
            && u.CityId == 0
            && u.PlayerId == (int)PlayerId.Player2);
        Assert.DoesNotContain(commands, c => c is BuildUnitCommand b && b.CityId == 0);
    }

    [Fact]
    public void Production_HardUpgradesEarlierThanEasyUnderSupplyPressure()
    {
        GameState s = BasicState();
        AddAiLightUnits(ref s, 3);
        s.Players[(int)PlayerId.Player2].Eco = FP.FromInt(50);
        PrepareDerived(ref s);
        s.Tick = 0;

        List<Command> hard = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);
        List<Command> easy = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Easy);

        Assert.Contains(hard, c => c is UpgradeCityCommand u && u.CityId == 0);
        Assert.DoesNotContain(easy, c => c is UpgradeCityCommand);
    }

    [Fact]
    public void Production_DoesNotStartSecondUpgradeOnMediumSmallEconomy()
    {
        GameState s = BasicState();
        s.Map.SetTile(3, 1, TileType.City);
        s.Cities.Add(City.Create(2, 3, 1, PlayerId.Player2, isCapital: false));
        City upgrading = s.Cities[0];
        upgrading.DevelopmentOrder = 2;
        upgrading.DevelopmentProgress = FP.FromInt(5);
        s.Cities[0] = upgrading;
        AddAiLightUnits(ref s, 8);
        s.Players[(int)PlayerId.Player2].Eco = FP.FromInt(200);
        PrepareDerived(ref s);
        s.Tick = 0;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Medium);

        Assert.DoesNotContain(commands, c => c is UpgradeCityCommand u && u.CityId == 2);
    }

    [Fact]
    public void Production_SkipsUpgradeWhenOwnedCityIsThreatened()
    {
        GameState s = BasicState(enemyX: 4, enemyY: 1);
        AddAiLightUnits(ref s, 4);
        s.Players[(int)PlayerId.Player2].Eco = FP.FromInt(100);
        PrepareDerived(ref s);
        s.Tick = 0;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);

        Assert.DoesNotContain(commands, c => c is UpgradeCityCommand);
    }

    [Fact]
    public void Promotion_SpendsPointUsingPriority()
    {
        GameState s = BasicState();
        Unit u = s.Units[0];
        u.PromotionPoints = 1;
        s.Units[0] = u;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Medium);

        Assert.Contains(commands, c => c is ChoosePromotionCommand p
            && p.UnitId == 0
            && p.PerkId == (byte)UnitPerk.LightOptics);
    }

    [Fact]
    public void Tactical_VisibleEnemy_DrawsUnitsTowardApproachTile()
    {
        GameState s = BasicState(enemyX: 4, enemyY: 1);
        s.Tick = 10;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);

        Assert.Contains(commands, c => c is MoveUnitCommand m
            && m.UnitId == 0
            && IsAdjacent(m.TargetX, m.TargetY, 4, 1));
    }

    [Fact]
    public void Tactical_HiddenEnemyIsIgnored()
    {
        GameState s = BasicState(width: 16, height: 16, enemyX: 14, enemyY: 14);
        s.Tick = 10;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);

        Assert.DoesNotContain(commands, c => c is MoveUnitCommand m
            && IsAdjacent(m.TargetX, m.TargetY, 14, 14));
    }

    [Fact]
    public void Operational_CapturesKnownNeutralCity()
    {
        GameState s = BasicState();
        s.Map.SetTile(3, 1, TileType.City);
        s.Cities.Add(City.Create(2, 3, 1, PlayerId.None, isCapital: false));
        PrepareDerived(ref s);
        s.Tick = 30;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Medium);

        Assert.Contains(commands, c => c is MoveUnitCommand m
            && m.UnitId == 0
            && m.TargetX == 3
            && m.TargetY == 1);
    }

    [Fact]
    public void Tactical_WoundedUnitRetreatsTowardOwnedShelter()
    {
        GameState s = BasicState();
        s.Units[0] = Unit.Create(0, PlayerId.Player2, UnitType.Light, 5, 1);
        Unit wounded = s.Units[0];
        wounded.Hp = FP.FromInt(12);
        s.Units[0] = wounded;
        PrepareDerived(ref s);
        s.Tick = 10;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);

        Assert.Contains(commands, c => c is MoveUnitCommand m
            && m.UnitId == 0
            && m.TargetX == 1
            && m.TargetY == 1);
    }

    [Fact]
    public void Strategic_BuildsFortInThreatenedOwnedTerritory()
    {
        GameState s = BasicState(enemyX: 5, enemyY: 1);
        s.Players[(int)PlayerId.Player2].Eco = FP.FromInt(100);
        s.Tick = 90;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);

        Assert.Contains(commands, c => c is BuildFortCommand f
            && FogOfWar.IsVisible(s, PlayerId.Player2, f.TargetX, f.TargetY));
    }

    [Fact]
    public void Strategic_BuildsVisibleRoadRoute()
    {
        GameState s = OwnLogisticsState(width: 10, height: 3);
        s.Map.SetTile(5, 1, TileType.City);
        s.Cities.Add(City.Create(1, 5, 1, PlayerId.Player2, isCapital: false));
        PrepareDerived(ref s);
        RevealAll(ref s, PlayerId.Player2);
        s.Tick = 90;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Medium);

        Assert.Contains(commands, c => c is BuildRoadCommand r
            && r.UnitId == 0
            && FogOfWar.IsVisible(s, PlayerId.Player2, r.TargetX, r.TargetY));
    }

    [Fact]
    public void Operational_InterdictsVisibleEnemyRoad()
    {
        GameState s = BasicState(width: 8, height: 3, enemyX: 7, enemyY: 1);
        Unit enemy = s.Units[1];
        enemy.Hp = FP.Zero;
        s.Units[1] = enemy;
        s.Map.SetTile(4, 1, TileType.Road);
        PrepareDerived(ref s);
        RevealAll(ref s, PlayerId.Player2);
        int roadIdx = 1 * s.Map.Width + 4;
        s.TileOwner[roadIdx] = (byte)PlayerId.Player1;
        s.TileRoadSupplyOwner[roadIdx] = (byte)PlayerId.Player1;
        s.Tick = 30;

        List<Command> commands = AiBrain.Decide(s, PlayerId.Player2, AiDifficulty.Hard);

        Assert.Contains(commands, c => c is MoveUnitCommand m
            && m.UnitId == 0
            && m.TargetX == 4
            && m.TargetY == 1);
    }

    private static GameState OwnLogisticsState(int width, int height)
    {
        var s = GameState.Initial(seed: 77);
        var b = new MapState.Builder(width, height);
        b.Set(1, 1, TileType.Capital);
        s.Map = b.Build();
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player2, isCapital: true));
        s.Units.Add(Unit.Create(0, PlayerId.Player2, UnitType.Light, 1, 1));
        PrepareDerived(ref s);
        return s;
    }

    private static GameState BasicState(
        int width = 12,
        int height = 8,
        int enemyX = 10,
        int enemyY = 6)
    {
        var s = GameState.Initial(seed: 42);
        var b = new MapState.Builder(width, height);
        b.Set(1, 1, TileType.Capital);
        b.Set(enemyX, enemyY, TileType.Capital);
        s.Map = b.Build();
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player2, isCapital: true));
        s.Cities.Add(City.Create(1, enemyX, enemyY, PlayerId.Player1, isCapital: true));
        s.Units.Add(Unit.Create(0, PlayerId.Player2, UnitType.Light, 1, 1));
        s.Units.Add(Unit.Create(1, PlayerId.Player1, UnitType.Light, enemyX, enemyY));
        PrepareDerived(ref s);
        return s;
    }

    private static void PrepareDerived(ref GameState s)
    {
        PowerProjection.Tick(ref s);
        SupplyLines.Tick(ref s);
        FogOfWar.Tick(ref s);
    }

    private static void RevealAll(ref GameState s, PlayerId viewer)
    {
        int tileCount = s.Map.TileCount;
        int offset = (int)viewer * tileCount;
        for (int i = 0; i < tileCount; i++)
        {
            s.TileVisibility[offset + i] = (byte)VisibilityState.Visible;
            int x = i % s.Map.Width;
            int y = i / s.Map.Width;
            s.LastSeenTileType[offset + i] = (byte)s.Map.GetTileUnchecked(x, y);
            s.LastSeenTileOwner[offset + i] = s.TileOwner[i];
        }
    }

    private static void AddAiLightUnits(ref GameState s, int count)
    {
        int[,] spots =
        {
            { 2, 1 }, { 1, 2 }, { 2, 2 }, { 3, 1 }, { 3, 2 },
            { 1, 3 }, { 2, 3 }, { 3, 3 }, { 4, 2 }, { 4, 3 },
        };

        for (int i = 0; i < count; i++)
        {
            int idx = s.Units.Count;
            int spot = i % spots.GetLength(0);
            s.Units.Add(Unit.Create(idx, PlayerId.Player2, UnitType.Light, spots[spot, 0], spots[spot, 1]));
        }
    }

    private static bool IsAdjacent(int ax, int ay, int bx, int by)
        => System.Math.Abs(ax - bx) + System.Math.Abs(ay - by) == 1;

    private static string CommandText(List<Command> commands)
    {
        var parts = new List<string>();
        foreach (Command command in commands)
        {
            parts.Add(command switch
            {
                MoveUnitCommand m => $"M:{m.PlayerId}:{m.UnitId}:{m.TargetX}:{m.TargetY}",
                BuildUnitCommand b => $"B:{b.PlayerId}:{b.CityId}:{b.Type}",
                UpgradeCityCommand u => $"U:{u.PlayerId}:{u.CityId}",
                BuildFortCommand f => $"F:{f.PlayerId}:{f.TargetX}:{f.TargetY}",
                BuildRoadCommand r => $"R:{r.PlayerId}:{r.UnitId}:{r.TargetX}:{r.TargetY}",
                ChoosePromotionCommand p => $"P:{p.PlayerId}:{p.UnitId}:{p.PerkId}",
                _ => command.GetType().Name,
            });
        }
        return string.Join("|", parts);
    }
}
