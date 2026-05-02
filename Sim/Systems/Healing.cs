using System.Runtime.InteropServices;
using WarGame.Sim.Math;
using WarGame.Sim.State;

namespace WarGame.Sim.Systems;

// A unit standing on a friendly city tile regenerates HP each tick at the
// owner's expense — every healed point of HP costs the player some ECO.
// This gives wounded units a tactical reason to retreat home rather than
// throwing them away in another fight.
//
// Tunings (Phase 1, will iterate):
//   HP per tick     : 0.5  (15 HP/sec at 30 Hz; light fully heals in ~4s,
//                            heavy in ~10s)
//   ECO per tick    : 0.05 (1.5 ECO/sec while healing; full light heal
//                            ~6 ECO, full heavy ~15 ECO — both ~50% of
//                            their build cost, so retreating is usually
//                            but not always cheaper than rebuilding)
//
// Healing stops if the player runs out of ECO (drain capped at available).
public static class Healing
{
    public static readonly FP HpPerTick = FP.One / FP.FromInt(2);    // 0.5
    public static readonly FP EcoPerTick = FP.One / FP.FromInt(20);  // 0.05

    public static void Tick(ref GameState s)
    {
        if (s.Cities.Count == 0 || s.Units.Count == 0) return;

        Span<Unit> units = CollectionsMarshal.AsSpan(s.Units);
        for (int i = 0; i < units.Length; i++)
        {
            ref Unit u = ref units[i];
            if (!u.IsAlive) continue;
            if (u.Owner == PlayerId.None) continue;

            FP max = UnitStats.MaxHp(u.Type);
            if (u.Hp >= max) continue;

            // Healing is stricter than supply: roads can lower maintenance,
            // but they never create a safe hospital. The unit must be on an
            // owned city/fort tile that is still friendly-controlled.
            bool onFriendlyCity = false;
            for (int j = 0; j < s.Cities.Count; j++)
            {
                City c = s.Cities[j];
                if (c.Owner != u.Owner) continue;
                if (c.TileX == u.TileX && c.TileY == u.TileY)
                {
                    onFriendlyCity = true;
                    break;
                }
            }
            if (!onFriendlyCity) continue;

            int tileIdx = u.TileY * s.Map.Width + u.TileX;
            if (s.TileOwner is null || tileIdx >= s.TileOwner.Length
                || (PlayerId)s.TileOwner[tileIdx] != u.Owner)
                continue;
            SupplyStatus supply = SupplyLines.GetUnitStatus(s, i);
            if (supply is SupplyStatus.None or SupplyStatus.CutOff)
                continue;

            ref Player p = ref s.Players[(int)u.Owner];
            if (p.Eco < EcoPerTick) continue;

            p.Eco -= EcoPerTick;
            FP next = u.Hp + HpPerTick;
            u.Hp = next > max ? max : next;
        }
    }
}
