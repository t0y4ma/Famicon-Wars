using System;
using System.Collections.Generic;

namespace FamiconWars.Core
{
    /// <summary>
    /// A plain rule-based 内政 for the COM (no tuned weights yet). Called whenever 内政 is open to the
    /// army (another army's phase, a 内政 time). It keeps money for the next phase's production and
    /// reserves, best first:
    ///  1. the base, when a facility of ours lies outside the production range and the next level reaches it;
    ///  2. a factory / airport / port, for the units of its next level;
    ///  3. a city into a factory, when no factory of ours is within the production range;
    ///  4. facilities for income (the quicker it pays back, the better).
    /// Facilities an enemy foot unit could reach next phase are left alone (a capture would waste the money).
    /// </summary>
    public static class DomesticAi
    {
        /// <summary>The reservations to make now, in order (each still to be checked when applied).</summary>
        public static List<Command> Plan(GameState s, Army me)
        {
            var list = new List<Command>();
            if (!RulesEngine.DomesticOpen(s, me)) return list;
            var e = s.Data.Economy;
            int income = 0;
            for (int i = 0; i < s.Owner.Length; i++) if (s.Owner[i] == me) income += RulesEngine.Income(s, i);
            // money kept for the next phase's units (the income comes on top of it)
            int keep = Math.Max(3000, income / 2);
            int spendable = s.Funds[(int)me] - keep;
            if (spendable <= 0) return list;

            int factoriesInRange = 0;
            var outOfRange = new List<int>();
            int hq = -1;
            for (int i = 0; i < s.Owner.Length; i++)
            {
                if (s.Owner[i] != me) continue;
                var t = s.Data.Terrains[s.Terrain[i]];
                if (t.Id == "HQ") { hq = i; continue; }
                if (t.Produces == null) continue;
                bool inRange = RulesEngine.InProductionRange(s, me, i % s.Width, i / s.Width);
                if (!inRange) outOfRange.Add(i);
                else if (t.Id != e.ConvertFrom) factoriesInRange++;
            }

            var options = new List<(float score, int cost, Command cmd, int tile)>();
            for (int i = 0; i < s.Owner.Length; i++)
            {
                if (s.Owner[i] != me) continue;
                var t = s.Data.Terrains[s.Terrain[i]];
                if (!t.IsProperty || s.Reserve[i] != 0) continue;
                int x = i % s.Width, y = i / s.Width;
                if (Threatened(s, me, x, y)) continue;
                int lv = s.Level[i];

                // level up
                if (RulesEngine.CheckReserve(s, me, x, y, null, out int cost) == null)
                {
                    float score = 0;
                    int gain = e.IncomeFor(t.Income, lv + 1) - e.IncomeFor(t.Income, lv);
                    if (gain > 0 && cost > 0) score = 40f * gain * 10 / cost;                 // payback
                    if (i == hq)
                    {
                        int next = e.RadiusFor(lv + 1);
                        foreach (var f in outOfRange)
                        {
                            int dx = Math.Abs(f % s.Width - x), dy = Math.Abs(f / s.Width - y);
                            if (next < 0 || Math.Max(dx, dy) <= next) { score += 100; break; }
                        }
                    }
                    else if (t.Produces != null && t.Id != e.ConvertFrom)
                    {
                        bool unlocks = false;
                        foreach (var (def, need) in RulesEngine.ProductionTable(s, x, y)) if (need == lv + 1) unlocks = true;
                        if (unlocks) score += lv == 1 ? 90 : lv == 2 ? 60 : 40;
                        if (t.Produces == Domain.Air || t.Produces == Domain.Sea) score -= 10;
                        if (!RulesEngine.InProductionRange(s, me, x, y)) score -= 50;       // useless until the base reaches it
                    }
                    if (score > 0) options.Add((score, cost, new LevelUpCommand { Army = me, X = x, Y = y }, i));
                }

                // a city into a factory when there is none to make units at
                if (t.Id == e.ConvertFrom && factoriesInRange == 0 && RulesEngine.InProductionRange(s, me, x, y)
                    && RulesEngine.CheckReserve(s, me, x, y, "FACTORY", out int ccost) == null)
                    options.Add((70, ccost, new ConvertCommand { Army = me, X = x, Y = y, Target = "FACTORY" }, i));
            }

            options.Sort((a, b) => b.score.CompareTo(a.score));
            var used = new HashSet<int>();
            foreach (var o in options)
            {
                if (used.Contains(o.tile) || o.cost > spendable) continue;
                list.Add(o.cmd);
                used.Add(o.tile);
                spendable -= o.cost;
            }
            return list;
        }

        /// <summary>An enemy unit that can capture stands close enough to start capturing the tile next phase.</summary>
        static bool Threatened(GameState s, Army me, int x, int y)
        {
            foreach (var u in s.Units)
            {
                if (u.Army == me || u.Army == Army.None || s.IsOut(u.Army)) continue;
                var d = s.Def(u);
                int reach = d.Capture > 0 ? d.Move + 1 : d.CargoCapacity > 0 ? d.Move + 2 : -1;   // transports can bring infantry
                if (reach < 0) continue;
                int ux = u.X, uy = u.Y;
                if (u.IsCarried) { var c = s.UnitById(u.CarriedBy); if (c == null) continue; ux = c.X; uy = c.Y; }
                if (Math.Abs(ux - x) + Math.Abs(uy - y) <= reach) return true;
            }
            return false;
        }
    }
}
