using System;
using System.Collections.Generic;

namespace FamiconWars.Core
{
    public enum MoveClass { Foot, Vehicle, Air, Ship }
    public enum Domain { Ground, Air, Sea }
    public enum Army { None = -1, Red = 0, Blue = 1 }

    public sealed class UnitDef
    {
        public int Index;
        public string Id, NameKey, ShortName;
        public int Price, Move, Fuel, FuelPerPhase, Ammo, AmmoPrice, RangeMin, RangeMax;
        public MoveClass MoveClass;
        /// <summary>0 = cannot capture, 1 = all but HQ, 2 = everything.</summary>
        public int Capture;
        public int CargoCapacity;
        /// <summary>"Foot" = infantry only, "Ground" = any ground unit.</summary>
        public string CargoClass;
        public bool CanSupply;

        public Domain Domain => MoveClass == MoveClass.Air ? Domain.Air : MoveClass == MoveClass.Ship ? Domain.Sea : Domain.Ground;
        public bool IsIndirect => RangeMin > 1;
        public bool CanCarry(UnitDef other)
        {
            if (CargoCapacity <= 0) return false;
            if (CargoClass == "Foot") return other.MoveClass == MoveClass.Foot;
            if (CargoClass == "Ground") return other.Domain == Domain.Ground;
            return false;
        }
    }

    public sealed class TerrainDef
    {
        public int Index;
        public string Id, NameKey, Color;
        public char Char;
        public int DefFoot, DefVehicle, DefShip, Income;
        public bool IsProperty;
        public Domain? Produces, Supplies;
        /// <summary>Move cost per MoveClass; -1 = impassable.</summary>
        public int[] Cost = new int[4];

        public int DefenseFor(UnitDef u)
        {
            switch (u.MoveClass)
            {
                case MoveClass.Foot: return DefFoot;
                case MoveClass.Vehicle: return DefVehicle;
                case MoveClass.Ship: return DefShip;
                default: return 0;
            }
        }
    }

    public sealed class Rules
    {
        public int TerrainK = 100, AttackerBonus = 0, RandMin = 0, RandMax = 9;
        public int CaptureGoal = 20, TimeLimit = 255, ProductionRadius = 2, ResupplyHp = 20, StartFunds = 0, UnitLimit = 50;

        public Rules Clone() => (Rules)MemberwiseClone();
    }

    /// <summary>Immutable master data loaded from CSV text.</summary>
    public sealed class GameData
    {
        public readonly List<UnitDef> Units = new List<UnitDef>();
        public readonly List<TerrainDef> Terrains = new List<TerrainDef>();
        public int[,] Damage; // [attacker, defender], -1 = cannot attack
        public Rules Rules = new Rules();
        readonly Dictionary<string, UnitDef> unitById = new Dictionary<string, UnitDef>();
        readonly Dictionary<string, TerrainDef> terrainById = new Dictionary<string, TerrainDef>();
        readonly Dictionary<char, TerrainDef> terrainByChar = new Dictionary<char, TerrainDef>();

        public UnitDef Unit(string id) => unitById.TryGetValue(id, out var u) ? u : throw new KeyNotFoundException("unit " + id);
        public TerrainDef Terrain(string id) => terrainById.TryGetValue(id, out var t) ? t : throw new KeyNotFoundException("terrain " + id);
        public TerrainDef TerrainByChar(char c) => terrainByChar.TryGetValue(c, out var t) ? t : throw new KeyNotFoundException("terrain char " + c);
        public int BaseDamage(UnitDef a, UnitDef d) => Damage[a.Index, d.Index];

        /// <summary>Same definitions with different rules (the COM simulates with a fixed mid-range roll).</summary>
        public GameData WithRules(Rules rules)
        {
            var g = (GameData)MemberwiseClone();
            g.Rules = rules;
            return g;
        }

        public static GameData Load(string unitsCsv, string terrainsCsv, string damageCsv, string rulesCsv)
        {
            var g = new GameData();
            foreach (var r in Csv.Read(unitsCsv))
            {
                var u = new UnitDef
                {
                    Index = g.Units.Count, Id = r["id"], NameKey = r["nameKey"], ShortName = r["shortName"],
                    Price = r.Int("price"), Move = r.Int("move"), MoveClass = (MoveClass)Enum.Parse(typeof(MoveClass), r["moveClass"]),
                    Fuel = r.Int("fuel"), FuelPerPhase = r.Int("fuelPerPhase"), Ammo = r.Int("ammo"), AmmoPrice = r.Int("ammoPrice"),
                    RangeMin = r.Int("rangeMin"), RangeMax = r.Int("rangeMax"), Capture = r.Int("capture"),
                    CargoCapacity = r.Int("cargoCapacity"), CargoClass = r["cargoClass"], CanSupply = r.Int("canSupply") == 1
                };
                g.Units.Add(u); g.unitById[u.Id] = u;
            }
            foreach (var r in Csv.Read(terrainsCsv))
            {
                var t = new TerrainDef
                {
                    Index = g.Terrains.Count, Id = r["id"], NameKey = r["nameKey"], Char = r["char"][0],
                    DefFoot = r.Int("defFoot"), DefVehicle = r.Int("defVehicle"), DefShip = r.Int("defShip"),
                    IsProperty = r.Int("isProperty") == 1, Income = r.Int("income"),
                    Produces = ParseDomain(r["produces"]), Supplies = ParseDomain(r["supplies"]), Color = r["color"]
                };
                t.Cost[(int)MoveClass.Foot] = r.Int("costFoot");
                t.Cost[(int)MoveClass.Vehicle] = r.Int("costVehicle");
                t.Cost[(int)MoveClass.Air] = r.Int("costAir");
                t.Cost[(int)MoveClass.Ship] = r.Int("costShip");
                g.Terrains.Add(t); g.terrainById[t.Id] = t; g.terrainByChar[t.Char] = t;
            }
            int n = g.Units.Count;
            g.Damage = new int[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) g.Damage[i, j] = -1;
            foreach (var r in Csv.Read(damageCsv))
            {
                var a = g.Unit(r["attacker"]);
                foreach (var d in g.Units)
                {
                    var v = r[d.Id];
                    g.Damage[a.Index, d.Index] = string.IsNullOrWhiteSpace(v) ? -1 : int.Parse(v);
                }
            }
            if (!string.IsNullOrEmpty(rulesCsv))
            {
                foreach (var r in Csv.Read(rulesCsv))
                {
                    int v = r.Int("value");
                    switch (r["key"])
                    {
                        case "terrainK": g.Rules.TerrainK = v; break;
                        case "attackerBonus": g.Rules.AttackerBonus = v; break;
                        case "randMin": g.Rules.RandMin = v; break;
                        case "randMax": g.Rules.RandMax = v; break;
                        case "captureGoal": g.Rules.CaptureGoal = v; break;
                        case "timeLimit": g.Rules.TimeLimit = v; break;
                        case "productionRadius": g.Rules.ProductionRadius = v; break;
                        case "resupplyHp": g.Rules.ResupplyHp = v; break;
                        case "startFunds": g.Rules.StartFunds = v; break;
                        case "unitLimit": g.Rules.UnitLimit = v; break;
                    }
                }
            }
            return g;
        }

        static Domain? ParseDomain(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "-") return null;
            return (Domain)Enum.Parse(typeof(Domain), s);
        }
    }

    /// <summary>Minimal CSV reader (no quoted fields needed for our data).</summary>
    public static class Csv
    {
        public sealed class Row
        {
            readonly Dictionary<string, string> cells;
            public Row(Dictionary<string, string> c) { cells = c; }
            public string this[string key] => cells.TryGetValue(key, out var v) ? v : "";
            public int Int(string key) { var v = this[key]; return string.IsNullOrWhiteSpace(v) ? 0 : int.Parse(v); }
        }

        public static List<Row> Read(string text)
        {
            var rows = new List<Row>();
            var lines = text.Replace("\r", "").Split('\n');
            string[] header = null;
            foreach (var raw in lines)
            {
                var line = raw.TrimStart('\uFEFF');
                if (line.Trim().Length == 0) continue;
                var parts = line.Split(',');
                if (header == null) { header = parts; continue; }
                var d = new Dictionary<string, string>();
                for (int i = 0; i < header.Length; i++) d[header[i].Trim()] = i < parts.Length ? parts[i].Trim() : "";
                rows.Add(new Row(d));
            }
            return rows;
        }
    }
}
