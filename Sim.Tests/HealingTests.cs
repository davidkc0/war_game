using WarGame.Sim;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using Xunit;

namespace WarGame.Sim.Tests;

public class HealingTests
{
    private static GameState Build(int w = 10)
    {
        var s = GameState.Initial(seed: 1);
        s.Map = new MapState.Builder(w, 1).Build();
        s.TileOwner = new byte[w];
        s.Cities.Add(City.Create(0, 0, 0, PlayerId.Player1, isCapital: true));
        return s;
    }

    [Fact]
    public void WoundedUnitOnFriendlyCity_HealsOverTime()
    {
        var s = Build();
        // Hp=10 (well below 60 max), parked on the capital.
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0);
        u.Hp = FP.FromInt(10);
        s.Units.Add(u);
        // Give the player some ECO so healing isn't blocked on cost.
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(50);

        FP before = s.Units[0].Hp;
        s = GameSim.StepN(s, GameSim.TicksPerSecond * 2);  // 2 sec
        FP after = s.Units[0].Hp;
        Assert.True(after > before, $"unit didn't heal: {before} -> {after}");
    }

    [Fact]
    public void HealingDrainsEco()
    {
        var s = Build();
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0);
        u.Hp = FP.FromInt(10);
        s.Units.Add(u);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(50);
        FP ecoBefore = s.Players[(int)PlayerId.Player1].Eco;

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 2);
        FP ecoAfter = s.Players[(int)PlayerId.Player1].Eco;
        // The capital adds ~6 ECO over 2s while healing drains ~3 ECO and
        // builds nothing. Net change should still be tractable to assert.
        // We assert that less ECO accrued than the bare-capital baseline.
        // Bare capital = 3 * 2 = 6 ECO over 2 sec (capital rate).
        Assert.True(ecoAfter - ecoBefore < FP.FromInt(6),
            $"healing didn't drain eco: gained {ecoAfter - ecoBefore}");
    }

    [Fact]
    public void NoEco_HealingStops()
    {
        var s = Build();
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0);
        u.Hp = FP.FromInt(10);
        s.Units.Add(u);
        // Zero ECO and a non-capital city so income is also slow.
        s.Cities[0] = City.Create(0, 0, 0, PlayerId.Player1, isCapital: false);
        s.Players[(int)PlayerId.Player1].Eco = FP.Zero;

        // Run for half a second. ECO accumulates ~0.5 from the city, healing
        // burns ~0.05/tick = 1.5/s. Healing happens but very slowly.
        FP before = s.Units[0].Hp;
        s = GameSim.StepN(s, 5);
        FP after = s.Units[0].Hp;
        // Either no heal or a very small amount — assert progress is bounded.
        Assert.True(after - before < FP.FromInt(2),
            $"healed too fast on starvation budget: {before} -> {after}");
    }

    [Fact]
    public void UnitNotOnCity_DoesNotHeal()
    {
        var s = Build();
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0);  // far from city
        u.Hp = FP.FromInt(10);
        s.Units.Add(u);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(50);

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 5);
        Assert.Equal(FP.FromInt(10), s.Units[0].Hp);
    }

    [Fact]
    public void UnitOnEnemyCity_DoesNotHeal()
    {
        // Test the rule in isolation by calling Healing.Tick directly —
        // avoids GameSim.Step running PowerProjection which can flip the
        // city to P1 in this contrived setup.
        var s = Build();
        s.Cities.Add(City.Create(1, 5, 0, PlayerId.Player2, isCapital: false));
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 5, 0);
        u.Hp = FP.FromInt(10);
        s.Units.Add(u);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(50);

        for (int i = 0; i < 100; i++)
            WarGame.Sim.Systems.Healing.Tick(ref s);

        Assert.Equal(FP.FromInt(10), s.Units[0].Hp);
    }

    [Fact]
    public void HealingCapsAtMaxHp()
    {
        var s = Build();
        var u = Unit.Create(0, PlayerId.Player1, UnitType.Light, 0, 0);
        u.Hp = UnitStats.LightMaxHp - FP.One;   // 1 HP missing
        s.Units.Add(u);
        s.Players[(int)PlayerId.Player1].Eco = FP.FromInt(50);

        s = GameSim.StepN(s, GameSim.TicksPerSecond * 10);
        Assert.Equal(UnitStats.LightMaxHp, s.Units[0].Hp);
    }
}
