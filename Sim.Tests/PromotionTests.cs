using System.Collections.Generic;
using WarGame.Sim;
using WarGame.Sim.Commands;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class PromotionTests
{
    [Fact]
    public void CombatDamage_AwardsXpAndKillBonus()
    {
        var s = GameState.Initial(seed: 21);
        s.Map = new MapState.Builder(3, 1).Build();
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0));
        var target = Unit.Create(1, PlayerId.Player2, UnitType.Light, 1, 0);
        target.Hp = FP.One / FP.FromInt(10);
        s.Units.Add(target);

        Combat.Tick(ref s);

        Assert.False(s.Units[1].IsAlive);
        Assert.True(s.Units[0].XpRaw >= UnitProgression.KillBonusXp.Raw);
    }

    [Fact]
    public void RankThresholds_GrantPromotionPointsOnce()
    {
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0);

        UnitProgression.AwardXp(ref u, UnitProgression.Rank2Xp);
        Assert.Equal(2, u.Rank);
        Assert.Equal(1, u.PromotionPoints);

        UnitProgression.AwardXp(ref u, FP.FromInt(5));
        Assert.Equal(2, u.Rank);
        Assert.Equal(1, u.PromotionPoints);
    }

    [Fact]
    public void ChoosePromotionCommand_AppliesValidOwnedPerk()
    {
        var s = GameState.Initial(seed: 22);
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0);
        u.PromotionPoints = 1;
        s.Units.Add(u);

        s = GameSim.Step(s, new List<Command> {
            new ChoosePromotionCommand(0, (byte)UnitPerk.LightOptics) { PlayerId = (int)PlayerId.Player1 },
        });

        Assert.True(UnitProgression.HasPerk(s.Units[0], UnitPerk.LightOptics));
        Assert.Equal(0, s.Units[0].PromotionPoints);
        Assert.Equal(UnitStats.LightVisionRadius + 1, UnitProgression.VisionRadius(s.Units[0]));
    }

    [Fact]
    public void ChoosePromotionCommand_RejectsInvalidTypeAndWrongOwner()
    {
        var wrongType = GameState.Initial(seed: 23);
        var heavy = Unit.Create(0, PlayerId.Player1, UnitType.Heavy, 0, 0);
        heavy.PromotionPoints = 1;
        wrongType.Units.Add(heavy);
        wrongType = GameSim.Step(wrongType, new List<Command> {
            new ChoosePromotionCommand(0, (byte)UnitPerk.LightOptics) { PlayerId = (int)PlayerId.Player1 },
        });
        Assert.Equal(0u, wrongType.Units[0].PerkMask);
        Assert.Equal(1, wrongType.Units[0].PromotionPoints);

        var wrongOwner = GameState.Initial(seed: 24);
        var light = Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0);
        light.PromotionPoints = 1;
        wrongOwner.Units.Add(light);
        wrongOwner = GameSim.Step(wrongOwner, new List<Command> {
            new ChoosePromotionCommand(0, (byte)UnitPerk.LightOptics) { PlayerId = (int)PlayerId.Player2 },
        });
        Assert.Equal(0u, wrongOwner.Units[0].PerkMask);
        Assert.Equal(1, wrongOwner.Units[0].PromotionPoints);
    }

    [Fact]
    public void PathfinderAndRoadRunner_ModifyOnlyLightMovementFactors()
    {
        var plain = Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0);
        var pathfinder = plain;
        pathfinder.PromotionPoints = 1;
        UnitProgression.TryChoosePerk(ref pathfinder, (byte)UnitPerk.LightPathfinder);

        Assert.True(UnitProgression.SpeedFactorRaw(pathfinder, TileType.Mountain)
            > UnitProgression.SpeedFactorRaw(plain, TileType.Mountain));
        Assert.True(UnitProgression.SpeedFactorRaw(pathfinder, TileType.River)
            > UnitProgression.SpeedFactorRaw(plain, TileType.River));

        var roadRunner = plain;
        roadRunner.PromotionPoints = 1;
        UnitProgression.TryChoosePerk(ref roadRunner, (byte)UnitPerk.LightRoadRunner);
        Assert.True(UnitProgression.SpeedFactorRaw(roadRunner, TileType.Road)
            > UnitProgression.SpeedFactorRaw(plain, TileType.Road));
    }
}
