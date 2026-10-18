using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FamiconWars.Core;
using FamiconWars.Game;
using UnityEditor;
using UnityEngine;

namespace FamiconWars.EditorTools
{
    /// <summary>
    /// 「部隊の能力・相性」: units.csv (name, id, picture, price, move, fuel, ammo, range …) and damage.csv
    /// (base damage of each attacker against each defender; empty = cannot attack). Units can be added and
    /// removed; a changed or removed id is carried into damage.csv, production.csv and the map files on save.
    /// </summary>
    public class FwUnitWindow : EditorWindow
    {
        [MenuItem("フィールド・コマンド/部隊の能力・相性")]
        static void Open() => GetWindow<FwUnitWindow>("部隊の能力・相性").minSize = new Vector2(900, 480);

        static readonly Dictionary<string, string> ColumnNames = new Dictionary<string, string>
        {
            { "shortName", "略称" }, { "price", "価格" }, { "move", "移動力" },
            { "moveClass", "移動タイプ" }, { "fuel", "燃料" }, { "fuelPerPhase", "燃料/手番" }, { "ammo", "弾" },
            { "ammoPrice", "弾の単価" }, { "rangeMin", "射程 最小" }, { "rangeMax", "射程 最大" }, { "capture", "占領" },
            { "cargoCapacity", "搭載数" }, { "cargoClass", "搭載できる" }, { "canSupply", "補給" },
        };
        static readonly string[] Hidden = { "id", "nameKey", "name", "icon" };
        static readonly string[] MoveClasses = { "Foot", "Vehicle", "Air", "Ship" };
        static readonly string[] MoveClassNames = { "歩兵", "車両", "航空", "艦船" };
        static readonly string[] CargoClasses = { "-", "Foot", "Ground" };
        static readonly string[] CargoClassNames = { "—", "歩兵", "地上部隊" };
        static readonly string[] CaptureNames = { "不可", "基地以外", "すべて" };

        string[] unitHeader, dmgHeader;
        List<string[]> units, dmg;
        /// <summary>Id each row had when the files were read ("" = added here).</summary>
        List<string> origIds;
        readonly List<string> removed = new List<string>();
        Vector2 scroll;
        int tab;
        bool dirty;
        string message;

        void OnEnable() => Load();

        void Load()
        {
            units = FwData.Table(FwData.Read("units"), out unitHeader);
            dmg = FwData.Table(FwData.Read("damage"), out dmgHeader);
            // older files: add the name and picture columns
            foreach (var col in new[] { "name", "icon" })
                if (Array.IndexOf(unitHeader, col) < 0)
                {
                    unitHeader = unitHeader.Concat(new[] { col }).ToArray();
                    for (int i = 0; i < units.Count; i++)
                    {
                        string v = col == "icon" ? units[i][Col("id")] : Labels.Get(units[i][Col("nameKey")]);
                        units[i] = units[i].Concat(new[] { v }).ToArray();
                    }
                }
            origIds = units.Select(r => r[Col("id")]).ToList();
            removed.Clear();
            dirty = false; message = null;
        }

        int Col(string h) => Array.IndexOf(unitHeader, h);
        int DmgCol(string id) => Array.IndexOf(dmgHeader, id);

        string NameOf(string id)
        {
            foreach (var r in units) if (r[Col("id")] == id) return r[Col("name")].Length > 0 ? r[Col("name")] : Labels.Get(r[Col("nameKey")]);
            return id;
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(!dirty))
                    if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(70))) Save();
                if (GUILayout.Button("読み直す", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    if (!dirty || EditorUtility.DisplayDialog("読み直す", "保存していない変更は失われます。", "読み直す", "やめる")) Load();
                if (GUILayout.Button("部隊を追加", EditorStyles.toolbarButton, GUILayout.Width(90))) AddUnit();
                GUILayout.Space(12);
                tab = GUILayout.Toolbar(tab, new[] { "能力", "相性(ダメージ)" }, EditorStyles.toolbarButton, GUILayout.Width(240));
                GUILayout.FlexibleSpace();
                if (dirty) GUILayout.Label("未保存の変更があります", EditorStyles.boldLabel);
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 0) DrawUnits(); else DrawDamage();
            EditorGUILayout.EndScrollView();
        }

        // ---------------- units ----------------

        void DrawUnits()
        {
            EditorGUILayout.HelpBox("IDを変えたり部隊を消したりすると、保存のときに相性・生産表・マップの部隊も合わせて書き換えます。\n" +
                                    "絵 = 盤面に出す絵(既存の部隊の絵から選ぶ)。燃料/手番 = 毎手番減る燃料。占領: 歩兵は「すべて」、工兵は「基地以外」。", MessageType.None);
            const float w = 62;
            var icons = UnitIcons.Order;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("", GUILayout.Width(22));
                GUILayout.Label("名前", EditorStyles.miniBoldLabel, GUILayout.Width(110));
                GUILayout.Label("ID", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                GUILayout.Label("絵", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                foreach (var h in unitHeader)
                {
                    if (Hidden.Contains(h)) continue;
                    GUILayout.Label(ColumnNames.TryGetValue(h, out var n) ? n : h, EditorStyles.miniBoldLabel, GUILayout.Width(w));
                }
            }
            int remove = -1;
            for (int ri = 0; ri < units.Count; ri++)
            {
                var r = units[ri];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("×", "この部隊を消す"), GUILayout.Width(22))) remove = ri;
                    var nn = FwData.Clean(EditorGUILayout.TextField(r[Col("name")], GUILayout.Width(110)));
                    if (nn != r[Col("name")]) { r[Col("name")] = nn; dirty = true; }
                    var nid = EditorGUILayout.DelayedTextField(r[Col("id")], GUILayout.Width(80));
                    if (nid != r[Col("id")]) RenameUnit(ri, nid);
                    int ic = Array.IndexOf(icons, r[Col("icon")]);
                    var iconNames = icons.Select(id => NameOrId(id)).ToArray();
                    int nic = EditorGUILayout.Popup(Mathf.Max(0, ic), iconNames, GUILayout.Width(80));
                    if (ic < 0 || nic != ic) { if (icons[nic] != r[Col("icon")]) { r[Col("icon")] = icons[nic]; dirty = true; } }
                    for (int c = 0; c < unitHeader.Length; c++)
                    {
                        string h = unitHeader[c], v = r[c], nv = v;
                        if (Hidden.Contains(h)) continue;
                        switch (h)
                        {
                            case "moveClass": nv = MoveClasses[EditorGUILayout.Popup(Mathf.Max(0, Array.IndexOf(MoveClasses, v)), MoveClassNames, GUILayout.Width(w))]; break;
                            case "cargoClass": nv = CargoClasses[EditorGUILayout.Popup(Mathf.Max(0, Array.IndexOf(CargoClasses, v)), CargoClassNames, GUILayout.Width(w))]; break;
                            case "capture": nv = EditorGUILayout.Popup(Mathf.Clamp(ToInt(v), 0, 2), CaptureNames, GUILayout.Width(w)).ToString(); break;
                            case "canSupply": nv = EditorGUILayout.Toggle(ToInt(v) == 1, GUILayout.Width(w)) ? "1" : "0"; break;
                            case "shortName": nv = FwData.Clean(EditorGUILayout.TextField(v, GUILayout.Width(w))); break;
                            default: nv = EditorGUILayout.IntField(ToInt(v), GUILayout.Width(w)).ToString(); break;
                        }
                        if (nv != v) { r[c] = nv; dirty = true; }
                    }
                }
            }
            if (remove >= 0) RemoveUnit(remove);
        }

        /// <summary>The built-in name of a picture id (from the original unit list).</summary>
        static string NameOrId(string iconId)
        {
            var n = Labels.Get("unit." + iconId.ToLowerInvariant());
            return n.StartsWith("unit.") ? iconId : n;
        }

        static string CleanId(string id) => new string((id ?? "").ToUpperInvariant().Where(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_').ToArray());

        void RenameUnit(int ri, string wanted)
        {
            string id = CleanId(wanted), old = units[ri][Col("id")];
            if (id.Length == 0 || id == old) return;
            if (units.Any(r => r[Col("id")] == id)) { EditorUtility.DisplayDialog("IDを変えられません", id + " はほかの部隊が使っています", "OK"); return; }
            units[ri][Col("id")] = id;
            units[ri][Col("nameKey")] = "unit." + id.ToLowerInvariant();
            // the matchup table follows at once (row and column)
            int ai = DmgCol("attacker");
            int dc = DmgCol(old);
            if (dc >= 0) dmgHeader[dc] = id;
            foreach (var r in dmg) if (r[ai] == old) r[ai] = id;
            dirty = true;
        }

        void AddUnit()
        {
            int n = 1;
            while (units.Any(r => r[Col("id")] == "NEW" + n)) n++;
            string id = "NEW" + n;
            // starts as a copy of the first unit (an infantry), to be edited
            var row = (string[])units[0].Clone();
            row[Col("id")] = id; row[Col("nameKey")] = "unit." + id.ToLowerInvariant(); row[Col("name")] = "新しい部隊" + n;
            units.Add(row); origIds.Add("");
            int ai = DmgCol("attacker");
            dmgHeader = dmgHeader.Concat(new[] { id }).ToArray();
            for (int i = 0; i < dmg.Count; i++) dmg[i] = dmg[i].Concat(new[] { "" }).ToArray();
            var drow = new string[dmgHeader.Length];
            for (int i = 0; i < drow.Length; i++) drow[i] = "";
            drow[ai] = id;
            dmg.Add(drow);
            dirty = true;
            message = id + " を追加しました。名前・IDと能力を決め、相性の表も埋めてください(生産表への登録は「内政・生産の設定」で)。";
        }

        void RemoveUnit(int ri)
        {
            string id = units[ri][Col("id")];
            var maps = MapsUsing(origIds[ri].Length > 0 ? origIds[ri] : id);
            string warn = maps.Count > 0 ? "\n\nマップ " + string.Join("、", maps) + " に置かれたこの部隊も、保存のときに消えます。" : "";
            if (!EditorUtility.DisplayDialog("部隊を消す", NameOf(id) + "(" + id + ")を消しますか?" + warn, "消す", "やめる")) return;
            if (origIds[ri].Length > 0) removed.Add(origIds[ri]);
            units.RemoveAt(ri); origIds.RemoveAt(ri);
            int ai = DmgCol("attacker"), dc = DmgCol(id);
            dmg.RemoveAll(r => r[ai] == id);
            if (dc >= 0)
            {
                dmgHeader = dmgHeader.Where((_, i) => i != dc).ToArray();
                for (int i = 0; i < dmg.Count; i++) dmg[i] = dmg[i].Where((_, k) => k != dc).ToArray();
            }
            dirty = true;
        }

        const string MapsDir = "Assets/Resources/Maps";

        static List<string> MapsUsing(string id)
        {
            var list = new List<string>();
            if (!Directory.Exists(MapsDir)) return list;
            foreach (var f in Directory.GetFiles(MapsDir, "*.txt"))
                if (File.ReadAllLines(f).Any(l => l.StartsWith(id + ","))) list.Add(Path.GetFileNameWithoutExtension(f));
            return list;
        }

        // ---------------- damage ----------------

        void DrawDamage()
        {
            EditorGUILayout.HelpBox("行 = 攻撃する部隊、列 = 攻撃される部隊。数値 = 10機で攻撃したときの基本ダメージ(%)。空欄 = 攻撃できない。", MessageType.None);
            const float nameW = 120, w = 44;
            int ai = DmgCol("attacker");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("攻撃 ＼ 防御", EditorStyles.boldLabel, GUILayout.Width(nameW));
                for (int c = 0; c < dmgHeader.Length; c++)
                {
                    if (c == ai) continue;
                    string label = dmgHeader[c];
                    foreach (var r in units) if (r[Col("id")] == dmgHeader[c]) label = r[Col("shortName")];
                    GUILayout.Label(new GUIContent(label, NameOf(dmgHeader[c])), EditorStyles.miniBoldLabel, GUILayout.Width(w));
                }
            }
            foreach (var r in dmg)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(NameOf(r[ai]), GUILayout.Width(nameW));
                    for (int c = 0; c < dmgHeader.Length; c++)
                    {
                        if (c == ai) continue;
                        var nv = EditorGUILayout.TextField(r[c], GUILayout.Width(w)).Trim();
                        if (nv.Length > 0 && !int.TryParse(nv, out _)) nv = r[c];      // numbers or empty only
                        if (nv != r[c]) { r[c] = nv; dirty = true; }
                    }
                }
            }
        }

        static int ToInt(string v) => int.TryParse(v, out var n) ? n : 0;

        // ---------------- save ----------------

        void Save()
        {
            string unitText = FwData.Join(unitHeader, units), dmgText = FwData.Join(dmgHeader, dmg);
            // ids renamed here (old → new) and removed
            var renamed = new Dictionary<string, string>();
            for (int i = 0; i < units.Count; i++)
                if (origIds[i].Length > 0 && origIds[i] != units[i][Col("id")]) renamed[origIds[i]] = units[i][Col("id")];
            string prodText = ProductionWith(renamed);
            try { FwData.LoadGame(new Dictionary<string, string> { { "units", unitText }, { "damage", dmgText }, { "production", prodText } }); }
            catch (Exception e) { EditorUtility.DisplayDialog("保存できません", "値の形が正しくありません:\n" + e.Message, "OK"); return; }
            FwData.Write("units", unitText);
            FwData.Write("damage", dmgText);
            FwData.Write("production", prodText);
            int maps = RewriteMaps(renamed);
            Labels.Register(FwData.LoadGame());
            origIds = units.Select(r => r[Col("id")]).ToList();
            removed.Clear();
            dirty = false;
            message = "保存しました(" + DateTime.Now.ToString("HH:mm:ss") + ")" + (maps > 0 ? "。マップ " + maps + " 個の部隊も書き換えました" : "")
                      + "。次の対戦から反映されます(オンラインはサーバーの更新も必要です)。";
        }

        /// <summary>production.csv with renamed units carried over and removed ones dropped (new units: not made anywhere yet).</summary>
        string ProductionWith(Dictionary<string, string> renamed)
        {
            var rows = FwData.Table(FwData.Read("production"), out var header);
            if (header == null) return FwData.Read("production");
            int ui = Array.IndexOf(header, "unit");
            rows.RemoveAll(r => removed.Contains(r[ui]));
            foreach (var r in rows) if (renamed.TryGetValue(r[ui], out var nid)) r[ui] = nid;
            foreach (var u in units)
            {
                string id = u[Col("id")];
                if (rows.Any(r => r[ui] == id)) continue;
                var nr = new string[header.Length];
                for (int i = 0; i < nr.Length; i++) nr[i] = "";
                nr[ui] = id;
                rows.Add(nr);
            }
            // in the order of units.csv
            var order = units.Select(u => u[Col("id")]).ToList();
            rows = rows.OrderBy(r => order.IndexOf(r[ui]) < 0 ? 999 : order.IndexOf(r[ui])).ToList();
            return FwData.Join(header, rows);
        }

        /// <summary>Units placed on the maps follow renamed ids; removed ones are taken off. Returns the number of files changed.</summary>
        int RewriteMaps(Dictionary<string, string> renamed)
        {
            if ((renamed.Count == 0 && removed.Count == 0) || !Directory.Exists(MapsDir)) return 0;
            int changed = 0;
            foreach (var f in Directory.GetFiles(MapsDir, "*.txt"))
            {
                var lines = File.ReadAllText(f).Replace("\r", "").Split('\n').ToList();
                bool inUnits = false, touched = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    if (l.StartsWith("[")) { inUnits = l.Trim() == "[units]"; continue; }
                    if (!inUnits || l.Trim().Length == 0) continue;
                    int k = l.IndexOf(',');
                    if (k <= 0) continue;
                    string id = l.Substring(0, k).Trim();
                    if (removed.Contains(id)) { lines.RemoveAt(i--); touched = true; }
                    else if (renamed.TryGetValue(id, out var nid)) { lines[i] = nid + l.Substring(k); touched = true; }
                }
                if (!touched) continue;
                File.WriteAllText(f, string.Join("\n", lines), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(f);
                changed++;
            }
            return changed;
        }
    }
}
