using System;
using System.Collections.Generic;
using System.Globalization;

namespace FamiconWars.Core
{
    /// <summary>When 内政 can be done: off-turn (others' phases and the 内政 time) or only in the 内政 time.</summary>
    public enum DomesticTiming { OffTurn, InterludeOnly }

    /// <summary>
    /// 内政 settings, read from Resources/Data/economy.csv (key,value,note) and production.csv (which level of
    /// which facility makes which unit). Edit those files to tune; nothing here is chosen on the match screen.
    /// </summary>
    public sealed class Economy
    {
        public int MaxLevel = 4;
        /// <summary>Income = base × (1000 + (Lv-1) × this) / 1000.</summary>
        public int IncomeFactorPermille = 500;
        public int CaptureLevelDrop = 1, ConvertLevelDrop = 1, ConvertMinLevel = 2;
        /// <summary>Production radius around a base, per base level (index Lv-1); -1 = anywhere.</summary>
        public int[] HqRadius = { 2, 3, 4, -1 };
        /// <summary>Facilities of this level or higher (of FreeActTerrains): the first unit made there in a phase can move at once. 0 = off.</summary>
        public int FreeActLevel = 4;
        public readonly HashSet<string> FreeActTerrains = new HashSet<string> { "FACTORY", "AIRPORT", "PORT" };
        /// <summary>Terrain id → cost of raising a facility to level 2, 3, … (index Lv-2).</summary>
        public readonly Dictionary<string, int[]> LevelCost = new Dictionary<string, int[]>();
        /// <summary>Conversion target terrain id → cost. Only a city converts.</summary>
        public readonly Dictionary<string, int> ConvertCost = new Dictionary<string, int>();
        public string ConvertFrom = "CITY";
        public DomesticTiming Timing = DomesticTiming.OffTurn;
        /// <summary>Share of the human players (‰) who must press 「内政終了」 to end the 内政 time.</summary>
        public int InterludeVotePermille = 666;
        /// <summary>[producing terrain index, unit index] → level needed (0 = never made there).</summary>
        public int[,] Unlock;

        public int LevelUpCost(string terrainId, int fromLevel)
        {
            if (fromLevel >= MaxLevel || !LevelCost.TryGetValue(terrainId, out var c)) return -1;
            int k = fromLevel - 1;
            return k >= 0 && k < c.Length ? c[k] : -1;
        }

        public int RadiusFor(int hqLevel)
        {
            int k = Math.Max(1, Math.Min(hqLevel, HqRadius.Length)) - 1;
            return HqRadius.Length == 0 ? 2 : HqRadius[k];
        }

        public int IncomeFor(int baseIncome, int level) => baseIncome * (1000 + (Math.Max(1, level) - 1) * IncomeFactorPermille) / 1000;

        public int Required(TerrainDef t, UnitDef u) => Unlock == null ? 1 : Unlock[t.Index, u.Index];

        public static Economy Load(GameData g, string economyCsv, string productionCsv)
        {
            var e = new Economy();
            foreach (var r in Csv.Read(economyCsv))
            {
                string key = r["key"], v = r["value"];
                switch (key)
                {
                    case "maxLevel": e.MaxLevel = Int(v); break;
                    case "incomeFactor": e.IncomeFactorPermille = (int)Math.Round(decimal.Parse(v, CultureInfo.InvariantCulture) * 1000); break;
                    case "captureLevelDrop": e.CaptureLevelDrop = Int(v); break;
                    case "convertLevelDrop": e.ConvertLevelDrop = Int(v); break;
                    case "convertMinLevel": e.ConvertMinLevel = Int(v); break;
                    case "convertFrom": e.ConvertFrom = v; break;
                    case "hqRadius": e.HqRadius = Ints(v); break;
                    case "freeActLevel": e.FreeActLevel = Int(v); break;
                    case "freeActTerrains":
                        e.FreeActTerrains.Clear();
                        foreach (var t in v.Split('|')) if (t.Trim().Length > 0) e.FreeActTerrains.Add(t.Trim());
                        break;
                    case "timing": e.Timing = v == "interlude" ? DomesticTiming.InterludeOnly : DomesticTiming.OffTurn; break;
                    case "interludeVote": e.InterludeVotePermille = (int)Math.Round(decimal.Parse(v, CultureInfo.InvariantCulture) * 1000); break;
                    default:
                        if (key.StartsWith("cost.")) e.LevelCost[key.Substring(5)] = Ints(v);
                        else if (key.StartsWith("convert.")) e.ConvertCost[key.Substring(8)] = Int(v);
                        break;
                }
            }
            e.Unlock = new int[g.Terrains.Count, g.Units.Count];
            if (!string.IsNullOrEmpty(productionCsv))
            {
                foreach (var r in Csv.Read(productionCsv))
                {
                    UnitDef u;
                    try { u = g.Unit(r["unit"]); } catch (KeyNotFoundException) { continue; }
                    foreach (var t in g.Terrains)
                        if (t.Produces != null) e.Unlock[t.Index, u.Index] = r.Int(t.Id);
                }
            }
            else
            {
                // no table: every facility makes what it made before, from level 1
                foreach (var t in g.Terrains)
                    if (t.Produces != null)
                        foreach (var u in g.Units) if (u.Domain == t.Produces.Value) e.Unlock[t.Index, u.Index] = 1;
            }
            return e;
        }

        static int Int(string v) => int.Parse(v.Trim(), CultureInfo.InvariantCulture);
        static int[] Ints(string v)
        {
            var parts = v.Split('|');
            var a = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) a[i] = Int(parts[i]);
            return a;
        }

        // ---- writing back (the editor windows) ----

        /// <summary>production.csv text for the current Unlock table.</summary>
        public static string ProductionCsv(GameData g, int[,] unlock)
        {
            var sb = new System.Text.StringBuilder("unit");
            var cols = new List<TerrainDef>();
            foreach (var t in g.Terrains) if (t.Produces != null) { cols.Add(t); sb.Append(',').Append(t.Id); }
            sb.Append('\n');
            foreach (var u in g.Units)
            {
                sb.Append(u.Id);
                foreach (var t in cols) { sb.Append(','); if (unlock[t.Index, u.Index] > 0) sb.Append(unlock[t.Index, u.Index]); }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
