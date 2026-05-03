using WarGame.Sim.State;
using WarGame.Sim.Systems;
using Xunit;

namespace WarGame.Sim.Tests;

public class CityCaptureTests
{
    [Fact]
    public void UnitsOnBroadWater_DoNotCaptureAdjacentCities()
    {
        var s = GameState.Initial(seed: 11);
        var b = new MapState.Builder(3, 3, TileType.Plains);
        b.Set(1, 1, TileType.City);
        b.Set(0, 0, TileType.Water);
        b.Set(1, 0, TileType.Water);
        b.Set(2, 0, TileType.Water);
        s.Map = b.Build();
        s.Cities.Add(City.Create(0, 1, 1, PlayerId.Player1, isCapital: false));
        s.Units.Add(Unit.Create(0, PlayerId.Player2, UnitType.Light, 1, 0));

        CityCapture.Tick(ref s);

        Assert.Equal(City.CityMaxCaptureHp, s.Cities[0].CaptureHp);
        Assert.Equal(PlayerId.Player1, s.Cities[0].Owner);
    }
}
