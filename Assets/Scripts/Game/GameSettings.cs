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
        /// <summary>Per army, for a COM: 0 = random 戦法 (kept secret), 1 速攻, 2 物量, 3 精鋭.</summary>
        public int[] Styles = { 0, 0 };
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
            if (Styles == null || Styles.Length != 2) Styles = new[] { 0, 0 };
            for (int i = 0; i < 2; i++) Styles[i] = Mathf.Clamp(Styles[i], 0, 3);
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
        };

        public static MapEntry Find(string id)
        {
            foreach (var m in All) if (m.Id == id) return m;
            return null;
        }
    }
}
