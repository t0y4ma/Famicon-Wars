using System;
using System.Collections.Generic;
using FamiconWars.Core;
using FamiconWars.Game;
using UnityEditor;
using UnityEngine;

namespace FamiconWars.EditorTools
{
    /// <summary>
    /// 「内政・生産の設定」: which facility makes which unit from which level (production.csv), and the
    /// other 内政 numbers (economy.csv: costs, income factor, level drops, …). Saved straight into Resources/Data.
    /// </summary>
    public class FwProductionWindow : EditorWindow
    {
        [MenuItem("フィールド・コマンド/内政・生産の設定")]
        static void Open() => GetWindow<FwProductionWindow>("内政・生産の設定").minSize = new Vector2(760, 480);

        GameData data;
        int[,] unlock;
        readonly List<TerrainDef> cols = new List<TerrainDef>();
        string[] econHeader;
        List<string[]> econ;
        Vector2 scroll;
        bool dirty;
        string message;
        int tab;

        void OnEnable() => Load();

        void Load()
        {
            try
            {
                data = FwData.LoadGame();
                unlock = (int[,])data.Economy.Unlock.Clone();
                cols.Clear();
                foreach (var t in data.Terrains) if (t.Produces != null) cols.Add(t);
                econ = FwData.Table(FwData.Read("economy"), out econHeader);
                dirty = false; message = null;
            }
            catch (Exception e) { data = null; message = "読み込めませんでした: " + e.Message; }
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(!dirty || data == null))
                    if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(70))) Save();
                if (GUILayout.Button("読み直す", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    if (!dirty || EditorUtility.DisplayDialog("読み直す", "保存していない変更は失われます。", "読み直す", "やめる")) Load();
                GUILayout.Space(12);
                tab = GUILayout.Toolbar(tab, new[] { "生産できる部隊", "内政の数値" }, EditorStyles.toolbarButton, GUILayout.Width(260));
                GUILayout.FlexibleSpace();
                if (dirty) GUILayout.Label("未保存の変更があります", EditorStyles.boldLabel);
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            if (data == null) return;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 0) DrawProduction(); else DrawEconomy();
            EditorGUILayout.EndScrollView();
        }

        void DrawProduction()
        {
            EditorGUILayout.HelpBox("数字 = その施設がこのレベルになると作れる部隊(1 = 最初から)。「—」= その施設では作れない。\n" +
                                    "基地は最初から全部作れるのが標準です。都市はレベルを上げても作れる部隊は増えません(ここで決めた分だけ)。", MessageType.None);
            int max = Mathf.Max(1, data.Economy.MaxLevel);
            var options = new string[max + 1];
            options[0] = "—";
            for (int i = 1; i <= max; i++) options[i] = "Lv" + i;
            const float nameW = 170, colW = 92;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("部隊", EditorStyles.boldLabel, GUILayout.Width(nameW));
                foreach (var t in cols) GUILayout.Label(Labels.Get(t.NameKey), EditorStyles.boldLabel, GUILayout.Width(colW));
            }
            Domain? last = null;
            foreach (var u in data.Units)
            {
                if (last != u.Domain)
                {
                    last = u.Domain;
                    GUILayout.Space(6);
                    GUILayout.Label(u.Domain == Domain.Ground ? "陸" : u.Domain == Domain.Air ? "空" : "海", EditorStyles.miniBoldLabel);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Labels.Get(u.NameKey) + "  (" + u.Id + ")", GUILayout.Width(nameW));
                    foreach (var t in cols)
                    {
                        if (t.Produces.Value != u.Domain) { GUILayout.Label("", GUILayout.Width(colW)); continue; }
                        int v = Mathf.Clamp(unlock[t.Index, u.Index], 0, max);
                        int nv = EditorGUILayout.Popup(v, options, GUILayout.Width(colW - 6));
                        if (nv != v) { unlock[t.Index, u.Index] = nv; dirty = true; }
                    }
                }
            }
        }

        static readonly string[] TimingValues = { "offturn", "interlude" };
        static readonly string[] TimingNames = { "相手の手番中と内政の時間", "内政の時間だけ" };

        string Title(string key)
        {
            string Fac(string id) { try { return Labels.Get(data.Terrain(id).NameKey); } catch (KeyNotFoundException) { return id; } }
            if (key.StartsWith("cost.")) return Fac(key.Substring(5)) + "のレベルアップ費用";
            if (key.StartsWith("convert.")) return Fac(key.Substring(8)) + "への転向費用";
            switch (key)
            {
                case "maxLevel": return "最大レベル";
                case "incomeFactor": return "1レベルごとの収入の増え方";
                case "hqRadius": return "基地の生産範囲";
                case "convertFrom": return "転向できる施設";
                case "convertMinLevel": return "転向に必要なレベル";
                case "convertLevelDrop": return "転向で下がるレベル";
                case "captureLevelDrop": return "占領で下がるレベル";
                case "freeActLevel": return "すぐ動ける特典のレベル";
                case "freeActTerrains": return "特典がある施設";
                case "timing": return "内政できる時";
                case "interludeVote": return "内政の時間を終える割合";
            }
            return key;
        }

        void DrawEconomy()
        {
            EditorGUILayout.HelpBox("economy.csv の値です。", MessageType.None);
            int ki = Array.IndexOf(econHeader, "key"), vi = Array.IndexOf(econHeader, "value"), ni = Array.IndexOf(econHeader, "note");
            if (ki < 0 || vi < 0) { EditorGUILayout.HelpBox("economy.csv の見出しが key,value,note ではありません", MessageType.Error); return; }
            int max = 4;
            foreach (var row in econ) if (row[ki] == "maxLevel" && int.TryParse(row[vi], out var m)) max = Mathf.Clamp(m, 1, 10);
            var facilities = new List<TerrainDef>();
            foreach (var t in data.Terrains) if (t.IsProperty) facilities.Add(t);
            foreach (var row in econ)
            {
                string key = row[ki], v = row[vi], nv = v;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(Title(key), EditorStyles.boldLabel, GUILayout.Width(230));
                        if (key.StartsWith("cost.")) nv = LevelFields(v, 2, max, false);
                        else if (key == "hqRadius") nv = LevelFields(v, 1, max, true);
                        else if (key == "freeActTerrains")
                        {
                            var set = new HashSet<string>(v.Split('|'));
                            foreach (var t in facilities)
                            {
                                bool on = set.Contains(t.Id);
                                bool non = GUILayout.Toggle(on, Labels.Get(t.NameKey), "Button", GUILayout.Width(64));
                                if (non) set.Add(t.Id); else set.Remove(t.Id);
                            }
                            var picked = new List<string>();
                            foreach (var t in facilities) if (set.Contains(t.Id)) picked.Add(t.Id);
                            nv = string.Join("|", picked);
                        }
                        else if (key == "convertFrom")
                        {
                            int idx = Mathf.Max(0, facilities.FindIndex(t => t.Id == v));
                            var names = facilities.ConvertAll(t => Labels.Get(t.NameKey)).ToArray();
                            nv = facilities[EditorGUILayout.Popup(idx, names, GUILayout.Width(160))].Id;
                        }
                        else if (key == "timing")
                            nv = TimingValues[EditorGUILayout.Popup(Mathf.Max(0, Array.IndexOf(TimingValues, v)), TimingNames, GUILayout.Width(220))];
                        else if (key == "freeActLevel")
                        {
                            var opts = new string[max + 1];
                            opts[0] = "なし";
                            for (int i = 1; i <= max; i++) opts[i] = "Lv" + i + "以上";
                            int.TryParse(v, out var fl);
                            nv = EditorGUILayout.Popup(Mathf.Clamp(fl, 0, max), opts, GUILayout.Width(120)).ToString();
                        }
                        else if (key == "incomeFactor" || key == "interludeVote")
                        {
                            float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f);
                            nv = EditorGUILayout.FloatField(f, GUILayout.Width(80)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else if (int.TryParse(v, out var iv)) nv = EditorGUILayout.IntField(iv, GUILayout.Width(80)).ToString();
                        else nv = FwData.Clean(EditorGUILayout.TextField(v, GUILayout.Width(200)));
                        GUILayout.FlexibleSpace();
                    }
                    if (ni >= 0 && row[ni].Length > 0) GUILayout.Label(row[ni], EditorStyles.wordWrappedMiniLabel);
                }
                if (nv != v) { row[vi] = nv; dirty = true; }
            }
        }

        /// <summary>One field per level (from Lv first to Lv max) side by side; a "|" list in the file.</summary>
        static string LevelFields(string v, int first, int max, bool anywhere)
        {
            var parts = new List<int>();
            foreach (var p in v.Split('|')) parts.Add(int.TryParse(p.Trim(), out var n) ? n : 0);
            int count = Mathf.Max(0, max - first + 1);
            while (parts.Count < count) parts.Add(parts.Count > 0 ? parts[parts.Count - 1] : 0);
            if (parts.Count > count) parts.RemoveRange(count, parts.Count - count);
            for (int i = 0; i < count; i++)
            {
                GUILayout.Label("Lv" + (first + i), GUILayout.Width(28));
                if (anywhere)
                {
                    bool any = parts[i] < 0;
                    bool nany = GUILayout.Toggle(any, "どこでも", GUILayout.Width(68));
                    if (nany != any) parts[i] = nany ? -1 : 2;
                    using (new EditorGUI.DisabledScope(nany))
                    {
                        int nvv = EditorGUILayout.IntField(Mathf.Max(0, parts[i]), GUILayout.Width(40));
                        if (!nany) parts[i] = Mathf.Max(0, nvv);
                    }
                }
                else parts[i] = Mathf.Max(0, EditorGUILayout.IntField(parts[i], GUILayout.Width(70)));
                GUILayout.Space(6);
            }
            return string.Join("|", parts);
        }

        void Save()
        {
            string prodText = Economy.ProductionCsv(data, unlock);
            string econText = FwData.Join(econHeader, econ);
            try
            {
                // check that the game can read it before writing anything
                FwData.LoadGame(new Dictionary<string, string> { { "economy", econText }, { "production", prodText } });
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("保存できません", "値の形が正しくありません:\n" + e.Message, "OK");
                return;
            }
            FwData.Write("production", prodText);
            FwData.Write("economy", econText);
            dirty = false;
            message = "保存しました(" + DateTime.Now.ToString("HH:mm:ss") + ")。次の対戦から反映されます。";
            Load();
            message = "保存しました(" + DateTime.Now.ToString("HH:mm:ss") + ")。次の対戦から反映されます。";
        }
    }
}
