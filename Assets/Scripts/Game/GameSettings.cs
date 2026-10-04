using System;
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
        /// <summary>Per army: 0 = human, 1..4 = COM level.</summary>
        public int[] Players = { 0, 2 };
        /// <summary>0 ゆっくり, 1 ふつう, 2 はやい, 3 最速.</summary>
        public int Speed = 1;
        public bool BattleAnimation = true;

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
            if (Players == null || Players.Length != 2) Players = new[] { 0, 2 };
            for (int i = 0; i < 2; i++) Players[i] = Mathf.Clamp(Players[i], 0, 4);
            Speed = Mathf.Clamp(Speed, 0, SpeedNames.Length - 1);
            BgmVolume = Mathf.Clamp(BgmVolume, 0, 100);
            SeVolume = Mathf.Clamp(SeVolume, 0, 100);
            if (MapCatalog.Find(MapId) == null || !MapCatalog.Find(MapId).Ready) MapId = "map01";
        }
    }

    public sealed class MapEntry
    {
        public string Id, Name, Note;
        public int Width, Height;
        public bool Ready;
    }

    /// <summary>Maps shown on the map-select screen. Unready entries are placeholders (WIP).</summary>
    public static class MapCatalog
    {
        public static readonly MapEntry[] All =
        {
            new MapEntry { Id = "map01", Name = "演習島", Width = 18, Height = 12, Ready = true,
                Note = "川で分かれた2つの陸地を3本の橋がつなぐ。最初の一戦に。" },
            new MapEntry { Id = "wip02", Name = "ふたつの海峡", Note = "輸送と上陸がテーマ(制作中)" },
            new MapEntry { Id = "wip03", Name = "山岳回廊", Note = "歩兵と間接攻撃がテーマ(制作中)" },
            new MapEntry { Id = "wip04", Name = "群島決戦", Note = "航空・海上の総力戦(制作中)" },
            new MapEntry { Id = "wip05", Name = "対戦用 A", Note = "点対称の対戦マップ(制作中)" },
            new MapEntry { Id = "wip06", Name = "対戦用 B", Note = "点対称の対戦マップ(制作中)" },
        };

        public static MapEntry Find(string id)
        {
            foreach (var m in All) if (m.Id == id) return m;
            return null;
        }
    }
}
