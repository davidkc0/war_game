using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

public enum UnitPerk : byte
{
    LightOptics = 1,
    LightPathfinder = 2,
    LightQuickMarch = 3,
    LightRoadRunner = 4,
    LightPackTactics = 5,
    LightScreenLine = 6,

    HeavyPlating = 11,
    HeavyHullDown = 12,
    HeavyGunnery = 13,
    HeavyBreacher = 14,
    HeavyStabilizers = 15,
    HeavySpotterCrew = 16,
}

public static class UnitProgression
{
    public const byte MaxRank = 4;
    public static readonly FP Rank2Xp = FP.FromInt(80);
    public static readonly FP Rank3Xp = FP.FromInt(200);
    public static readonly FP Rank4Xp = FP.FromInt(380);
    public static readonly FP KillBonusXp = FP.FromInt(30);

    public static byte RankForXp(long xpRaw)
    {
        if (xpRaw >= Rank4Xp.Raw) return 4;
        if (xpRaw >= Rank3Xp.Raw) return 3;
        if (xpRaw >= Rank2Xp.Raw) return 2;
        return 1;
    }

    public static FP CurrentRankThreshold(in Unit u)
    {
        return u.Rank switch
        {
            <= 1 => Rank2Xp,
            2 => Rank3Xp,
            3 => Rank4Xp,
            _ => Rank4Xp,
        };
    }

    public static void AwardXp(ref Unit u, FP amount)
    {
        if (!u.IsAlive || amount <= FP.Zero) return;
        byte before = u.Rank == 0 ? (byte)1 : u.Rank;
        u.XpRaw += amount.Raw;
        byte after = RankForXp(u.XpRaw);
        if (after > MaxRank) after = MaxRank;
        u.Rank = after;
        if (after > before)
            u.PromotionPoints += (byte)(after - before);
    }

    public static bool TryChoosePerk(ref Unit u, byte perkId)
    {
        if (!u.IsAlive) return false;
        if (u.PromotionPoints == 0) return false;
        if (!IsValidPerkForUnit(u.Type, perkId)) return false;

        uint bit = PerkBit(perkId);
        if ((u.PerkMask & bit) != 0) return false;

        u.PerkMask |= bit;
        u.PromotionPoints--;
        return true;
    }

    public static bool HasPerk(in Unit u, UnitPerk perk)
        => (u.PerkMask & PerkBit((byte)perk)) != 0;

    public static bool IsValidPerkForUnit(UnitType type, byte perkId) => type switch
    {
        UnitType.Light => perkId is >= 1 and <= 6,
        UnitType.Heavy => perkId is >= 11 and <= 16,
        _ => false,
    };

    public static string PerkName(byte perkId) => ((UnitPerk)perkId) switch
    {
        UnitPerk.LightOptics => "Optics",
        UnitPerk.LightPathfinder => "Pathfinder",
        UnitPerk.LightQuickMarch => "Quick March",
        UnitPerk.LightRoadRunner => "Road Runner",
        UnitPerk.LightPackTactics => "Pack Tactics",
        UnitPerk.LightScreenLine => "Screen Line",
        UnitPerk.HeavyPlating => "Plating",
        UnitPerk.HeavyHullDown => "Hull Down",
        UnitPerk.HeavyGunnery => "Gunnery",
        UnitPerk.HeavyBreacher => "Breacher",
        UnitPerk.HeavyStabilizers => "Stabilizers",
        UnitPerk.HeavySpotterCrew => "Spotter Crew",
        _ => "Unknown",
    };

    public static int VisionRadius(in Unit u)
    {
        int radius = UnitStats.VisionRadius(u.Type);
        if (HasPerk(u, UnitPerk.LightOptics)) radius += 1;
        if (HasPerk(u, UnitPerk.HeavySpotterCrew)) radius += 1;
        return radius;
    }

    public static long SpeedFactorRaw(in Unit u, TileType tile)
    {
        bool isHeavy = u.Type == UnitType.Heavy;
        long raw = tile.SpeedFactorRaw(isHeavy);

        if (u.Type == UnitType.Light)
        {
            if (HasPerk(u, UnitPerk.LightPathfinder))
            {
                if (tile == TileType.Mountain) raw = FP.OneRaw * 40 / 100;
                else if (tile == TileType.River) raw = FP.OneRaw / 2;
            }
            if (HasPerk(u, UnitPerk.LightRoadRunner) && tile is TileType.Road or TileType.Bridge)
                raw = FP.OneRaw * 165 / 100;
            else if (HasPerk(u, UnitPerk.LightQuickMarch) && tile is not TileType.Road and not TileType.Bridge)
                raw = raw * 110 / 100;
        }

        return raw;
    }

    public static FP DamageMultiplier(in GameState s, int attackerId, int targetId, int sharedTargetAllies)
    {
        Unit attacker = s.Units[attackerId];
        Unit target = s.Units[targetId];
        FP mul = FP.One;

        if (HasPerk(attacker, UnitPerk.LightPackTactics) && sharedTargetAllies > 1)
            mul = mul * (FP.One + FP.FromInt(7) / FP.FromInt(100));

        if (HasPerk(attacker, UnitPerk.HeavyGunnery) && !attacker.IsMoving)
            mul = mul * (FP.One + FP.FromInt(8) / FP.FromInt(100));

        TileType defenderTile = s.Map.GetTileUnchecked(target.TileX, target.TileY);
        if (HasPerk(attacker, UnitPerk.HeavyBreacher)
            && defenderTile is TileType.City or TileType.Capital or TileType.Fort or TileType.Road or TileType.Bridge)
            mul = mul * (FP.One + FP.FromInt(10) / FP.FromInt(100));

        if (HasPerk(attacker, UnitPerk.HeavySpotterCrew) && TargetAlsoEngagedByFriendlyLight(s, attackerId, targetId))
            mul = mul * (FP.One + FP.FromInt(5) / FP.FromInt(100));

        return mul;
    }

    public static FP IncomingDamageMultiplier(in GameState s, int targetId)
    {
        Unit target = s.Units[targetId];
        FP mul = FP.One;

        if (HasPerk(target, UnitPerk.HeavyPlating) && !target.IsMoving)
            mul = mul * (FP.One - FP.FromInt(7) / FP.FromInt(100));

        TileType tile = s.Map.GetTileUnchecked(target.TileX, target.TileY);
        if (HasPerk(target, UnitPerk.HeavyHullDown)
            && tile is TileType.Forest or TileType.City or TileType.Capital or TileType.Fort)
            mul = mul * (FP.One - FP.FromInt(8) / FP.FromInt(100));

        if (HasPerk(target, UnitPerk.LightScreenLine) && HasAdjacentFriendly(s, targetId))
            mul = mul * (FP.One - FP.FromInt(7) / FP.FromInt(100));

        return mul;
    }

    public static FP MovingAttackerMultiplier(in Unit attacker)
    {
        if (!attacker.IsMoving) return FP.One;
        if (HasPerk(attacker, UnitPerk.HeavyStabilizers))
            return FP.FromInt(92) / FP.FromInt(100);
        return FP.FromInt(85) / FP.FromInt(100);
    }

    private static bool TargetAlsoEngagedByFriendlyLight(in GameState s, int attackerId, int targetId)
    {
        Unit attacker = s.Units[attackerId];
        Unit target = s.Units[targetId];
        for (int i = 0; i < s.Units.Count; i++)
        {
            if (i == attackerId) continue;
            Unit u = s.Units[i];
            if (!u.IsAlive || u.Owner != attacker.Owner || u.Type != UnitType.Light) continue;
            if (IsAdjacentOrSame(u.TileX, u.TileY, target.TileX, target.TileY)) return true;
        }
        return false;
    }

    private static bool HasAdjacentFriendly(in GameState s, int unitId)
    {
        Unit unit = s.Units[unitId];
        for (int i = 0; i < s.Units.Count; i++)
        {
            if (i == unitId) continue;
            Unit other = s.Units[i];
            if (!other.IsAlive || other.Owner != unit.Owner) continue;
            int dx = other.TileX - unit.TileX; if (dx < 0) dx = -dx;
            int dy = other.TileY - unit.TileY; if (dy < 0) dy = -dy;
            if (dx + dy == 1) return true;
        }
        return false;
    }

    private static bool IsAdjacentOrSame(int ax, int ay, int bx, int by)
    {
        int dx = ax - bx; if (dx < 0) dx = -dx;
        int dy = ay - by; if (dy < 0) dy = -dy;
        return dx + dy <= 1;
    }

    private static uint PerkBit(byte perkId) => 1u << (perkId - 1);
}
