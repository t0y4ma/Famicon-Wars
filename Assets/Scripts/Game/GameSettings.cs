using System;
using System.Collections.Generic;
using FamiconWars.Core;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// Options chosen before a match. Saved to PlayerPrefs (browser storage on WebGL) as JSON.
    /// Sound fields are stored and editable now but not wired to audio yet (WIP).
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        public string MapId = "map01";
        /// <summary>Per army (Armies.Max entries): 0 = human, 1..4 = COM level, -1 = nobody (only on maps for 3 or more).</summary>
        public int[] Players = DefaultPlayers();
        /// <summary>Per army, for a COM: 0 = random 戦法 (kept secret), 1 速攻, 2 物量, 3 精鋭.</summary>
        public int[] Styles = new int[Armies.Max];
        /// <summary>Time for one human phase in seconds (TurnTime): 0 = no limit.</summary>
        public int TurnSeconds = TurnTime.DefaultSeconds;
        public const int Nobody = -1;

        static int[] DefaultPlayers()
        {
            var p = new int[Armies.Max];
            for (int i = 1; i < p.Length; i++) p[i] = 2;   // you against COM 普通
            return p;
        }
        /// <summary>0 ゆっくり, 1 ふつう, 2 はやい, 3 最速.</summary>
        public int Speed = 1;
        public bool BattleAnimation = true;
        /// <summary>Exact numbers (machines left after an attack, damage in the matchup chart) or words only.</summary>
        public bool ShowNumbers = true;
        /// <summary>Moving the cursor to a screen edge scrolls the board.</summary>
        public bool EdgeScroll = true;

        // ---- WIP: sound (properties only) ----
        public bool BgmEnabled = true;
        public int BgmVolume = 75;   // 0..100
        public int SeVolume = 75;    // 0..100

        public static readonly string[] SpeedNames = { "ゆっくり", "ふつう", "はやい", "最速" };
        /// <summary>Pause between COM actions (seconds).</summary>
        public static readonly float[] SpeedDelay = { 0.9f, 0.45f, 0.15f, 0f };
        /// <summary>Battle cut-in duration multiplier.</summary>
        public static readonly float[] BattleTime = { 1.3f, 1f, 0.65f, 0.4f };

        const string Key = "fw.settings.v1";

        public static GameSettings Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var s = JsonUtility.FromJson<GameSettings>(json);
                    if (s != null) { s.Sanitize(); return s; }
                }
            }
            catch (Exception) { /* corrupted or unavailable storage: fall back to defaults */ }
            return new GameSettings();
        }

        public void Save()
        {
            try { PlayerPrefs.SetString(Key, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }
            catch (Exception) { /* storage may be blocked in a private browser window */ }
        }

        void Sanitize()
        {
            // older saves have two armies: keep their choices and add the rest
            var p = DefaultPlayers();
            if (Players != null) for (int i = 0; i < Math.Min(p.Length, Players.Length); i++) p[i] = Mathf.Clamp(Players[i], Nobody, 4);
            Players = p;
            var st = new int[Armies.Max];
            if (Styles != null) for (int i = 0; i < Math.Min(st.Length, Styles.Length); i++) st[i] = Mathf.Clamp(Styles[i], 0, 3);
            Styles = st;
            TurnSeconds = TurnTime.Clamp(TurnSeconds);
            Speed = Mathf.Clamp(Speed, 0, SpeedNames.Length - 1);
            BgmVolume = Mathf.Clamp(BgmVolume, 0, 100);
            SeVolume = Mathf.Clamp(SeVolume, 0, 100);
            if (MapCatalog.Find(MapId) == null || !MapCatalog.Find(MapId).Ready) MapId = "map01";
        }

        /// <summary>Who takes part on a map for this many armies: at least two, an empty seat counts as nobody.</summary>
        public bool[] Seated(int players)
        {
            var seated = new bool[players];
            int n = 0;
            for (int a = 0; a < players; a++) { seated[a] = Players[a] != Nobody; if (seated[a]) n++; }
            // two armies always play (a two-army map has no empty seats)
            for (int a = 0; a < players && n < 2; a++) if (!seated[a]) { seated[a] = true; Players[a] = 2; n++; }
            return seated;
        }
    }

    public sealed class MapEntry
    {
        public string Id, Name, Note;
        public int Width, Height;
        /// <summary>Armies on the map (bases).</summary>
        public int Players = 2;
        public bool Ready;
    }

    /// <summary>
    /// Maps shown on the map-select screen: the built-in list below (its order and notes), then any other
    /// map file in Resources/Maps (made with the map editor). A file's own header (name=, note=, size)
    /// takes precedence over the list. Unready entries are placeholders (WIP).
    /// </summary>
    public static class MapCatalog
    {
        static MapEntry[] all;
        public static MapEntry[] All => all ??= Load();

        /// <summary>Read the map files again (after the map editor saved one).</summary>
        public static void Reload() => all = null;

        static readonly MapEntry[] Builtin =
        {
            new MapEntry { Id = "map01", Name = "演習島", Width = 18, Height = 12, Ready = true,
                Note = "川で分かれた2つの陸地を3本の橋がつなぐ。最初の一戦に。" },
            new MapEntry { Id = "map02", Name = "ふたつの海峡", Width = 22, Height = 14, Ready = true,
                Note = "海峡が陸を分ける。両端と中央の島を通る橋で渡る。港が8つあり、戦艦で海から撃ち合える。" },
            new MapEntry { Id = "map03", Name = "山岳回廊", Width = 20, Height = 14, Ready = true,
                Note = "山が点在する3本の広い道。中央の都市の取り合い。" },
            new MapEntry { Id = "map04", Name = "群島決戦", Width = 24, Height = 16, Ready = true,
                Note = "中央の島へ広い橋、外周に長い橋。港の前の海から戦艦で島と橋を狙える。" },
            new MapEntry { Id = "map05", Name = "対戦用 A", Width = 20, Height = 14, Ready = true,
                Note = "点対称の平原。都市は中央に多く、道は一本。後手は都市が1つ多い。" },
            new MapEntry { Id = "map06", Name = "対戦用 B", Width = 22, Height = 15, Ready = true,
                Note = "中央を川が横切る点対称マップ。橋と浅瀬で10か所から渡れ、川べりの都市を奪い合う。" },
            new MapEntry { Id = "map07", Name = "四つ巴", Width = 22, Height = 22, Players = 4, Ready = true,
                Note = "4軍が四隅から。どの軍も同じ地形。中央の4都市の取り合い。2〜4人で遊べる。" },
            new MapEntry { Id = "map08", Name = "三国峠", Width = 23, Height = 16, Players = 3, Ready = true,
                Note = "3軍の基地はどれも同じ距離。下の軍は2軍に挟まれやすい代わりに、基地の近くに空港が2つある。" },
        };

        static MapEntry[] Load()
        {
            var files = new Dictionary<string, string>();
            foreach (var ta in Resources.LoadAll<TextAsset>("Maps")) files[ta.name] = ta.text;
            var list = new List<MapEntry>();
            foreach (var b in Builtin)
            {
                var e = new MapEntry { Id = b.Id, Name = b.Name, Note = b.Note, Width = b.Width, Height = b.Height, Players = b.Players, Ready = b.Ready };
                if (files.TryGetValue(b.Id, out var text)) ReadHeader(e, text);
                else if (files.Count > 0) continue;            // the file is gone
                list.Add(e);
            }
            var extra = new List<string>(files.Keys);
            extra.Sort(StringComparer.Ordinal);
            foreach (var id in extra)
            {
                if (list.Exists(m => m.Id == id)) continue;
                var e = new MapEntry { Id = id, Name = id, Note = "", Ready = true };
                ReadHeader(e, files[id]);
                list.Add(e);
            }
            return list.ToArray();
        }

        /// <summary>name=, note=, width=, height= and the number of armies (owner letters and units).</summary>
        public static void ReadHeader(MapEntry e, string text)
        {
            string section = "";
            int rows = 0, cols = 0, top = 1;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimStart('\uFEFF').TrimEnd();
                if (line.Length == 0) continue;
                if (line.StartsWith("[")) { section = line; continue; }
                if (section == "")
                {
                    int k = line.IndexOf('=');
                    if (k < 0) continue;
                    string key = line.Substring(0, k), v = line.Substring(k + 1);
                    if (key == "name" && v.Length > 0) e.Name = v;
                    else if (key == "note") e.Note = v;
                    else if (key == "width" && int.TryParse(v, out var w)) e.Width = w;
                    else if (key == "height" && int.TryParse(v, out var h)) e.Height = h;
                }
                else if (section == "[tiles]") { rows++; cols = Math.Max(cols, line.Length); }
                else if (section == "[owners]") { foreach (char c in line) top = Math.Max(top, (int)Armies.FromLetter(c)); }
                else if (section == "[units]")
                {
                    var p = line.Split(',');
                    if (p.Length > 1 && Enum.TryParse<Army>(p[1].Trim(), out var a)) top = Math.Max(top, (int)a);
                }
            }
            if (e.Width == 0) e.Width = cols;
            if (e.Height == 0) e.Height = rows;
            e.Players = Math.Max(2, top + 1);
        }

        public static MapEntry Find(string id)
        {
            foreach (var m in All) if (m.Id == id) return m;
            return null;
        }
    }
}
