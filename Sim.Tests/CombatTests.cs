using System.Collections.Generic;
using WarGame.Sim;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class CombatTests
{
    private static GameState BuildEmpty(int w, int h)
    {
        var s = GameState.Initial(seed: 1);
        s.Map = new MapState.Builder(w, h).Build();
        // Each player gets a capital at a corner so combat tests don't
        // collide with the encirclement system. The corners are far enough
        // from the test arena (typical 5x1) that they don't perturb power
        // projection at the action area.
        s.Cities.Add(City.Create(0, 0, 0,                 PlayerId.Player1, isCapital: true));
        s.Cities.Add(City.Create(1, w - 1, h - 1,         PlayerId.Player2, isCapital: true));
        return s;
    }

    [Fact]
    public void AdjacentEnemies_BothTakeDamage()
    {
        var s = BuildEmpty(5, 1);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 1, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 2, 0));

        FP hp0Before = s.Units[0].Hp;
        FP hp1Before = s.Units[1].Hp;
        s = GameSim.Step(s, null);
        Assert.True(s.Units[0].Hp < hp0Before);
        Assert.True(s.Units[1].Hp < hp1Before);
    }

    [Fact]
    public void NonAdjacentEnemies_DoNotEngage()
    {
        var s = BuildEmpty(5, 1);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 4, 0));

        s = GameSim.Step(s, null);
        Assert.Equal(UnitStats.LightMaxHp, s.Units[0].Hp);
        Assert.Equal(UnitStats.LightMaxHp, s.Units[1].Hp);
    }

    [Fact]
    public void Allies_DoNotEngage()
    {
        var s = BuildEmpty(5, 1);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 1, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player1, UnitType.Light, 2, 0));

        s = GameSim.Step(s, null);
        Assert.Equal(UnitStats.LightMaxHp, s.Units[0].Hp);
        Assert.Equal(UnitStats.LightMaxHp, s.Units[1].Hp);
    }

    [Fact]
    public void HeavyKillsLightFasterThanLightKillsHeavy()
    {
        // Heavy 20 dmg/s vs light's 60 HP -> dies in 3s. Light 8 dmg/s vs
        // heavy's 150 HP -> dies in ~19s. After ~5 sec the light should be
        // dead and the heavy alive.
        var s = BuildEmpty(5, 1);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Heavy, 1, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 2, 0));

        s = GameSim.StepN(s, 5 * GameSim.TicksPerSecond);
        Assert.False(s.Units[1].IsAlive, "light should be dead");
        Assert.True(s.Units[0].IsAlive, "heavy should be alive");
    }

    [Fact]
    public void DeadUnits_StaySlot_ButCannotEngage()
    {
        var s = BuildEmpty(5, 1);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 1, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Heavy, 2, 0));

        // Run long enough to kill the light.
        s = GameSim.StepN(s, 10 * GameSim.TicksPerSecond);
        Assert.False(s.Units[0].IsAlive);
        // Slot 0 still exists — Id stability.
        Assert.Equal(2, s.Units.Count);
        Assert.Equal(0, s.Units[0].Id);
    }
}

public class ProductionTests
{
    private static GameState BuildEmpty(int w, int h)
    {
        var s = GameState.Initial(seed: 1);
        s.Map = new MapState.Builder(w, h).Build();
        return s;
    }

    [Fact]
    public void City_AccruesEcoForOwner()
    {
        var s = BuildEmpty(5, 5);
        s.Cities.Add(City.Create(0, 2, 2, PlayerId.Player1, isCapital: false));

        FP ecoBefore = s.Players[(int)PlayerId.Player1].Eco;
        s = GameSim.StepN(s, GameSim.TicksPerSecond);  // 1 second
        FP ecoAfter = s.Players[(int)PlayerId.Player1].Eco;
        // ~1 ECO/sec; allow tiny FP drift.
        FP gained = ecoAfter - ecoBefore;
        Assert.True(gained > FP.FromInt(1) - FP.FromRaw(1024),
            $"expected ~1 ECO; got {gained}");
        Assert.True(gained < FP.FromInt(1) + FP.FromRaw(1024));
    }

    [Fact]
    public void Capital_Produces3xCity()
    {
        var s = BuildEmpty(5, 5);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));
        s.Cities.Add(City.Create(1, 3, 3, PlayerId.Player2, isCapital: true));

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 5);
        FP city = s.Players[(int)PlayerId.Player1].Eco;
        FP capital = s.Players[(int)PlayerId.Player2].Eco;
        // capital >= 2.5x city (allowing for FP drift on the per-tick rate).
        Assert.True(capital * FP.FromInt(2) > city * FP.FromInt(5),
            $"capital {capital} not ~3x city {city}");
    }

    [Fact]
    public void CityDevelopment_ScalesIncomeAndSupply()
    {
        var s = BuildEmpty(5, 5);
        var city = City.Create(0, 1, 1, PlayerId.Player1, isCapital: false);
        city.DevelopmentLevel = 3;
        s.Cities.Add(city);
        s.Cities.Add(City.Create(1, 3, 3, PlayerId.Player2, isCapital: true));

        s = GameSim.StepN(s, GameSim.TicksPerSecond);

        FP gained = s.Players[(int)PlayerId.Player1].Eco;
        Assert.True(gained > FP.FromInt(3) - FP.FromRaw(1024),
            $"expected ~3 ECO; got {gained}");
        Assert.True(gained < FP.FromInt(3) + FP.FromRaw(1024));
        Assert.Equal(12, UnitStats.SupplyCapacity(s.Cities[0]));
    }

    [Fact]
    public void UpgradeCity_CompletesAndScalesSupply()
    {
        var s = BuildEmpty(5, 5);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));
        s.Cities.Add(City.Create(1, 3, 3, PlayerId.Player2, isCapital: true));
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(50);

        s = GameSim.Step(s, new List<Commands.Command> {
            new Commands.UpgradeCityCommand(0) { PlayerId = (int)PlayerId.Player1 },
        });
        Assert.True(s.Cities[0].IsUpgrading);
        Assert.False(s.Cities[0].IsProducing);

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 41);

        Assert.Equal(2, s.Cities[0].DevelopmentLevel);
        Assert.Equal(8, s.Cities[0].SupplyCapacity);
        Assert.False(s.Cities[0].IsUpgrading);
    }

    [Fact]
    public void UpgradeCity_RejectsWrongPlayerFortNeutralAndMaxLevel()
    {
        var s = BuildEmpty(5, 5);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));
        s.Cities.Add(City.Create(1, 2, 1, PlayerId.None, isCapital: false));
        s.Map.SetTile(3, 1, TileType.Fort);
        s.Cities.Add(new City
        {
            Id = 2,
            TileX = 3,
            TileY = 1,
            Owner = PlayerId.Player1,
            OriginalOwner = PlayerId.Player1,
            SupplyCapacity = FortConstruction.FortSupplyCapacity,
            DevelopmentLevel = 0,
            CaptureHp = FortConstruction.FortCaptureHp,
        });
        var max = City.Create(3, 4, 1, PlayerId.Player1, isCapital: false);
        max.DevelopmentLevel = 3;
        s.Cities.Add(max);

        s = GameSim.Step(s, new List<Commands.Command> {
            new Commands.UpgradeCityCommand(0) { PlayerId = (int)PlayerId.Player2 },
            new Commands.UpgradeCityCommand(1) { PlayerId = (int)PlayerId.Player1 },
            new Commands.UpgradeCityCommand(2) { PlayerId = (int)PlayerId.Player1 },
            new Commands.UpgradeCityCommand(3) { PlayerId = (int)PlayerId.Player1 },
        });

        Assert.False(s.Cities[0].IsUpgrading);
        Assert.False(s.Cities[1].IsUpgrading);
        Assert.False(s.Cities[2].IsUpgrading);
        Assert.False(s.Cities[3].IsUpgrading);
    }

    [Fact]
    public void UpgradeCity_IsMutuallyExclusiveWithProduction()
    {
        var s = BuildEmpty(5, 5);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));
        s.Cities.Add(City.Create(1, 3, 3, PlayerId.Player2, isCapital: true));

        s = GameSim.Step(s, new List<Commands.Command> {
            new Commands.BuildUnitCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player1 },
            new Commands.UpgradeCityCommand(0) { PlayerId = (int)PlayerId.Player1 },
        });
        Assert.True(s.Cities[0].IsProducing);
        Assert.False(s.Cities[0].IsUpgrading);

        s = BuildEmpty(5, 5);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));
        s.Cities.Add(City.Create(1, 3, 3, PlayerId.Player2, isCapital: true));
        s = GameSim.Step(s, new List<Commands.Command> {
            new Commands.UpgradeCityCommand(0) { PlayerId = (int)PlayerId.Player1 },
            new Commands.BuildUnitCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player1 },
        });
        Assert.True(s.Cities[0].IsUpgrading);
        Assert.False(s.Cities[0].IsProducing);
    }

    [Fact]
    public void AutoBuild_ResumesAfterUpgradeCompletes()
    {
        var s = BuildEmpty(5, 5);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));
        s.Cities.Add(City.Create(1, 3, 3, PlayerId.Player2, isCapital: true));
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(60);

        s = GameSim.Step(s, new List<Commands.Command> {
            new Commands.SetAutoBuildCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player1 },
            new Commands.UpgradeCityCommand(0) { PlayerId = (int)PlayerId.Player1 },
        });

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 41 + 1);

        Assert.Equal(2, s.Cities[0].DevelopmentLevel);
        Assert.Equal((byte)((byte)UnitType.Light + 1), s.Cities[0].AutoBuildOrder);
        Assert.Equal((byte)((byte)UnitType.Light + 1), s.Cities[0].ProductionOrder);
    }

    [Fact]
    public void CityCapture_CancelsActiveUpgradeButPreservesCompletedLevel()
    {
        var s = BuildEmpty(5, 5);
        var city = City.Create(0, 2, 2, PlayerId.Player1, isCapital: false);
        city.DevelopmentLevel = 2;
        city.SupplyCapacity = UnitStats.SupplyCapacity(city);
        city.DevelopmentOrder = 3;
        city.DevelopmentProgress = FP.FromInt(20);
        city.CaptureHp = City.CaptureDamageOnTile;
        s.Cities.Add(city);
        s.Units.Add(Unit.Create(0, PlayerId.Player2, UnitType.Heavy, 2, 2));

        CityCapture.Tick(ref s);

        Assert.Equal(PlayerId.Player2, s.Cities[0].Owner);
        Assert.Equal(2, s.Cities[0].DevelopmentLevel);
        Assert.Equal(8, s.Cities[0].SupplyCapacity);
        Assert.False(s.Cities[0].IsUpgrading);
        Assert.Equal(FP.Zero, s.Cities[0].DevelopmentProgress);
    }

    [Fact]
    public void BuildOrder_SpawnsLightAfterTwelveSeconds()
    {
        var s = BuildEmpty(5, 5);
        s.Cities.Add(City.Create(0, 2, 2, PlayerId.Player1, isCapital: false));

        var cmd = new List<Commands.Command> {
            new Commands.BuildUnitCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player1 },
        };
        s = GameSim.Step(s, cmd);

        // City produces 1 ECO/sec, light costs 12 ECO -> ~12 sec.
        s = GameSim.StepN(s, GameSim.TicksPerSecond * 13);
        Assert.NotEmpty(s.Units);
        Assert.Equal(UnitType.Light, s.Units[0].Type);
        Assert.Equal(PlayerId.Player1, s.Units[0].Owner);
    }

    [Fact]
    public void Capital_BuildsHeavyFasterThanCityWould()
    {
        var capState = BuildEmpty(3, 3);
        capState.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: true));
        var cityState = BuildEmpty(3, 3);
        cityState.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));

        var orderCmd = new List<Commands.Command> {
            new Commands.BuildUnitCommand(0, UnitType.Heavy) { PlayerId = (int)PlayerId.Player1 },
        };
        capState = GameSim.Step(capState, orderCmd);
        cityState = GameSim.Step(cityState, orderCmd);

        // After 13 sec, capital (3 ECO/sec) has 39 ECO -> heavy (36) built.
        // City (1 ECO/sec) has 13 -> nothing built.
        capState = GameSim.StepN(capState, GameSim.TicksPerSecond * 13);
        cityState = GameSim.StepN(cityState, GameSim.TicksPerSecond * 13);
        Assert.NotEmpty(capState.Units);
        Assert.Empty(cityState.Units);
    }

    [Fact]
    public void SupplyCeiling_BlocksProductionWhenAtCap()
    {
        // One city has cap 5. Pre-fill with 5 light units (5 supply). Order
        // another light — nothing should spawn. Units placed on the city
        // tile so maintenance doesn't drain ECO / kill them.
        var s = BuildEmpty(5, 5);
        s.Cities.Add(City.Create(0, 2, 2, PlayerId.Player1, isCapital: false));
        for (int i = 0; i < 5; i++)
            s.Units.Add(Unit.Create(i, PlayerId.Player1, UnitType.Light, 2, 2));

        var order = new List<Commands.Command> {
            new Commands.BuildUnitCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player1 },
        };
        s = GameSim.Step(s, order);

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 30);
        // Still only the original 5 units.
        int alive = 0;
        for (int i = 0; i < s.Units.Count; i++) if (s.Units[i].IsAlive) alive++;
        Assert.Equal(5, alive);
    }

    [Fact]
    public void BuildOrder_CancelStopsProduction()
    {
        var s = BuildEmpty(3, 3);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));

        var startCmd = new List<Commands.Command> {
            new Commands.BuildUnitCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player1 },
        };
        s = GameSim.Step(s, startCmd);
        s = GameSim.StepN(s, GameSim.TicksPerSecond * 5);

        var cancelCmd = new List<Commands.Command> {
            new Commands.BuildUnitCommand(0, null) { PlayerId = (int)PlayerId.Player1 },
        };
        s = GameSim.Step(s, cancelCmd);
        s = GameSim.StepN(s, GameSim.TicksPerSecond * 30);
        Assert.Empty(s.Units);
    }

    [Fact]
    public void AutoBuild_RequeuesAfterSpawnAndWaitsForClearTile()
    {
        var s = BuildEmpty(8, 1);
        s.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));

        s = GameSim.Step(s, new List<Commands.Command> {
            new Commands.SetAutoBuildCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player1 },
        });

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 5);
        Assert.Single(s.Units);
        Assert.Equal((byte)((byte)UnitType.Light + 1), s.Cities[0].ProductionOrder);
        Assert.Equal((byte)((byte)UnitType.Light + 1), s.Cities[0].AutoBuildOrder);

        // The second unit should not spawn on top of the first.
        s = GameSim.StepN(s, GameSim.TicksPerSecond * 8);
        Assert.Single(s.Units);

        s = GameSim.Step(s, new List<Commands.Command> {
            new Commands.MoveUnitCommand(0, 4, 0) { PlayerId = (int)PlayerId.Player1 },
        });
        s = GameSim.StepN(s, GameSim.TicksPerSecond * 3);

        Assert.True(s.Units.Count >= 2,
            $"expected autobuild to spawn a second unit after the city cleared; got {s.Units.Count}");
    }

    [Fact]
    public void AutoBuild_RejectedFromWrongPlayerAndFort()
    {
        var s = BuildEmpty(3, 3);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));

        s = GameSim.Step(s, new List<Commands.Command> {
            new Commands.SetAutoBuildCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player2 },
        });
        Assert.Equal(0, s.Cities[0].AutoBuildOrder);

        s.Map.SetTile(1, 1, TileType.Fort);
        s = GameSim.Step(s, new List<Commands.Command> {
            new Commands.SetAutoBuildCommand(0, UnitType.Heavy) { PlayerId = (int)PlayerId.Player1 },
        });
        Assert.Equal(0, s.Cities[0].AutoBuildOrder);
    }

    [Fact]
    public void BuildOrder_RejectedFromWrongPlayer()
    {
        var s = BuildEmpty(3, 3);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));

        // Player 2 trying to order from Player 1's city.
        var cmd = new List<Commands.Command> {
            new Commands.BuildUnitCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player2 },
        };
        s = GameSim.Step(s, cmd);
        Assert.False(s.Cities[0].IsProducing);
    }

    [Fact]
    public void RenameCity_SanitizesAndStoresOwnedCityName()
    {
        var s = BuildEmpty(3, 3);
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));

        var cmd = new List<Commands.Command> {
            new Commands.RenameCityCommand(0, "North-Bridge Outpost With Extra Text") { PlayerId = (int)PlayerId.Player1 },
        };
        s = GameSim.Step(s, cmd);

        Assert.Equal("North-Bridge Outpost Wit", s.Cities[0].Name);
        Assert.Equal("North-Bridge Outpost Wit", s.Cities[0].DisplayName);
    }

    [Fact]
    public void RenameCity_EmptyNameResetsDefault()
    {
        var s = BuildEmpty(3, 3);
        var city = City.Create(0, 1, 1, PlayerId.Player1, isCapital: false);
        city.Name = "Old Name";
        s.Cities.Add(city);

        var cmd = new List<Commands.Command> {
            new Commands.RenameCityCommand(0, "   ") { PlayerId = (int)PlayerId.Player1 },
        };
        s = GameSim.Step(s, cmd);

        Assert.Null(s.Cities[0].Name);
        Assert.Equal("City 1", s.Cities[0].DisplayName);
    }

    [Fact]
    public void RenameCity_RejectsWrongOwnerAndForts()
    {
        var wrongOwner = BuildEmpty(3, 3);
        wrongOwner.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));
        wrongOwner = GameSim.Step(wrongOwner, new List<Commands.Command> {
            new Commands.RenameCityCommand(0, "Enemy Edit") { PlayerId = (int)PlayerId.Player2 },
        });
        Assert.Null(wrongOwner.Cities[0].Name);

        var fort = BuildEmpty(3, 3);
        fort.Map.SetTile(1, 1, TileType.Fort);
        fort.Cities.Add(new City
        {
            Id = 0,
            TileX = 1,
            TileY = 1,
            Owner = PlayerId.Player1,
            OriginalOwner = PlayerId.Player1,
            SupplyCapacity = FortConstruction.FortSupplyCapacity,
            CaptureHp = FortConstruction.FortCaptureHp,
        });
        fort = GameSim.Step(fort, new List<Commands.Command> {
            new Commands.RenameCityCommand(0, "Fort Edit") { PlayerId = (int)PlayerId.Player1 },
        });
        Assert.Null(fort.Cities[0].Name);
    }

    [Fact]
    public void Fort_DoesNotAccrueEco()
    {
        var s = BuildEmpty(3, 3);
        s.Map.SetTile(1, 1, TileType.Fort);
        s.Cities.Add(new City
        {
            Id = 0,
            TileX = 1,
            TileY = 1,
            Owner = PlayerId.Player1,
            OriginalOwner = PlayerId.Player1,
            IsCapital = false,
            SupplyCapacity = FortConstruction.FortSupplyCapacity,
            CaptureHp = FortConstruction.FortCaptureHp,
        });

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 5);

        Assert.Equal(FP.Zero, s.Players[(int)PlayerId.Player1].Eco);
    }

    [Fact]
    public void BuildOrder_RejectedFromFort()
    {
        var s = BuildEmpty(3, 3);
        s.Map.SetTile(1, 1, TileType.Fort);
        s.Cities.Add(new City
        {
            Id = 0,
            TileX = 1,
            TileY = 1,
            Owner = PlayerId.Player1,
            OriginalOwner = PlayerId.Player1,
            IsCapital = false,
            SupplyCapacity = FortConstruction.FortSupplyCapacity,
            CaptureHp = FortConstruction.FortCaptureHp,
        });

        var cmd = new List<Commands.Command> {
            new Commands.BuildUnitCommand(0, UnitType.Light) { PlayerId = (int)PlayerId.Player1 },
        };
        s = GameSim.Step(s, cmd);

        Assert.False(s.Cities[0].IsProducing);
    }
}
