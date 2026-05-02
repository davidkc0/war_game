using System.Collections.Generic;
using WarGame.Sim.Generation;
using WarGame.Sim.State;
using Xunit;

namespace WarGame.Sim.Tests;

public class BalanceValidatorTests
{
    [Fact]
    public void Score_GeneratedMaps_TypicallyAccepted()
    {
        // Sample many seeds. Most should pass the validator on attempt 1.
        // The retry loop is for the few seeds that don't.
        int accepted = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            var r = MapGenerator.GenerateOnce((ulong)seed);
            var score = BalanceValidator.Score(r.Map, r.Cities);
            if (score.Accepted) accepted++;
        }
        Assert.True(accepted >= 20,
            $"only {accepted}/30 seeds accepted on first try — generator may be regressing");
    }

    [Fact]
    public void Score_IsolatedCity_RejectedForConnectivity()
    {
        // Hand-build a map with two cities separated by an impassable wall.
        var b = new MapState.Builder(20, 5);
        for (int y = 0; y < 5; y++) b.Set(10, y, TileType.Water);
        var map = b.Build();
        var cities = new List<City>
        {
            City.Create(0,  3, 2, PlayerId.Player1, isCapital: true),
            City.Create(1, 17, 2, PlayerId.Player2, isCapital: true),
        };

        var score = BalanceValidator.Score(map, cities);
        Assert.False(score.Accepted);
        Assert.Equal("city unreachable", score.RejectReason);
    }

    [Fact]
    public void Score_OpenMap_HasFullPathAndConnectivity()
    {
        // 30x30 plains with two opposing capitals and two neutral cities.
        var b = new MapState.Builder(30, 30);
        var map = b.Build();
        var cities = new List<City>
        {
            City.Create(0,  3,  3, PlayerId.Player1, isCapital: true),
            City.Create(1, 26, 26, PlayerId.Player2, isCapital: true),
            City.Create(2, 12, 15, PlayerId.None,    isCapital: false),
            City.Create(3, 18, 15, PlayerId.None,    isCapital: false),
        };
        var score = BalanceValidator.Score(map, cities);
        Assert.Equal(100, score.Connectivity);
        // Symmetric layout → near-100 path symmetry.
        Assert.True(score.PathSymmetry >= 80,
            $"symmetric map only scored {score.PathSymmetry} on path");
    }

    [Fact]
    public void Score_AsymmetricCapitalDistances_LowerPathScore()
    {
        // Capital 1 right next to a neutral city; capital 2 far away.
        // Path symmetry should drop hard.
        var b = new MapState.Builder(30, 5);
        var map = b.Build();
        var cities = new List<City>
        {
            City.Create(0,  2, 2, PlayerId.Player1, isCapital: true),
            City.Create(1, 27, 2, PlayerId.Player2, isCapital: true),
            City.Create(2,  5, 2, PlayerId.None,    isCapital: false),  // close to cap1
        };
        var score = BalanceValidator.Score(map, cities);
        Assert.True(score.PathSymmetry < 80,
            $"asymmetric layout still scored {score.PathSymmetry} on path");
    }

    [Fact]
    public void Score_NoCapitalCount_StillScoresConnectivity()
    {
        // Even without capitals (which the rest of the validator falls back
        // gracefully on), connectivity should still be evaluable.
        var b = new MapState.Builder(10, 10);
        var map = b.Build();
        var cities = new List<City>
        {
            City.Create(0, 1, 1, PlayerId.Player1, isCapital: false),
            City.Create(1, 8, 8, PlayerId.Player2, isCapital: false),
        };
        var score = BalanceValidator.Score(map, cities);
        Assert.True(score.Connectivity == 100);
    }
}

public class MapGeneratorRetryTests
{
    [Fact]
    public void Generate_AttachesAttemptCount()
    {
        var r = MapGenerator.Generate(42);
        Assert.True(r.AttemptsUsed >= 1);
        Assert.True(r.AttemptsUsed <= MapGenerator.MaxRetries);
    }

    [Fact]
    public void Generate_TypicallyAcceptedOnFirstAttempt()
    {
        int firstTry = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            var r = MapGenerator.Generate((ulong)seed);
            if (r.AttemptsUsed == 1) firstTry++;
        }
        Assert.True(firstTry >= 20,
            $"only {firstTry}/30 seeds accepted on first attempt — retry budget burning too fast");
    }

    [Fact]
    public void Generate_AcceptedResults_HaveAcceptedScore()
    {
        // Spot-check that any accepted result is actually above threshold.
        var r = MapGenerator.Generate(0);
        if (r.LastScore.Accepted)
            Assert.True(r.LastScore.Total >= BalanceValidator.DefaultAcceptanceThreshold);
    }

    [Fact]
    public void Generate_DeterminismHoldsThroughRetries()
    {
        // Same seed → same retry trail → same final map. This is the
        // critical guarantee for replays/lockstep.
        var a = MapGenerator.Generate(7);
        var b = MapGenerator.Generate(7);
        Assert.Equal(a.AttemptsUsed, b.AttemptsUsed);
        Assert.Equal(a.AcceptedSeed, b.AcceptedSeed);
        for (int i = 0; i < a.Map.TileCount; i++)
            Assert.Equal(a.Map.RawTiles[i], b.Map.RawTiles[i]);
    }
}
