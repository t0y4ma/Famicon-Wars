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
    /// 「マップエディタ」: paint terrain, owners and starting units on a grid drawn with the game's own
    /// pictures, with optional symmetry, and save as Resources/Maps/&lt;id&gt;.txt (the map list picks it up).
    /// Left button paints, right button picks up what is under the cursor.
    /// </summary>
    public class FwMapEditorWindow : EditorWindow
    {
        [MenuItem("フィールド・コマンド/マップエディタ")]
        static void Open() => GetWindow<FwMapEditorWindow>("マップエディタ").minSize = new Vector2(980, 620);

        const string MapsDir = "Assets/Resources/Maps";
        enum Tool { Terrain, Owner, Unit, Erase }
        enum Symmetry { None, LeftRight, TopBottom, Point, Rotate4 }
        static readonly string[] ToolNames = { "地形", "所有者", "部隊", "部隊を消す" };
        static readonly string[] SymNames = { "なし", "左右", "上下", "点対称", "4方向(90°)" };
        static readonly string[] OwnerNames = { "中立", "レッド", "ブルー", "グリーン", "イエロー" };

        sealed class MapUnit { public string Type; public Army Army; public int X, Y, Hp = 100; }

        GameData data;
        string mapId = "map09", mapName = "新しいマップ", mapNote = "";
        int width = 20, height = 14, newW = 20, newH = 14;
        /// <summary>Armies of this map (2 … Armies.Max): limits the owner / unit palettes and the checks.</summary>
        int armyCount = 2;
        char[,] tiles, owners;
        readonly List<MapUnit> units = new List<MapUnit>();
        string loadedFrom;

        Tool tool;
        Symmetry symmetry;
        bool swapArmies = true;
        int terrainPick, ownerPick, unitPick, unitArmyPick;
        float cell = 32;
        Vector2 scroll, sideScroll;
        bool dirty;
        string message;
        GameState preview;
        bool previewDirty = true;
        int hoverX = -1, hoverY = -1;

        void OnEnable()
        {
            try { data = FwData.LoadGame(); }
            catch (Exception e) { message = "データを読み込めません: " + e.Message; return; }
            if (tiles == null) NewMap(20, 14);
        }

        // ---------------- model ----------------

        char PlainChar => data.Terrain("PLAIN").Char;

        void NewMap(int w, int h)
        {
            width = newW = w; height = newH = h;
            tiles = new char[w, h]; owners = new char[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { tiles[x, y] = PlainChar; owners[x, y] = '.'; }
            units.Clear();
            armyCount = 2;
            mapId = NextFreeId(); mapName = "新しいマップ"; mapNote = "";
            loadedFrom = null; dirty = false; previewDirty = true;
        }

        static string NextFreeId()
        {
            for (int n = 1; n < 100; n++) { var id = "map" + n.ToString("00"); if (!File.Exists(MapsDir + "/" + id + ".txt")) return id; }
            return "map99";
        }

        void LoadMap(string id)
        {
            var text = File.ReadAllText(MapsDir + "/" + id + ".txt");
            var tileRows = new List<string>(); var ownerRows = new List<string>();
            units.Clear();
            mapName = id; mapNote = ""; int w = 0, h = 0;
            string section = "";
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimStart('﻿').TrimEnd();
                if (line.Length == 0) continue;
                if (line.StartsWith("[")) { section = line; continue; }
                switch (section)
                {
                    case "":
                        int k = line.IndexOf('=');
                        if (k < 0) break;
                        string key = line.Substring(0, k), v = line.Substring(k + 1);
                        if (key == "name") mapName = v; else if (key == "note") mapNote = v;
                        else if (key == "width") int.TryParse(v, out w); else if (key == "height") int.TryParse(v, out h);
                        break;
                    case "[tiles]": tileRows.Add(line); break;
                    case "[owners]": ownerRows.Add(line); break;
                    case "[units]":
                        var p = line.Split(',');
                        if (p.Length >= 4 && Enum.TryParse<Army>(p[1].Trim(), out var a))
                            units.Add(new MapUnit { Type = p[0].Trim(), Army = a, X = int.Parse(p[2]), Y = int.Parse(p[3]), Hp = p.Length > 4 ? int.Parse(p[4]) : 100 });
                        break;
                }
            }
            if (w == 0) w = tileRows.Count > 0 ? tileRows[0].Length : 1;
            if (h == 0) h = tileRows.Count;
            width = newW = w; height = newH = h;
            tiles = new char[w, h]; owners = new char[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    tiles[x, y] = y < tileRows.Count && x < tileRows[y].Length ? tileRows[y][x] : PlainChar;
                    owners[x, y] = y < ownerRows.Count && x < ownerRows[y].Length ? ownerRows[y][x] : '.';
                }
            armyCount = Mathf.Clamp(UsedArmies(), 2, Armies.Max);
            // the built-in maps keep their description in the map list: start from it
            var entry = MapCatalog.Find(id);
            if (string.IsNullOrEmpty(mapNote) && entry != null) mapNote = entry.Note;
            mapId = id; loadedFrom = id; dirty = false; previewDirty = true;
            message = id + " を開きました";
        }

        /// <summary>Highest army that owns a tile or a unit, plus one.</summary>
        int UsedArmies()
        {
            int top = -1;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) top = Math.Max(top, (int)Armies.FromLetter(owners[x, y]));
            foreach (var u in units) top = Math.Max(top, (int)u.Army);
            return top + 1;
        }

        string MapText()
        {
            var sb = new StringBuilder();
            sb.Append("name=").Append(FwData.Clean(mapName).Replace("=", "＝")).Append('\n');
            if (!string.IsNullOrWhiteSpace(mapNote)) sb.Append("note=").Append(FwData.Clean(mapNote)).Append('\n');
            sb.Append("width=").Append(width).Append('\n').Append("height=").Append(height).Append('\n');
            sb.Append("[tiles]\n");
            for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) sb.Append(tiles[x, y]); sb.Append('\n'); }
            sb.Append("[owners]\n");
            for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) sb.Append(owners[x, y]); sb.Append('\n'); }
            sb.Append("[units]\n");
            foreach (var u in units.OrderBy(u => (int)u.Army).ThenBy(u => u.Y).ThenBy(u => u.X))
                sb.Append(u.Type).Append(',').Append(u.Army).Append(',').Append(u.X).Append(',').Append(u.Y).Append(',').Append(u.Hp).Append('\n');
            return sb.ToString();
        }

        void Resize(int w, int h)
        {
            w = Mathf.Clamp(w, 6, 60); h = Mathf.Clamp(h, 6, 60);
            var nt = new char[w, h]; var no = new char[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool inside = x < width && y < height;
                    nt[x, y] = inside ? tiles[x, y] : PlainChar;
                    no[x, y] = inside ? owners[x, y] : '.';
                }
            tiles = nt; owners = no; width = w; height = h;
            units.RemoveAll(u => u.X >= w || u.Y >= h);
            Changed();
        }

        void Changed() { dirty = true; previewDirty = true; Repaint(); }

        // ---------------- painting ----------------

        /// <summary>The cell and its mirror images, each with the army it maps the painted army to.</summary>
        IEnumerable<(int x, int y, int turn)> Mirrors(int x, int y)
        {
            yield return (x, y, 0);
            int mx = width - 1 - x, my = height - 1 - y;
            switch (symmetry)
            {
                case Symmetry.LeftRight: yield return (mx, y, 1); break;
                case Symmetry.TopBottom: yield return (x, my, 1); break;
                case Symmetry.Point: yield return (mx, my, 1); break;
                case Symmetry.Rotate4:
                    if (width != height) break;
                    int n = width - 1;
                    yield return (n - y, x, 1);       // 90°
                    yield return (n - x, n - y, 2);   // 180°
                    yield return (y, n - x, 3);       // 270°
                    break;
            }
        }

        Army Turned(Army a, int turn)
        {
            if (a == Army.None || turn == 0 || !swapArmies) return a;
            int n = symmetry == Symmetry.Rotate4 ? ((int)a + turn) % armyCount
                // two-way symmetry: red ↔ blue, green ↔ yellow (an army without a partner stays)
                : (int)a ^ 1;
            return n < armyCount ? (Army)n : a;
        }

        void Paint(int x, int y)
        {
            foreach (var (px, py, turn) in Mirrors(x, y).Distinct().ToList())
            {
                if (px < 0 || py < 0 || px >= width || py >= height) continue;
                switch (tool)
                {
                    case Tool.Terrain:
                    {
                        var t = data.Terrains[terrainPick];
                        tiles[px, py] = t.Char;
                        if (!t.IsProperty) owners[px, py] = '.';
                        else if (turn > 0 && owners[x, y] != '.') owners[px, py] = Armies.Letter(Turned(Armies.FromLetter(owners[x, y]), turn));
                        break;
                    }
                    case Tool.Owner:
                    {
                        if (!data.TerrainByChar(tiles[px, py]).IsProperty) break;
                        var a = ownerPick == 0 ? Army.None : (Army)(ownerPick - 1);
                        owners[px, py] = Armies.Letter(Turned(a, turn));
                        break;
                    }
                    case Tool.Unit:
                    {
                        units.RemoveAll(u => u.X == px && u.Y == py);
                        units.Add(new MapUnit { Type = data.Units[unitPick].Id, Army = Turned((Army)unitArmyPick, turn), X = px, Y = py });
                        break;
                    }
                    case Tool.Erase:
                        units.RemoveAll(u => u.X == px && u.Y == py);
                        break;
                }
            }
            Changed();
        }

        /// <summary>Right button: take the terrain / owner / unit under the cursor into the palette.</summary>
        void Pick(int x, int y)
        {
            var u = units.FirstOrDefault(m => m.X == x && m.Y == y);
            if (tool == Tool.Unit && u != null) { unitPick = data.Unit(u.Type).Index; unitArmyPick = (int)u.Army; }
            else if (tool == Tool.Owner) { var a = Armies.FromLetter(owners[x, y]); ownerPick = a == Army.None ? 0 : (int)a + 1; }
            else { terrainPick = data.TerrainByChar(tiles[x, y]).Index; if (tool == Tool.Erase) tool = Tool.Terrain; }
            Repaint();
        }

        // ---------------- GUI ----------------

        void OnGUI()
        {
            if (data == null) { EditorGUILayout.HelpBox(message ?? "データがありません", MessageType.Error); return; }
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (var side = new EditorGUILayout.ScrollViewScope(sideScroll, GUILayout.Width(300)))
                {
                    sideScroll = side.scrollPosition;
                    DrawSide();
                }
                DrawGrid();
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("新規", EditorStyles.toolbarButton, GUILayout.Width(50)) && ConfirmDiscard()) NewMap(newW, newH);
                if (EditorGUILayout.DropdownButton(new GUIContent("開く"), FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(60)))
                {
                    var menu = new GenericMenu();
                    foreach (var f in Directory.GetFiles(MapsDir, "*.txt").Select(Path.GetFileNameWithoutExtension).OrderBy(s => s))
                    {
                        string id = f;
                        var entry = new MapEntry { Id = id, Name = id };
                        MapCatalog.ReadHeader(entry, File.ReadAllText(MapsDir + "/" + id + ".txt"));
                        if (entry.Name == id && MapCatalog.Find(id) != null) entry.Name = MapCatalog.Find(id).Name;
                        menu.AddItem(new GUIContent(entry.Name + " (" + id + ")"), id == loadedFrom, () => { if (ConfirmDiscard()) LoadMap(id); });
                    }
                    menu.ShowAsContext();
                }
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(50))) Save();
                GUILayout.Space(16);
                GUILayout.Label("拡大", GUILayout.Width(30));
                cell = GUILayout.HorizontalSlider(cell, 16, 56, GUILayout.Width(120));
                GUILayout.FlexibleSpace();
                if (dirty) GUILayout.Label("未保存", EditorStyles.boldLabel);
                if (hoverX >= 0) GUILayout.Label(HoverText(), GUILayout.Width(360));
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        bool ConfirmDiscard() => !dirty || EditorUtility.DisplayDialog("マップエディタ", "保存していない変更は失われます。", "続ける", "やめる");

        string HoverText()
        {
            if (hoverX < 0 || hoverX >= width || hoverY >= height) return "";
            var t = data.TerrainByChar(tiles[hoverX, hoverY]);
            var u = units.FirstOrDefault(m => m.X == hoverX && m.Y == hoverY);
            return "(" + hoverX + "," + hoverY + ") " + Labels.Get(t.NameKey)
                   + (t.IsProperty ? " " + Armies.Name(Armies.FromLetter(owners[hoverX, hoverY])) : "")
                   + (u != null ? " / " + Armies.Name(u.Army) + " " + Labels.Get(data.Unit(u.Type).NameKey) : "");
        }

        void DrawSide()
        {
            GUILayout.Label("マップ", EditorStyles.boldLabel);
            var id = EditorGUILayout.TextField("ファイル名(ID)", mapId);
            if (id != mapId) { mapId = new string(id.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray()); dirty = true; }
            var nm = EditorGUILayout.TextField("名前", mapName);
            if (nm != mapName) { mapName = nm; dirty = true; }
            GUILayout.Label("説明(マップ選択に出る)");
            var nt = EditorGUILayout.TextArea(mapNote, GUILayout.Height(48));
            if (nt != mapNote) { mapNote = nt; dirty = true; }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("大きさ " + width + "×" + height, GUILayout.Width(110));
                newW = EditorGUILayout.IntField(newW, GUILayout.Width(40));
                GUILayout.Label("×", GUILayout.Width(12));
                newH = EditorGUILayout.IntField(newH, GUILayout.Width(40));
                if (GUILayout.Button("変更", GUILayout.Width(50))) Resize(newW, newH);
            }

            var counts = Enumerable.Range(2, Armies.Max - 1).Select(n => n + "軍").ToArray();
            int ac = EditorGUILayout.Popup("軍の数", armyCount - 2, counts) + 2;
            if (ac != armyCount)
            {
                armyCount = ac;
                ownerPick = Mathf.Min(ownerPick, armyCount);
                unitArmyPick = Mathf.Min(unitArmyPick, armyCount - 1);
            }

            GUILayout.Space(8);
            GUILayout.Label("道具", EditorStyles.boldLabel);
            tool = (Tool)GUILayout.SelectionGrid((int)tool, ToolNames, 2);
            GUILayout.Label("左ボタン: 塗る / 右ボタン: 下にあるものを拾う", EditorStyles.miniLabel);
            symmetry = (Symmetry)EditorGUILayout.Popup("対称に塗る", (int)symmetry, SymNames);
            if (symmetry == Symmetry.Rotate4 && width != height) EditorGUILayout.HelpBox("4方向は正方形のマップだけです", MessageType.Warning);
            if (symmetry != Symmetry.None) swapArmies = EditorGUILayout.ToggleLeft("反対側は相手の軍にする", swapArmies);

            GUILayout.Space(6);
            switch (tool)
            {
                case Tool.Terrain:
                    for (int i = 0; i < data.Terrains.Count; i++)
                    {
                        var t = data.Terrains[i];
                        bool on = i == terrainPick;
                        if (GUILayout.Toggle(on, Labels.Get(t.NameKey), "Button") && !on) terrainPick = i;
                    }
                    break;
                case Tool.Owner:
                    ownerPick = GUILayout.SelectionGrid(Mathf.Min(ownerPick, armyCount), OwnerNames.Take(armyCount + 1).ToArray(), 1);
                    EditorGUILayout.HelpBox("施設(基地・都市・工場・空港・港)にだけ塗れます", MessageType.None);
                    break;
                case Tool.Unit:
                    unitArmyPick = GUILayout.Toolbar(Mathf.Min(unitArmyPick, armyCount - 1), OwnerNames.Skip(1).Take(armyCount).ToArray());
                    for (int i = 0; i < data.Units.Count; i++)
                    {
                        bool on = i == unitPick;
                        if (GUILayout.Toggle(on, Labels.Get(data.Units[i].NameKey), "Button") && !on) unitPick = i;
                    }
                    break;
            }

            GUILayout.Space(8);
            GUILayout.Label("チェック", EditorStyles.boldLabel);
            foreach (var line in Problems()) EditorGUILayout.HelpBox(line.text, line.error ? MessageType.Warning : MessageType.None);
        }

        List<(string text, bool error)> Problems()
        {
            var list = new List<(string, bool)>();
            var hq = new int[Armies.Max];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var a = Armies.FromLetter(owners[x, y]);
                    var t = data.TerrainByChar(tiles[x, y]);
                    if (t.Id == "HQ") { if (a == Army.None) list.Add(("(" + x + "," + y + ") の基地に持ち主がいません", true)); else hq[(int)a]++; }
                }
            int used = UsedArmies();
            list.Insert(0, (armyCount + "軍のマップ", false));
            if (used > armyCount) list.Add((Armies.Name((Army)(used - 1)) + "の施設か部隊がありますが、軍の数は" + armyCount + "です", true));
            for (int a = 0; a < armyCount; a++)
            {
                if (hq[a] == 0) list.Add((Armies.Name((Army)a) + "の基地がありません", true));
                else if (hq[a] > 1) list.Add((Armies.Name((Army)a) + "の基地が" + hq[a] + "つあります。1つにしてください", true));
                int n = units.Count(u => (int)u.Army == a);
                list.Add((Armies.Name((Army)a) + ": 部隊 " + n, false));
            }
            foreach (var u in units)
            {
                var t = data.TerrainByChar(tiles[u.X, u.Y]);
                if (t.Cost[(int)data.Unit(u.Type).MoveClass] < 0) list.Add(("(" + u.X + "," + u.Y + ") の" + Labels.Get(data.Unit(u.Type).NameKey) + "はその地形にいられません", true));
            }
            return list;
        }

        void DrawGrid()
        {
            var area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var content = new Rect(0, 0, width * cell + 8, height * cell + 8);
            scroll = GUI.BeginScrollView(area, scroll, content);
            if (previewDirty) BuildPreview();
            var e = Event.current;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    DrawCell(new Rect(4 + x * cell, 4 + y * cell, cell, cell), x, y);
            foreach (var u in units)
            {
                var r = new Rect(4 + u.X * cell, 4 + u.Y * cell, cell, cell);
                var sp = data.Units.Exists(d => d.Id == u.Type) ? UnitIcons.Get(data.Unit(u.Type).Icon, u.Army) : null;
                if (sp != null) DrawSprite(r, sp, ArmyStyle.FacesLeft(u.Army));
                else { EditorGUI.DrawRect(r.Shrink(cell * 0.2f), ArmyStyle.Board(u.Army)); GUI.Label(r, data.Unit(u.Type).ShortName, EditorStyles.centeredGreyMiniLabel); }
            }
            // grid lines and the cell under the cursor
            var line = new Color(0, 0, 0, 0.18f);
            for (int x = 0; x <= width; x++) EditorGUI.DrawRect(new Rect(4 + x * cell, 4, 1, height * cell), line);
            for (int y = 0; y <= height; y++) EditorGUI.DrawRect(new Rect(4, 4 + y * cell, width * cell, 1), line);

            int cx = Mathf.FloorToInt((e.mousePosition.x - 4) / cell), cy = Mathf.FloorToInt((e.mousePosition.y - 4) / cell);
            bool inside = cx >= 0 && cy >= 0 && cx < width && cy < height;
            if (inside)
            {
                foreach (var (px, py, _) in Mirrors(cx, cy))
                    if (px >= 0 && py >= 0 && px < width && py < height)
                        EditorGUI.DrawRect(new Rect(4 + px * cell, 4 + py * cell, cell, cell), new Color(1, 1, 0.3f, px == cx && py == cy ? 0.35f : 0.18f));
            }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                int hx = inside ? cx : -1, hy = inside ? cy : -1;
                if (hx != hoverX || hy != hoverY) { hoverX = hx; hoverY = hy; Repaint(); }
            }
            if (inside && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                if (e.button == 0) { Paint(cx, cy); e.Use(); }
                else if (e.button == 1 && e.type == EventType.MouseDown) { Pick(cx, cy); e.Use(); }
            }
            GUI.EndScrollView();
            wantsMouseMove = true;
        }

        void BuildPreview()
        {
            previewDirty = false;
            try
            {
                var map = MapDef.Parse(MapText(), data);
                preview = GameState.Create(data, map, 1);
            }
            catch (Exception) { preview = null; }
        }

        void DrawCell(Rect r, int x, int y)
        {
            var t = data.TerrainByChar(tiles[x, y]);
            ColorUtility.TryParseHtmlString(t.Color, out var col);
            EditorGUI.DrawRect(r, col);
            if (preview == null || !preview.InBounds(x, y)) return;
            if (t.IsProperty)
            {
                var sp = TileIcons.Get(t.Id, Armies.FromLetter(owners[x, y]));
                if (sp != null) DrawSprite(r, sp, false);
                else GUI.Label(r, Labels.Get(t.NameKey), EditorStyles.centeredGreyMiniLabel);
                return;
            }
            var ground = TerrainIcons.Get(preview, x, y);
            if (ground == null) return;
            DrawSprite(r, ground, false);
            foreach (var b in TerrainIcons.Borders(preview, x, y)) DrawSprite(r, b, false);
            var deck = TerrainIcons.Deck(preview, x, y);
            if (deck != null) DrawSprite(r, deck, false);
        }

        static void DrawSprite(Rect r, Sprite sp, bool flip)
        {
            var tex = sp.texture;
            if (tex == null) return;
            var tr = sp.textureRect;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            if (flip) { uv.x += uv.width; uv.width = -uv.width; }
            GUI.DrawTextureWithTexCoords(r, tex, uv, true);
        }

        void Save()
        {
            if (string.IsNullOrEmpty(mapId)) { EditorUtility.DisplayDialog("保存できません", "ファイル名(ID)を入れてください", "OK"); return; }
            var path = MapsDir + "/" + mapId + ".txt";
            if (File.Exists(path) && mapId != loadedFrom && !EditorUtility.DisplayDialog("上書き", mapId + " はすでにあります。上書きしますか?", "上書き", "やめる")) return;
            var text = MapText();
            try { GameState.Create(data, MapDef.Parse(text, data), 1); }
            catch (Exception e) { EditorUtility.DisplayDialog("保存できません", "マップを読めませんでした:\n" + e.Message, "OK"); return; }
            File.WriteAllText(path, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            MapCatalog.Reload();
            loadedFrom = mapId; dirty = false;
            message = path + " に保存しました。マップ選択の一覧に出ます(オンラインで使うにはサーバーの更新も必要です)。";
        }
    }

    static class RectExt
    {
        public static Rect Shrink(this Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2 * d, r.height - 2 * d);
    }
}
