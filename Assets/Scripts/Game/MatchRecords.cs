using System;
using System.Collections.Generic;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// One finished match: the map and every command with the random seed it was played with, so the
    /// whole game can be replayed exactly (combat rolls included).
    /// </summary>
    [Serializable]
    public sealed class MatchRecord
    {
        public string mapId, date, red, blue, result;
        public bool online;
        public string[] cmds;
        public int[] seeds;          // uint stored bit for bit (JsonUtility-friendly)

        public uint Seed(int i) => unchecked((uint)seeds[i]);
    }

    /// <summary>The most recent matches, kept in this browser / machine (PlayerPrefs).</summary>
    public static class MatchRecords
    {
        public const int Max = 8;
        const string CountKey = "fw.rec.count", ItemKey = "fw.rec.";

        public static List<MatchRecord> Load()
        {
            var list = new List<MatchRecord>();
            try
            {
                int n = PlayerPrefs.GetInt(CountKey, 0);
                for (int i = 0; i < n; i++)
                {
                    var json = PlayerPrefs.GetString(ItemKey + i, "");
                    if (string.IsNullOrEmpty(json)) continue;
                    var r = JsonUtility.FromJson<MatchRecord>(json);
                    if (r != null && r.cmds != null && r.seeds != null && r.cmds.Length == r.seeds.Length) list.Add(r);
                }
            }
            catch (Exception e) { Debug.LogWarning("[FW] records: " + e.Message); }
            return list;
        }

        /// <summary>Writes the list; if the browser's storage is full, the oldest matches are dropped until it fits.</summary>
        static void Store(List<MatchRecord> list)
        {
            int old = 0;
            try { old = PlayerPrefs.GetInt(CountKey, 0); } catch (Exception) { }
            while (true)
            {
                try
                {
                    for (int i = 0; i < list.Count; i++) PlayerPrefs.SetString(ItemKey + i, JsonUtility.ToJson(list[i]));
                    for (int i = list.Count; i < old; i++) PlayerPrefs.DeleteKey(ItemKey + i);
                    PlayerPrefs.SetInt(CountKey, list.Count);
                    PlayerPrefs.Save();
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[FW] records: " + e.Message);
                    if (list.Count <= 1) return;
                    list.RemoveAt(list.Count - 1);
                }
            }
        }

        /// <summary>Adds a match (newest first). The same match saved twice (e.g. after a rejoin) is kept once.</summary>
        public static void Add(MatchRecord r)
        {
            var list = Load();
            list.RemoveAll(x => Same(x, r));
            list.Insert(0, r);
            while (list.Count > Max) list.RemoveAt(list.Count - 1);
            Store(list);
        }

        public static void Delete(int index)
        {
            var list = Load();
            if (index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            Store(list);
        }

        static bool Same(MatchRecord a, MatchRecord b)
        {
            if (a.mapId != b.mapId || a.cmds.Length != b.cmds.Length || a.cmds.Length == 0) return false;
            int n = Math.Min(8, a.seeds.Length);
            for (int i = 0; i < n; i++) if (a.seeds[i] != b.seeds[i]) return false;
            return a.seeds[a.seeds.Length - 1] == b.seeds[b.seeds.Length - 1];
        }
    }
}
