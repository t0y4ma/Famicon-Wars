using System;

namespace FamiconWars.Core
{
    public struct Forecast
    {
        public int DamageToDefenderMin, DamageToDefenderMax;
        public bool Counter;
        public int DamageToAttackerMin, DamageToAttackerMax;
    }

    /// <summary>
    /// Damage = floor(B * HPa/100 * (100 - k*T)/100 * (1 + bonus) * takenPct) + R
    /// Attacker and defender fire simultaneously, both using pre-battle HP.
    /// </summary>
    public static class Combat
    {
        public static bool CanTarget(GameState s, UnitState att, UnitState def)
        {
            if (att.Army == def.Army || def.IsCarried) return false;
            if (att.Ammo <= 0) return false;
            return s.Data.BaseDamage(s.Def(att), s.Def(def)) >= 0;
        }

        /// <summary>Whether att, standing at (fx,fy), can attack def. moved = it moved this action.</summary>
        public static bool CanAttackFrom(GameState s, UnitState att, UnitState def, int fx, int fy, bool moved)
        {
            var d = s.Def(att);
            if (d.RangeMax <= 0 || !CanTarget(s, att, def)) return false;
            if (d.IsIndirect && moved) return false;
            int dist = Movement.Distance(fx, fy, def.X, def.Y);
            return dist >= d.RangeMin && dist <= d.RangeMax;
        }

        /// <summary>Counter happens only for direct attacks at distance 1 by a defender with a range-1 weapon.</summary>
        public static bool CanCounter(GameState s, UnitState att, int ax, int ay, UnitState def)
        {
            var ad = s.Def(att);
            var dd = s.Def(def);
            if (ad.IsIndirect || Movement.Distance(ax, ay, def.X, def.Y) != 1) return false;
            if (dd.RangeMin != 1 || def.Ammo <= 0) return false;
            return s.Data.BaseDamage(dd, ad) >= 0;
        }

        /// <summary>Damage before the random term.</summary>
        public static int BaseRoll(GameState s, UnitState shooter, int shooterHp, UnitState target, int tx, int ty, bool initiator)
        {
            var sd = s.Def(shooter);
            var td = s.Def(target);
            int b = s.Data.BaseDamage(sd, td);
            if (b < 0) return 0;
            int t = td.Domain == Domain.Air ? 0 : s.TerrainAt(tx, ty).DefenseFor(td);
            long terrain = Math.Max(0, 10000 - (long)s.Rules.TerrainK * t); // /10000
            long bonus = 100 + (initiator ? s.Rules.AttackerBonus : 0);     // /100
            long taken = target.Army == Army.None ? 100 : s.DamageTakenPct[(int)target.Army]; // /100
            long num = (long)b * shooterHp * terrain * bonus * taken;
            long den = 100L * 10000L * 100L * 100L;
            return (int)(num / den);
        }

        public static Forecast Predict(GameState s, UnitState att, int ax, int ay, UnitState def)
        {
            var f = new Forecast();
            int b = BaseRoll(s, att, att.Hp, def, def.X, def.Y, true);
            f.DamageToDefenderMin = Math.Min(def.Hp, b + s.Rules.RandMin);
            f.DamageToDefenderMax = Math.Min(def.Hp, b + s.Rules.RandMax);
            f.Counter = CanCounter(s, att, ax, ay, def);
            if (f.Counter)
            {
                int c = BaseRoll(s, def, def.Hp, att, ax, ay, false);
                f.DamageToAttackerMin = Math.Min(att.Hp, c + s.Rules.RandMin);
                f.DamageToAttackerMax = Math.Min(att.Hp, c + s.Rules.RandMax);
            }
            return f;
        }

        /// <summary>Rolls both sides with pre-battle HP. Does not modify HP; RulesEngine applies the result.</summary>
        public static void Roll(GameState s, UnitState att, int ax, int ay, UnitState def, out int toDef, out int toAtt, out bool counter)
        {
            int preAtt = att.Hp, preDef = def.Hp;
            toDef = Math.Min(preDef, BaseRoll(s, att, preAtt, def, def.X, def.Y, true) + s.Rng.Range(s.Rules.RandMin, s.Rules.RandMax));
            counter = CanCounter(s, att, ax, ay, def);
            toAtt = 0;
            if (counter)
                toAtt = Math.Min(preAtt, BaseRoll(s, def, preDef, att, ax, ay, false) + s.Rng.Range(s.Rules.RandMin, s.Rules.RandMax));
        }
    }
}
