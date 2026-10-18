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
        /// <summary>Armies in this map (2 or more): the highest army that owns a tile or a unit, plus one.</summary>
        public int Players = 2;
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
                    m.Owners[y * m.Width + x] = Armies.FromLetter(o);
                }
            int top = (int)Army.Blue;
            foreach (var a in m.Owners) top = Math.Max(top, (int)a);
            foreach (var u in m.Units) top = Math.Max(top, (int)u.army);
            m.Players = top + 1;
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
        /// <summary>Armies taking part: 0..Players-1, in turn order.</summary>
        public int Players = 2;
        public int[] Funds = new int[2];
        public bool[] ResupplyUsed = new bool[2];
        public bool[] HadUnits = new bool[2];
        /// <summary>Armies knocked out of the game (base taken, routed or surrendered).</summary>
        public bool[] Out = new bool[2];
        /// <summary>Knocked-out armies, first out first: the ranking from the bottom.</summary>
        public List<Army> Eliminated = new List<Army>();
        public int Day = 1;
        public Army Active = Army.Red;
        public Rng Rng;
        public int NextUnitId = 1;
        public bool GameOver;
        public Army Winner = Army.None;
        /// <summary>Damage taken multiplier (%) per army; IQ200 COM uses &lt;100.</summary>
        public int[] DamageTakenPct = { 100, 100 };

        // ---- 内政 (only used when Data.Economy is set) ----
        /// <summary>Facility level per tile (1..Economy.MaxLevel; 0 on tiles that are not facilities).</summary>
        public int[] Level;
        /// <summary>Reservation per tile: 0 none, ReserveLevelUp, or ReserveConvertBase + target terrain index. Applied at the owner's next phase.</summary>
        public int[] Reserve;
        /// <summary>Funds paid for the reservation on the tile (given back only when its owner takes it back).</summary>
        public int[] ReservePaid;
        /// <summary>Per army: the one reservation it may still take back (-1 none). A newer reservation fixes the older ones.</summary>
        public int[] LastReserve = { -1, -1 };
        /// <summary>Phase key (Day*Players+army) in which a facility already gave its "ready at once" unit.</summary>
        public int[] FreeActUsed;
        /// <summary>The 内政 time after a phase played by a COM: Active is the army that has just finished; only 内政 is allowed.</summary>
        public bool Interlude;
        /// <summary>Per army: pressed 「内政終了」 in this 内政 time (shown to everyone; the server ends it).</summary>
        public bool[] InterludeReady = new bool[2];

        public const int ReserveLevelUp = 1, ReserveConvertBase = 100;
        public bool Domestic => Data.Economy != null;

        public bool IsOut(Army a) => a == Army.None || Out[(int)a];
        public int AliveCount { get { int n = 0; for (int a = 0; a < Players; a++) if (!Out[a]) n++; return n; } }
        /// <summary>The other armies still in the game.</summary>
        public IEnumerable<Army> Enemies(Army me) { for (int a = 0; a < Players; a++) if (a != (int)me && !Out[a]) yield return (Army)a; }

        void SizeFor(int players)
        {
            Players = players;
            Funds = new int[players]; ResupplyUsed = new bool[players]; HadUnits = new bool[players]; Out = new bool[players];
            DamageTakenPct = new int[players];
            for (int a = 0; a < players; a++) DamageTakenPct[a] = 100;
            LastReserve = new int[players];
            for (int a = 0; a < players; a++) LastReserve[a] = -1;
            InterludeReady = new bool[players];
        }

        public Rules Rules => Data.Rules;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public int Index(int x, int y) => y * Width + x;
        public TerrainDef TerrainAt(int x, int y) => Data.Terrains[Terrain[Index(x, y)]];
        public UnitDef Def(UnitState u) => Data.Units[u.Type];
        public int LevelAt(int x, int y) => Level[Index(x, y)];
        /// <summary>Phase key: one number per (day, army).</summary>
        public int PhaseKey => Day * Players + (int)Active;
        public UnitState UnitById(int id) { foreach (var u in Units) if (u.Id == id) return u; return null; }

        /// <summary>The unit standing on a tile (carried units are not on the map).</summary>
        public UnitState UnitAt(int x, int y)
        {
            foreach (var u in Units) if (!u.IsCarried && u.X == x && u.Y == y) return u;
            return null;
        }

        /// <summary>
        /// A new game on the map. seated (optional, one entry per army of the map): armies left out
        /// (false) do not play: they start out of the game, with their properties neutral and no units.
        /// </summary>
        public static GameState Create(GameData data, MapDef map, uint seed, IList<bool> seated = null)
        {
            var s = new GameState
            {
                Data = data, Width = map.Width, Height = map.Height,
                Terrain = (int[])map.Tiles.Clone(), Owner = (Army[])map.Owners.Clone(),
                CaptureProgress = new int[map.Tiles.Length], CapturingUnit = new int[map.Tiles.Length],
                Rng = new Rng(seed)
            };
            for (int i = 0; i < s.CapturingUnit.Length; i++) s.CapturingUnit[i] = -1;
            s.Level = new int[map.Tiles.Length];
            for (int i = 0; i < s.Level.Length; i++) s.Level[i] = data.Terrains[s.Terrain[i]].IsProperty ? 1 : 0;
            s.Reserve = new int[map.Tiles.Length];
            s.ReservePaid = new int[map.Tiles.Length];
            s.FreeActUsed = new int[map.Tiles.Length];
            for (int i = 0; i < s.FreeActUsed.Length; i++) s.FreeActUsed[i] = -1;
            s.SizeFor(Math.Max(2, map.Players));
            for (int a = 0; a < s.Players; a++) s.Funds[a] = data.Rules.StartFunds;
            if (seated != null)
                for (int a = 0; a < s.Players; a++)
                    if (a >= seated.Count || !seated[a])
                    {
                        s.Out[a] = true;
                        for (int i = 0; i < s.Owner.Length; i++) if (s.Owner[i] == (Army)a) s.Owner[i] = Army.None;
                    }
            foreach (var mu in map.Units)
                if (mu.army == Army.None || !s.Out[(int)mu.army]) s.SpawnUnit(data.Unit(mu.type).Index, mu.army, mu.x, mu.y, mu.hp, false);
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
            s.Out = (bool[])Out.Clone();
            s.Level = (int[])Level.Clone();
            s.Reserve = (int[])Reserve.Clone();
            s.ReservePaid = (int[])ReservePaid.Clone();
            s.LastReserve = (int[])LastReserve.Clone();
            s.FreeActUsed = (int[])FreeActUsed.Clone();
            s.InterludeReady = (bool[])InterludeReady.Clone();
            s.Eliminated = new List<Army>(Eliminated);
            s.Units = new List<UnitState>(Units.Count);
            foreach (var u in Units) s.Units.Add(u.Clone());
            return s;
        }
    }
}
