using System;
using System.Collections.Generic;

namespace FamiconWars.Core
{
    /// <summary>Deterministic xorshift32. The state lives inside GameState so replays and online play match.</summary>
    public struct Rng
    {
        public uint State;
        public Rng(uint seed) { State = seed == 0 ? 2463534242u : seed; }
        public uint Next() { uint x = State; x ^= x << 13; x ^= x >> 17; x ^= x << 5; State = x; return x; }
        /// <summary>Inclusive range.</summary>
        public int Range(int min, int max) => max <= min ? min : min + (int)(Next() % (uint)(max - min + 1));
    }

    public sealed class UnitState
    {
        public int Id, Type, X, Y, Hp, Fuel, Ammo;
        public Army Army;
        public bool Acted;
        public int CarriedBy = -1;
        public List<int> Cargo = new List<int>();

        /// <summary>Displayed count of machines/soldiers (1..10).</summary>
        public int Count => (Hp + 9) / 10;
        public bool IsCarried => CarriedBy >= 0;

        public UnitState Clone()
        {
            var u = (UnitState)MemberwiseClone();
            u.Cargo = new List<int>(Cargo);
            return u;
        }
    }

    public sealed class MapDef
    {
        public string Name = "";
        public int Width, Height;
        public int[] Tiles;        // terrain index per tile
        public Army[] Owners;      // initial owner per tile
        public List<(string type, Army army, int x, int y, int hp)> Units = new List<(string, Army, int, int, int)>();

        /// <summary>Parses the text format used in Resources/Maps.</summary>
        public static MapDef Parse(string text, GameData data)
        {
            var m = new MapDef();
            var lines = text.Replace("\r", "").Split('\n');
            string section = "";
            var tileRows = new List<string>();
            var ownerRows = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw.TrimStart('\uFEFF').TrimEnd();
                if (line.Length == 0) continue;
                if (line.StartsWith("[")) { section = line; continue; }
                switch (section)
                {
                    case "":
                        var kv = line.Split(new[] { '=' }, 2);
                        if (kv[0] == "name") m.Name = kv[1];
                        else if (kv[0] == "width") m.Width = int.Parse(kv[1]);
                        else if (kv[0] == "height") m.Height = int.Parse(kv[1]);
                        break;
                    case "[tiles]": tileRows.Add(line); break;
                    case "[owners]": ownerRows.Add(line); break;
                    case "[units]":
                        var p = line.Split(',');
                        m.Units.Add((p[0].Trim(), (Army)Enum.Parse(typeof(Army), p[1].Trim()), int.Parse(p[2]), int.Parse(p[3]), p.Length > 4 ? int.Parse(p[4]) : 100));
                        break;
                }
            }
            if (m.Width == 0) m.Width = tileRows[0].Length;
            if (m.Height == 0) m.Height = tileRows.Count;
            m.Tiles = new int[m.Width * m.Height];
            m.Owners = new Army[m.Width * m.Height];
            for (int y = 0; y < m.Height; y++)
                for (int x = 0; x < m.Width; x++)
                {
                    m.Tiles[y * m.Width + x] = data.TerrainByChar(tileRows[y][x]).Index;
                    char o = y < ownerRows.Count && x < ownerRows[y].Length ? ownerRows[y][x] : '.';
                    m.Owners[y * m.Width + x] = o == 'r' ? Army.Red : o == 'b' ? Army.Blue : Army.None;
                }
            return m;
        }
    }

    /// <summary>All mutable game state. Only RulesEngine changes it.</summary>
    public sealed class GameState
    {
        public GameData Data;
        public int Width, Height;
        public int[] Terrain;
        public Army[] Owner;
        public int[] CaptureProgress;
        public int[] CapturingUnit;
        public List<UnitState> Units = new List<UnitState>();
        public int[] Funds = new int[2];
        public bool[] ResupplyUsed = new bool[2];
        public bool[] HadUnits = new bool[2];
        public int Day = 1;
        public Army Active = Army.Red;
        public Rng Rng;
        public int NextUnitId = 1;
        public bool GameOver;
        public Army Winner = Army.None;
        /// <summary>Damage taken multiplier (%) per army; IQ200 COM uses &lt;100.</summary>
        public int[] DamageTakenPct = { 100, 100 };

        public Rules Rules => Data.Rules;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public int Index(int x, int y) => y * Width + x;
        public TerrainDef TerrainAt(int x, int y) => Data.Terrains[Terrain[Index(x, y)]];
        public UnitDef Def(UnitState u) => Data.Units[u.Type];
        public UnitState UnitById(int id) { foreach (var u in Units) if (u.Id == id) return u; return null; }

        /// <summary>The unit standing on a tile (carried units are not on the map).</summary>
        public UnitState UnitAt(int x, int y)
        {
            foreach (var u in Units) if (!u.IsCarried && u.X == x && u.Y == y) return u;
            return null;
        }

        public static GameState Create(GameData data, MapDef map, uint seed)
        {
            var s = new GameState
            {
                Data = data, Width = map.Width, Height = map.Height,
                Terrain = (int[])map.Tiles.Clone(), Owner = (Army[])map.Owners.Clone(),
                CaptureProgress = new int[map.Tiles.Length], CapturingUnit = new int[map.Tiles.Length],
                Rng = new Rng(seed)
            };
            for (int i = 0; i < s.CapturingUnit.Length; i++) s.CapturingUnit[i] = -1;
            s.Funds[0] = s.Funds[1] = data.Rules.StartFunds;
            foreach (var mu in map.Units) s.SpawnUnit(data.Unit(mu.type).Index, mu.army, mu.x, mu.y, mu.hp, false);
            return s;
        }

        public UnitState SpawnUnit(int type, Army army, int x, int y, int hp, bool acted)
        {
            var d = Data.Units[type];
            var u = new UnitState { Id = NextUnitId++, Type = type, Army = army, X = x, Y = y, Hp = hp, Fuel = d.Fuel, Ammo = d.Ammo, Acted = acted };
            Units.Add(u);
            if (army != Army.None) HadUnits[(int)army] = true;
            return u;
        }

        public GameState Clone()
        {
            var s = (GameState)MemberwiseClone();
            s.Terrain = (int[])Terrain.Clone();
            s.Owner = (Army[])Owner.Clone();
            s.CaptureProgress = (int[])CaptureProgress.Clone();
            s.CapturingUnit = (int[])CapturingUnit.Clone();
            s.Funds = (int[])Funds.Clone();
            s.ResupplyUsed = (bool[])ResupplyUsed.Clone();
            s.HadUnits = (bool[])HadUnits.Clone();
            s.DamageTakenPct = (int[])DamageTakenPct.Clone();
            s.Units = new List<UnitState>(Units.Count);
            foreach (var u in Units) s.Units.Add(u.Clone());
            return s;
        }
    }
}
