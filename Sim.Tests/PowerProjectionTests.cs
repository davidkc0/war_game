using WarGame.Sim;
using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class PowerProjectionTests
{
    private static GameState Empty(int w, int h)
    {
        var s = GameState.Initial(seed: 1);
        s.Map = new MapState.Builder(w, h).Build();
        s.TileOwner = new byte[w * h];
        return s;
    }

    private static PlayerId Owner(GameState s, int x, int y)
        => (PlayerId)s.TileOwner[y * s.Map.Width + x];

    [Fact]
    public void EmptyState_AllTilesUnowned()
    {
        var s = Empty(20, 20);
        s = GameSim.Step(s, null);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++)
                Assert.Equal(PlayerId.None, Owner(s, x, y));
    }

    [Fact]
    public void SingleUnit_OwnsLocalArea()
    {
        var s = Empty(20, 20);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 10, 10));
        s = GameSim.Step(s, null);

        // The unit's tile is unambiguously theirs.
        Assert.Equal(PlayerId.Player1, Owner(s, 10, 10));
        // Tiles within the radius are theirs.
        Assert.Equal(PlayerId.Player1, Owner(s, 12, 10));
        // Far-away tiles are unowned.
        Assert.Equal(PlayerId.None, Owner(s, 0, 0));
    }

    [Fact]
    public void OpposingUnits_CreateContestedSeam()
    {
        // Two same-type units symmetric about (5, 0): at (3, 0) and (7, 0).
        // Both contribute equal influence (10*(5-2)/5 = 6) to (5, 0) — exact
        // tie, which the projection rule resolves to PlayerId.None.
        var s = Empty(11, 1);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 3, 0));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 7, 0));
        s = GameSim.Step(s, null);

        Assert.Equal(PlayerId.Player1, Owner(s, 3, 0));
        Assert.Equal(PlayerId.Player2, Owner(s, 7, 0));
        Assert.Equal(PlayerId.None,    Owner(s, 5, 0));
    }

    [Fact]
    public void Capital_OwnsMoreLandThanCity()
    {
        // Use a wide map so the capital's bigger radius matters. Two
        // identical setups except the central source: city vs capital.
        // The capital should own at least as many tiles as the city, and
        // strictly more for some realistic configurations.
        int CountOwned(City source)
        {
            var s = Empty(40, 1);
            s.Cities.Add(source);
            s = GameSim.Step(s, null);
            int count = 0;
            for (int x = 0; x < 40; x++)
                if (Owner(s, x, 0) == source.Owner) count++;
            return count;
        }
        var city    = City.Create(0, 20, 0, PlayerId.Player1, isCapital: false);
        var capital = City.Create(0, 20, 0, PlayerId.Player1, isCapital: true);
        Assert.True(CountOwned(capital) > CountOwned(city));
    }

    [Fact]
    public void City_FlipsOwnershipWhenSurrounded()
    {
        // Player 2 city at (5,0). Player 1 piles 5 heavy units onto the
        // city tile itself. With CityCapture: 5 units * 3 damage/tick on
        // tile = 15 damage/tick. CityMaxCaptureHp = 100. Flip at tick ~7.
        var s = Empty(20, 1);
        s.Cities.Add(City.Create(0, 5, 0, PlayerId.Player2, isCapital: false));
        for (int i = 0; i < 5; i++)
            s.Units.Add(Unit.Create(i, PlayerId.Player1, UnitType.Heavy, 5, 0));

        // Run enough ticks for the capture HP to deplete.
        s = GameSim.StepN(s, 30);

        Assert.Equal(PlayerId.Player1, s.Cities[0].Owner);
    }

    [Fact]
    public void DeadUnits_DontProject()
    {
        var s = Empty(20, 1);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 10, 0));
        // Manually 0-out HP — same effect as combat would produce.
        var u = s.Units[0];
        u.Hp = WarGame.Sim.Math.FP.Zero;
        s.Units[0] = u;

        s = GameSim.Step(s, null);
        Assert.Equal(PlayerId.None, Owner(s, 10, 0));
    }

    [Fact]
    public void Determinism_SameStateSameField()
    {
        // Two identical sims should produce identical TileOwner arrays.
        GameState Build()
        {
            var x = Empty(20, 20);
            x.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 3, 3));
            x.Units.Add(Unit.Create(1, PlayerId.Player1, UnitType.Heavy, 4, 4));
            x.Units.Add(Unit.Create(2, PlayerId.Player2, UnitType.Light, 16, 16));
            x.Cities.Add(City.Create(0, 5, 5, PlayerId.Player1, isCapital: true));
            x.Cities.Add(City.Create(1, 14, 14, PlayerId.Player2, isCapital: false));
            return x;
        }
        var a = Build(); var b = Build();
        a = GameSim.Step(a, null);
        b = GameSim.Step(b, null);
        Assert.Equal(a.TileOwner, b.TileOwner);
    }

    [Fact]
    public void LightFalloff_HitsZeroBeyondRadius()
    {
        // A light unit at (10, 10) with radius 5: tile (16, 10) is distance
        // 6 — strictly outside. Should not flip ownership.
        var s = Empty(30, 1);
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 10, 0));
        s = GameSim.Step(s, null);
        Assert.Equal(PlayerId.None, Owner(s, 16, 0));
        Assert.Equal(PlayerId.Player1, Owner(s, 14, 0));   // distance 4 — inside
    }
}
