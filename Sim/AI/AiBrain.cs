using System.Collections.Generic;
using WarGame.Sim.Commands;
using WarGame.Sim.Math;
using WarGame.Sim.State;
using WarGame.Sim.Systems;

namespace WarGame.Sim.AI;

// Deterministic single-player AI. This is deliberately heuristic: it reads
// the same fog-filtered information a human player can act on, then emits
// normal commands. The sim remains the only authority for whether a command
// is legal.
public static class AiBrain
{
    private const int TacticalInterval = 10;
    private const int OperationalInterval = 30;
    private const int StrategicInterval = 90;
    private static readonly int[] CardinalDx = { 0, 1, 0, -1 };
    private static readonly int[] CardinalDy = { -1, 0, 1, 0 };

    private static readonly byte[] LightPromotionPriority =
    {
        (byte)UnitPerk.LightOptics,
        (byte)UnitPerk.LightPathfinder,
        (byte)UnitPerk.LightQuickMarch,
        (byte)UnitPerk.LightPackTactics,
        (byte)UnitPerk.LightScreenLine,
        (byte)UnitPerk.LightRoadRunner,
    };

    private static readonly byte[] HeavyPromotionPriority =
    {
        (byte)UnitPerk.HeavyGunnery,
        (byte)UnitPerk.HeavyPlating,
        (byte)UnitPerk.HeavyHullDown,
        (byte)UnitPerk.HeavyBreacher,
        (byte)UnitPerk.HeavyStabilizers,
        (byte)UnitPerk.HeavySpotterCrew,
    };

    public static List<Command> Decide(in GameState state, PlayerId aiPlayer, AiDifficulty difficulty)
    {
        var commands = new List<Command>(MaxCommands(difficulty));
        if (aiPlayer is not (PlayerId.Player1 or PlayerId.Player2)) return commands;
        if (state.Winner != PlayerId.None) return commands;
        if (state.Map.Width <= 0 || state.Map.Height <= 0) return commands;

        int max = MaxCommands(difficulty);
        bool[] unitCommanded = new bool[state.Units.Count];
        bool[] cityCommanded = new bool[state.Cities.Count];

        AddPromotionCommands(state, aiPlayer, commands, max);

        if (OnCadence(state.Tick, OperationalInterval, difficulty))
            AddProductionCommands(state, aiPlayer, difficulty, commands, max, cityCommanded);

        if (OnCadence(state.Tick, TacticalInterval, difficulty))
        {
            AddRetreatCommands(state, aiPlayer, difficulty, commands, max, unitCommanded);
            AddVisibleEnemyCommands(state, aiPlayer, difficulty, commands, max, unitCommanded);
        }

        if (OnCadence(state.Tick, OperationalInterval, difficulty))
        {
            AddSupplyInterdictionCommands(state, aiPlayer, difficulty, commands, max, unitCommanded);
            AddCaptureCommands(state, aiPlayer, difficulty, commands, max, unitCommanded);
            AddExploreCommands(state, aiPlayer, difficulty, commands, max, unitCommanded);
        }

        if (OnCadence(state.Tick, StrategicInterval, difficulty))
        {
            AddFortCommands(state, aiPlayer, difficulty, commands, max);
            AddRoadCommands(state, aiPlayer, difficulty, commands, max, unitCommanded);
        }

        return commands;
    }

    private static bool OnCadence(int tick, int interval, AiDifficulty difficulty)
    {
        int actual = difficulty == AiDifficulty.Easy ? interval * 2 : interval;
        return tick % actual == 0;
    }

    private static int MaxCommands(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => 8,
        AiDifficulty.Hard => 16,
        _ => 12,
    };

    private static bool TryAdd(List<Command> commands, int max, Command command)
    {
        if (commands.Count >= max) return false;
        commands.Add(command);
        return true;
    }

    private static void AddPromotionCommands(
        in GameState s,
        PlayerId ai,
        List<Command> commands,
        int max)
    {
        for (int i = 0; i < s.Units.Count && commands.Count < max; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != ai || u.PromotionPoints == 0) continue;
            byte[] priority = u.Type == UnitType.Heavy ? HeavyPromotionPriority : LightPromotionPriority;
            for (int p = 0; p < priority.Length; p++)
            {
                byte perk = priority[p];
                if (UnitProgression.HasPerk(u, (UnitPerk)perk)) continue;
                TryAdd(commands, max, new ChoosePromotionCommand(i, perk) { PlayerId = (int)ai });
                break;
            }
        }
    }

    private static void AddProductionCommands(
        in GameState s,
        PlayerId ai,
        AiDifficulty difficulty,
        List<Command> commands,
        int max,
        bool[] cityCommanded)
    {
        CountUnits(s, ai, out int light, out int heavy);
        int total = light + heavy;
        CountSupply(s, ai, out int supplyUsed, out int supplyCap);
        int activeUpgrades = CountActiveUpgrades(s, ai);
        int maxActiveUpgrades = MaxActiveUpgrades(s, ai, difficulty);

        for (int i = 0; i < s.Cities.Count && commands.Count < max; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != ai || c.IsProducing || c.IsUpgrading) continue;
            if (s.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile()) continue;

            if (ShouldUpgradeCity(s, ai, difficulty, c, total, supplyUsed, supplyCap, activeUpgrades, maxActiveUpgrades))
            {
                if (TryAdd(commands, max, new UpgradeCityCommand(i) { PlayerId = (int)ai }))
                {
                    cityCommanded[i] = true;
                    activeUpgrades++;
                }
                continue;
            }

            UnitType type;
            if (light < OpeningLightTarget(difficulty) || light <= heavy)
                type = UnitType.Light;
            else if (total >= HeavyUnlockCount(difficulty))
                type = UnitType.Heavy;
            else
                type = UnitType.Light;

            if (TryAdd(commands, max, new BuildUnitCommand(i, type) { PlayerId = (int)ai }))
                cityCommanded[i] = true;

            if (type == UnitType.Light) light++;
            else heavy++;
            total++;
        }
    }

    private static int OpeningLightTarget(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => 5,
        AiDifficulty.Hard => 3,
        _ => 4,
    };

    private static int HeavyUnlockCount(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => 8,
        AiDifficulty.Hard => 4,
        _ => 5,
    };

    private static bool ShouldUpgradeCity(
        in GameState s,
        PlayerId ai,
        AiDifficulty difficulty,
        in City city,
        int totalUnits,
        int supplyUsed,
        int supplyCap,
        int activeUpgrades,
        int maxActiveUpgrades)
    {
        if (activeUpgrades >= maxActiveUpgrades) return false;

        byte level = UnitStats.NormalizeDevelopmentLevel(city.DevelopmentLevel);
        int cost = UnitStats.UpgradeCost(level);
        if (cost <= 0) return false;
        if (VisibleEnemyNear(s, ai, city.TileX, city.TileY, UpgradeThreatRadius(difficulty))) return false;

        bool supplyPressure = supplyCap > 0 && supplyUsed * 100 >= supplyCap * SupplyPressurePercent(difficulty);
        int minUnits = supplyPressure
            ? PressureUpgradeMinUnits(difficulty, city.IsCapital)
            : StockpileUpgradeMinUnits(difficulty, city.IsCapital);
        if (totalUnits < minUnits) return false;

        int reserve = supplyPressure
            ? PressureUpgradeReserve(difficulty)
            : StockpileUpgradeReserve(difficulty);
        return s.Players[(int)ai].Eco >= FP.FromInt(cost + reserve);
    }

    private static int SupplyPressurePercent(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => 95,
        AiDifficulty.Hard => 75,
        _ => 85,
    };

    private static int PressureUpgradeMinUnits(AiDifficulty difficulty, bool capital) => difficulty switch
    {
        AiDifficulty.Easy => capital ? 7 : 9,
        AiDifficulty.Hard => capital ? 3 : 4,
        _ => capital ? 5 : 6,
    };

    private static int StockpileUpgradeMinUnits(AiDifficulty difficulty, bool capital) => difficulty switch
    {
        AiDifficulty.Easy => capital ? 10 : 12,
        AiDifficulty.Hard => capital ? 4 : 6,
        _ => capital ? 6 : 8,
    };

    private static int PressureUpgradeReserve(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => 45,
        AiDifficulty.Hard => 8,
        _ => 18,
    };

    private static int StockpileUpgradeReserve(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => 75,
        AiDifficulty.Hard => 24,
        _ => 45,
    };

    private static int UpgradeThreatRadius(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => 5,
        AiDifficulty.Hard => 3,
        _ => 4,
    };

    private static int MaxActiveUpgrades(in GameState s, PlayerId ai, AiDifficulty difficulty)
    {
        int realCities = CountRealCities(s, ai);
        if (realCities <= 0) return 0;
        return difficulty switch
        {
            AiDifficulty.Easy => 1,
            AiDifficulty.Hard => realCities >= 3 ? 2 : 1,
            _ => realCities >= 4 ? 2 : 1,
        };
    }

    private static int CountActiveUpgrades(in GameState s, PlayerId ai)
    {
        int count = 0;
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != ai || !c.IsUpgrading) continue;
            if (s.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile()) continue;
            count++;
        }
        return count;
    }

    private static int CountRealCities(in GameState s, PlayerId ai)
    {
        int count = 0;
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != ai) continue;
            if (s.Map.GetTileUnchecked(c.TileX, c.TileY).IsFortTile()) continue;
            count++;
        }
        return count;
    }

    private static void AddRetreatCommands(
        in GameState s,
        PlayerId ai,
        AiDifficulty difficulty,
        List<Command> commands,
        int max,
        bool[] unitCommanded)
    {
        int threshold = difficulty switch
        {
            AiDifficulty.Easy => 30,
            AiDifficulty.Hard => 58,
            _ => 45,
        };

        for (int i = 0; i < s.Units.Count && commands.Count < max; i++)
        {
            if (unitCommanded[i]) continue;
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != ai) continue;
            if (!IsHpBelowPercent(u, threshold)) continue;
            if (!TryNearestOwnedShelter(s, ai, u, out int tx, out int ty)) continue;
            TryAddMove(s, ai, i, tx, ty, commands, max, unitCommanded);
        }
    }

    private static void AddVisibleEnemyCommands(
        in GameState s,
        PlayerId ai,
        AiDifficulty difficulty,
        List<Command> commands,
        int max,
        bool[] unitCommanded)
    {
        for (int i = 0; i < s.Units.Count && commands.Count < max; i++)
        {
            if (unitCommanded[i]) continue;
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != ai) continue;
            if (!TryBestVisibleEnemyApproach(s, ai, u, out int tx, out int ty)) continue;
            TryAddMove(s, ai, i, tx, ty, commands, max, unitCommanded);
        }
    }

    private static void AddSupplyInterdictionCommands(
        in GameState s,
        PlayerId ai,
        AiDifficulty difficulty,
        List<Command> commands,
        int max,
        bool[] unitCommanded)
    {
        for (int i = 0; i < s.Units.Count && commands.Count < max; i++)
        {
            if (unitCommanded[i]) continue;
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != ai || u.Type != UnitType.Light) continue;
            if (!TryBestVisibleEnemyRoad(s, ai, u, out int tx, out int ty)) continue;
            TryAddMove(s, ai, i, tx, ty, commands, max, unitCommanded);
        }
    }

    private static void AddCaptureCommands(
        in GameState s,
        PlayerId ai,
        AiDifficulty difficulty,
        List<Command> commands,
        int max,
        bool[] unitCommanded)
    {
        for (int i = 0; i < s.Units.Count && commands.Count < max; i++)
        {
            if (unitCommanded[i]) continue;
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != ai) continue;
            if (SupplyLines.GetUnitStatus(s, i) == SupplyStatus.CutOff) continue;
            if (!TryBestKnownCityTarget(s, ai, u, out int tx, out int ty)) continue;
            TryAddMove(s, ai, i, tx, ty, commands, max, unitCommanded);
        }
    }

    private static void AddExploreCommands(
        in GameState s,
        PlayerId ai,
        AiDifficulty difficulty,
        List<Command> commands,
        int max,
        bool[] unitCommanded)
    {
        int explorers = 0;
        int maxExplorers = difficulty == AiDifficulty.Hard ? 3 : 2;

        for (int pass = 0; pass < 2 && commands.Count < max; pass++)
        {
            for (int i = 0; i < s.Units.Count && commands.Count < max; i++)
            {
                if (unitCommanded[i]) continue;
                Unit u = s.Units[i];
                if (!u.IsAlive || u.Owner != ai) continue;
                if (pass == 0 && u.Type != UnitType.Light) continue;
                if (u.Path is { Count: > 0 }) continue;
                if (!TryBestExploreTarget(s, ai, u, out int tx, out int ty)) continue;
                if (TryAddMove(s, ai, i, tx, ty, commands, max, unitCommanded))
                    explorers++;
                if (explorers >= maxExplorers) return;
            }
        }
    }

    private static void AddFortCommands(
        in GameState s,
        PlayerId ai,
        AiDifficulty difficulty,
        List<Command> commands,
        int max)
    {
        if (commands.Count >= max) return;
        if (s.Players[(int)ai].Eco < FP.FromInt(FortConstruction.FortEcoCost)) return;
        if (FortConstruction.CountPlayerForts(s, ai) + FortConstruction.CountPendingForts(s, ai)
            >= FortConstruction.MaxFortsPerPlayer)
            return;

        int bestScore = int.MinValue;
        int bestIdx = -1;
        for (int y = 0; y < s.Map.Height; y++)
        {
            for (int x = 0; x < s.Map.Width; x++)
            {
                int idx = y * s.Map.Width + x;
                if (!FogOfWar.IsVisible(s, ai, x, y)) continue;
                if (s.Map.GetTileUnchecked(x, y) != TileType.Plains) continue;
                if (!IsOwnedBy(s, ai, x, y)) continue;
                if (HasPendingFortAt(s, x, y)) continue;
                if (NearOwnedFort(s, ai, x, y, 4)) continue;

                int score = FortScore(s, ai, x, y);
                if (score > bestScore || (score == bestScore && idx < bestIdx))
                {
                    bestScore = score;
                    bestIdx = idx;
                }
            }
        }

        if (bestIdx < 0 || bestScore <= 0) return;
        TryAdd(commands, max, new BuildFortCommand(bestIdx % s.Map.Width, bestIdx / s.Map.Width)
        {
            PlayerId = (int)ai
        });
    }

    private static void AddRoadCommands(
        in GameState s,
        PlayerId ai,
        AiDifficulty difficulty,
        List<Command> commands,
        int max,
        bool[] unitCommanded)
    {
        for (int i = 0; i < s.Units.Count && commands.Count < max; i++)
        {
            if (unitCommanded[i]) continue;
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != ai) continue;
            if (u.Path is { Count: > 0 }) continue;
            if (HasPendingRoadForUnit(s, i)) continue;
            if (TerrainRules.IsBroadWater(s.Map, u.TileX, u.TileY)) continue;
            if (!FogOfWar.IsVisible(s, ai, u.TileX, u.TileY)) continue;
            if (!TryBestRoadTarget(s, ai, u, out int tx, out int ty)) continue;

            List<int> path = Pathfinding.FindRoadBuildPath(s.Map, u.TileX, u.TileY, tx, ty);
            if (path.Count == 0 || !RoadPathFullyVisible(s, ai, path)) continue;
            if (TryAdd(commands, max, new BuildRoadCommand(i, tx, ty) { PlayerId = (int)ai }))
            {
                unitCommanded[i] = true;
                return;
            }
        }
    }

    private static bool TryAddMove(
        in GameState s,
        PlayerId ai,
        int unitId,
        int tx,
        int ty,
        List<Command> commands,
        int max,
        bool[] unitCommanded)
    {
        if (commands.Count >= max) return false;
        if ((uint)unitId >= (uint)s.Units.Count) return false;
        Unit u = s.Units[unitId];
        if (!u.IsAlive || u.Owner != ai) return false;
        if (!s.Map.InBounds(tx, ty)) return false;
        if (!FogOfWar.IsKnown(s, ai, tx, ty)) return false;
        if (AlreadyTargeting(s, u, tx, ty)) return false;

        bool isHeavy = u.Type == UnitType.Heavy;
        if (!s.Map.GetTileUnchecked(tx, ty).IsPassable(isHeavy)) return false;
        List<int> path = Pathfinding.FindPath(s.Map, u.TileX, u.TileY, tx, ty, isHeavy);
        if (path.Count == 0 && (u.TileX != tx || u.TileY != ty)) return false;

        if (!TryAdd(commands, max, new MoveUnitCommand(unitId, tx, ty) { PlayerId = (int)ai }))
            return false;

        unitCommanded[unitId] = true;
        return true;
    }

    private static bool TryBestVisibleEnemyApproach(
        in GameState s,
        PlayerId ai,
        in Unit unit,
        out int tx,
        out int ty)
    {
        tx = ty = -1;
        int bestScore = int.MaxValue;

        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit enemy = s.Units[i];
            if (!enemy.IsAlive || enemy.Owner == ai) continue;
            if (!FogOfWar.IsVisible(s, ai, enemy.TileX, enemy.TileY)) continue;

            int dist = Manhattan(unit.TileX, unit.TileY, enemy.TileX, enemy.TileY);
            if (dist <= 1) continue;

            if (!TryApproachTile(s, ai, unit, enemy.TileX, enemy.TileY, out int ax, out int ay, out int pathLen))
                continue;

            int hp = enemy.Hp.ToInt();
            int score = pathLen * 100 + hp + (enemy.Type == UnitType.Heavy ? -20 : 0);
            if (score < bestScore || (score == bestScore && TileIndex(s, ax, ay) < TileIndex(s, tx, ty)))
            {
                bestScore = score;
                tx = ax;
                ty = ay;
            }
        }

        return tx >= 0;
    }

    private static bool TryApproachTile(
        in GameState s,
        PlayerId ai,
        in Unit unit,
        int targetX,
        int targetY,
        out int tx,
        out int ty,
        out int pathLen)
    {
        tx = ty = -1;
        pathLen = int.MaxValue;
        bool isHeavy = unit.Type == UnitType.Heavy;

        for (int d = 0; d < 4; d++)
        {
            int x = targetX + CardinalDx[d], y = targetY + CardinalDy[d];
            if (!s.Map.InBounds(x, y)) continue;
            if (!FogOfWar.IsKnown(s, ai, x, y)) continue;
            if (!s.Map.GetTileUnchecked(x, y).IsPassable(isHeavy)) continue;
            if (IsOccupiedByVisibleEnemy(s, ai, x, y)) continue;

            List<int> path = Pathfinding.FindPath(s.Map, unit.TileX, unit.TileY, x, y, isHeavy);
            if (path.Count == 0 && (unit.TileX != x || unit.TileY != y)) continue;
            int len = path.Count;
            if (len < pathLen || (len == pathLen && TileIndex(s, x, y) < TileIndex(s, tx, ty)))
            {
                pathLen = len;
                tx = x;
                ty = y;
            }
        }

        return tx >= 0;
    }

    private static bool TryBestKnownCityTarget(
        in GameState s,
        PlayerId ai,
        in Unit unit,
        out int tx,
        out int ty)
    {
        tx = ty = -1;
        int bestScore = int.MaxValue;
        bool isHeavy = unit.Type == UnitType.Heavy;

        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner == ai) continue;
            if (!FogOfWar.IsKnown(s, ai, c.TileX, c.TileY)) continue;

            List<int> path = Pathfinding.FindPath(s.Map, unit.TileX, unit.TileY, c.TileX, c.TileY, isHeavy);
            if (path.Count == 0 && (unit.TileX != c.TileX || unit.TileY != c.TileY)) continue;

            int score = path.Count * 100 + (c.Owner == PlayerId.None ? -50 : 0) + (c.IsCapital ? -25 : 0);
            int cityTile = TileIndex(s, c.TileX, c.TileY);
            if (score < bestScore || (score == bestScore && cityTile < TileIndex(s, tx, ty)))
            {
                bestScore = score;
                tx = c.TileX;
                ty = c.TileY;
            }
        }

        return tx >= 0;
    }

    private static bool TryBestVisibleEnemyRoad(
        in GameState s,
        PlayerId ai,
        in Unit unit,
        out int tx,
        out int ty)
    {
        tx = ty = -1;
        int bestScore = int.MaxValue;
        bool isHeavy = unit.Type == UnitType.Heavy;

        for (int y = 0; y < s.Map.Height; y++)
        {
            for (int x = 0; x < s.Map.Width; x++)
            {
                TileType t = s.Map.GetTileUnchecked(x, y);
                if (t is not (TileType.Road or TileType.Bridge)) continue;
                if (!FogOfWar.IsVisible(s, ai, x, y)) continue;

                PlayerId owner = FogOfWar.GetKnownTileOwner(s, ai, x, y);
                bool enemyRoadSupply = s.TileRoadSupplyOwner is not null
                    && TileIndex(s, x, y) < s.TileRoadSupplyOwner.Length
                    && (PlayerId)s.TileRoadSupplyOwner[TileIndex(s, x, y)] == Opponent(ai);
                if (owner != Opponent(ai) && !enemyRoadSupply) continue;
                if (!t.IsPassable(isHeavy)) continue;

                List<int> path = Pathfinding.FindPath(s.Map, unit.TileX, unit.TileY, x, y, isHeavy);
                if (path.Count == 0 && (unit.TileX != x || unit.TileY != y)) continue;
                int score = path.Count * 100 + (enemyRoadSupply ? -50 : 0);
                if (score < bestScore || (score == bestScore && TileIndex(s, x, y) < TileIndex(s, tx, ty)))
                {
                    bestScore = score;
                    tx = x;
                    ty = y;
                }
            }
        }

        return tx >= 0;
    }

    private static bool TryBestExploreTarget(
        in GameState s,
        PlayerId ai,
        in Unit unit,
        out int tx,
        out int ty)
    {
        tx = ty = -1;
        int bestScore = int.MinValue;
        bool isHeavy = unit.Type == UnitType.Heavy;

        for (int y = 0; y < s.Map.Height; y++)
        {
            for (int x = 0; x < s.Map.Width; x++)
            {
                if (!FogOfWar.IsKnown(s, ai, x, y)) continue;
                TileType t = FogOfWar.GetKnownTileType(s, ai, x, y);
                if (!t.IsPassable(isHeavy)) continue;
                if (IsOccupiedByVisibleEnemy(s, ai, x, y)) continue;

                int hidden = HiddenNeighborScore(s, ai, x, y);
                if (hidden <= 0) continue;

                List<int> path = Pathfinding.FindPath(s.Map, unit.TileX, unit.TileY, x, y, isHeavy);
                if (path.Count == 0 && (unit.TileX != x || unit.TileY != y)) continue;

                int score = hidden * 100 - path.Count * 3 - Manhattan(unit.TileX, unit.TileY, x, y);
                if (score > bestScore || (score == bestScore && TileIndex(s, x, y) < TileIndex(s, tx, ty)))
                {
                    bestScore = score;
                    tx = x;
                    ty = y;
                }
            }
        }

        return tx >= 0;
    }

    private static bool TryBestRoadTarget(
        in GameState s,
        PlayerId ai,
        in Unit unit,
        out int tx,
        out int ty)
    {
        tx = ty = -1;
        int bestScore = int.MaxValue;

        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != ai) continue;
            if (!FogOfWar.IsVisible(s, ai, c.TileX, c.TileY)) continue;

            for (int d = 0; d < 4; d++)
            {
                int x = c.TileX + CardinalDx[d], y = c.TileY + CardinalDy[d];
                if (!s.Map.InBounds(x, y)) continue;
                if (!FogOfWar.IsVisible(s, ai, x, y)) continue;
                TileType tile = s.Map.GetTileUnchecked(x, y);
                if (tile is TileType.Road or TileType.Bridge) continue;
                if (!Pathfinding.CanEngineerEnter(s.Map, unit.TileX, unit.TileY, x, y)) continue;

                List<int> path = Pathfinding.FindRoadBuildPath(s.Map, unit.TileX, unit.TileY, x, y);
                if (path.Count == 0 || !RoadPathFullyVisible(s, ai, path)) continue;
                int score = path.Count * 100 + TileIndex(s, x, y);
                if (score < bestScore)
                {
                    bestScore = score;
                    tx = x;
                    ty = y;
                }
            }
        }

        return tx >= 0;
    }

    private static bool TryNearestOwnedShelter(in GameState s, PlayerId ai, in Unit unit, out int tx, out int ty)
    {
        tx = ty = -1;
        int bestLen = int.MaxValue;
        bool isHeavy = unit.Type == UnitType.Heavy;

        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != ai) continue;
            if (!FogOfWar.IsVisible(s, ai, c.TileX, c.TileY)) continue;
            List<int> path = Pathfinding.FindPath(s.Map, unit.TileX, unit.TileY, c.TileX, c.TileY, isHeavy);
            if (path.Count == 0 && (unit.TileX != c.TileX || unit.TileY != c.TileY)) continue;
            int cityTile = TileIndex(s, c.TileX, c.TileY);
            if (path.Count < bestLen || (path.Count == bestLen && cityTile < TileIndex(s, tx, ty)))
            {
                bestLen = path.Count;
                tx = c.TileX;
                ty = c.TileY;
            }
        }

        return tx >= 0;
    }

    private static int FortScore(in GameState s, PlayerId ai, int x, int y)
    {
        int score = 0;
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != ai) continue;
            int d = Manhattan(x, y, c.TileX, c.TileY);
            if (d <= 3) score += c.IsCapital ? 55 : 35;
            else if (d <= 6) score += c.IsCapital ? 25 : 15;
        }

        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner == ai) continue;
            if (!FogOfWar.IsVisible(s, ai, u.TileX, u.TileY)) continue;
            int d = Manhattan(x, y, u.TileX, u.TileY);
            if (d <= 5) score += 50 - d * 6;
        }

        if (TouchesNonOwnedTile(s, ai, x, y)) score += 20;
        return score;
    }

    private static int HiddenNeighborScore(in GameState s, PlayerId ai, int x, int y)
    {
        int score = 0;
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                if (System.Math.Abs(dx) + System.Math.Abs(dy) > 2) continue;
                int nx = x + dx, ny = y + dy;
                if (!s.Map.InBounds(nx, ny)) continue;
                if (FogOfWar.GetVisibility(s, ai, nx, ny) == VisibilityState.Hidden)
                    score++;
            }
        }
        return score;
    }

    private static bool RoadPathFullyVisible(in GameState s, PlayerId ai, List<int> path)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int flat = path[i];
            int x = flat % s.Map.Width, y = flat / s.Map.Width;
            if (!FogOfWar.IsVisible(s, ai, x, y)) return false;
        }
        return true;
    }

    private static bool AlreadyTargeting(in GameState s, in Unit u, int tx, int ty)
    {
        if (u.TileX == tx && u.TileY == ty) return true;
        if (u.Path is null || u.Path.Count == 0) return false;
        return u.Path[^1] == TileIndex(s, tx, ty);
    }

    private static bool IsHpBelowPercent(in Unit u, int threshold)
    {
        FP max = UnitStats.MaxHp(u.Type);
        return u.Hp.Raw * 100 < max.Raw * threshold;
    }

    private static void CountUnits(in GameState s, PlayerId owner, out int light, out int heavy)
    {
        light = 0;
        heavy = 0;
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != owner) continue;
            if (u.Type == UnitType.Heavy) heavy++;
            else light++;
        }
    }

    private static void CountSupply(in GameState s, PlayerId owner, out int used, out int capacity)
    {
        used = 0;
        capacity = 0;
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != owner) continue;
            used += UnitStats.SupplyCost(u.Type);
        }

        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != owner) continue;
            capacity += UnitStats.SupplyCapacity(c);
        }
    }

    private static bool VisibleEnemyNear(in GameState s, PlayerId owner, int x, int y, int radius)
    {
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner == owner) continue;
            if (!FogOfWar.IsVisible(s, owner, u.TileX, u.TileY)) continue;
            if (Manhattan(x, y, u.TileX, u.TileY) <= radius) return true;
        }
        return false;
    }

    private static bool IsOwnedBy(in GameState s, PlayerId owner, int x, int y)
    {
        int idx = TileIndex(s, x, y);
        return s.TileOwner is not null && idx < s.TileOwner.Length && (PlayerId)s.TileOwner[idx] == owner;
    }

    private static bool TouchesNonOwnedTile(in GameState s, PlayerId owner, int x, int y)
    {
        for (int i = 0; i < 4; i++)
        {
            int nx = x + CardinalDx[i], ny = y + CardinalDy[i];
            if (!s.Map.InBounds(nx, ny)) continue;
            if (!IsOwnedBy(s, owner, nx, ny)) return true;
        }
        return false;
    }

    private static bool IsOccupiedByVisibleEnemy(in GameState s, PlayerId owner, int x, int y)
    {
        for (int i = 0; i < s.Units.Count; i++)
        {
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner == owner) continue;
            if (!FogOfWar.IsVisible(s, owner, u.TileX, u.TileY)) continue;
            if (u.TileX == x && u.TileY == y) return true;
        }
        return false;
    }

    private static bool HasPendingRoadForUnit(in GameState s, int unitId)
    {
        if (s.PendingRoads is null) return false;
        for (int i = 0; i < s.PendingRoads.Count; i++)
            if (s.PendingRoads[i].UnitId == unitId) return true;
        return false;
    }

    private static bool HasPendingFortAt(in GameState s, int x, int y)
    {
        if (s.PendingForts is null) return false;
        for (int i = 0; i < s.PendingForts.Count; i++)
            if (s.PendingForts[i].TileX == x && s.PendingForts[i].TileY == y) return true;
        return false;
    }

    private static bool NearOwnedFort(in GameState s, PlayerId owner, int x, int y, int radius)
    {
        for (int i = 0; i < s.Cities.Count; i++)
        {
            City c = s.Cities[i];
            if (c.Owner != owner) continue;
            if (s.Map.GetTileUnchecked(c.TileX, c.TileY) != TileType.Fort) continue;
            if (Manhattan(x, y, c.TileX, c.TileY) <= radius) return true;
        }
        return false;
    }

    private static PlayerId Opponent(PlayerId player)
        => player == PlayerId.Player1 ? PlayerId.Player2 : PlayerId.Player1;

    private static int Manhattan(int ax, int ay, int bx, int by)
        => System.Math.Abs(ax - bx) + System.Math.Abs(ay - by);

    private static int TileIndex(in GameState s, int x, int y)
        => x < 0 || y < 0 ? int.MaxValue : y * s.Map.Width + x;
}
