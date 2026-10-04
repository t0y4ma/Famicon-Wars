using System.Collections.Generic;
using FamiconWars.Core;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// Placeholder renderer: coloured squares and text, built at runtime.
    /// Reads GameState only; never changes it. Tile (x, y) sits at world (x, -y).
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        public static readonly Color RedArmy = new Color(0.86f, 0.22f, 0.2f);
        public static readonly Color BlueArmy = new Color(0.2f, 0.42f, 0.9f);
        static readonly Color Neutral = new Color(0.55f, 0.55f, 0.55f);

        Sprite square;
        Font font;
        GameState state;
        SpriteRenderer[] ownerMarks;
        SpriteRenderer[] highlights;
        TextMesh[] captureTexts;
        readonly Dictionary<int, UnitView> unitViews = new Dictionary<int, UnitView>();
        Transform unitRoot;
        SpriteRenderer cursor;

        class UnitView
        {
            public GameObject Root;
            public SpriteRenderer Body;
            public TextMesh Label, Count, Flag;
        }

        public void Build(GameState s)
        {
            state = s;
            foreach (Transform c in transform) Destroy(c.gameObject);
            unitViews.Clear();
            var tex = new Texture2D(16, 16) { filterMode = FilterMode.Point };
            var px = new Color[256];
            for (int i = 0; i < 256; i++) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply();
            square = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var tileRoot = new GameObject("Tiles").transform; tileRoot.SetParent(transform, false);
            int n = s.Width * s.Height;
            ownerMarks = new SpriteRenderer[n];
            highlights = new SpriteRenderer[n];
            captureTexts = new TextMesh[n];
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    int i = s.Index(x, y);
                    var t = s.TerrainAt(x, y);
                    ColorUtility.TryParseHtmlString(t.Color, out var col);
                    var tile = MakeSprite("T" + x + "_" + y, tileRoot, new Vector3(x, -y, 0), 0.96f, col, 0);
                    if (t.IsProperty)
                    {
                        ownerMarks[i] = MakeSprite("Own", tile.transform, new Vector3(0, 0.3f, 0), 0.32f, Neutral, 1);
                        ownerMarks[i].transform.localScale = new Vector3(0.9f, 0.3f, 1);
                        MakeText("Name", tile.transform, new Vector3(0, -0.28f, 0), Labels.Get(t.NameKey), 0.05f, new Color(0, 0, 0, 0.75f), 1);
                        captureTexts[i] = MakeText("Cap", tile.transform, new Vector3(0, 0.3f, 0), "", 0.045f, Color.white, 6);
                    }
                    else if (t.Id == "MOUNTAIN" || t.Id == "FOREST")
                        MakeText("Glyph", tile.transform, Vector3.zero, t.Id == "MOUNTAIN" ? "▲" : "♣", 0.12f, new Color(0, 0, 0, 0.35f), 1);
                    highlights[i] = MakeSprite("Hi", tile.transform, Vector3.zero, 1f, Color.clear, 2);
                }
            unitRoot = new GameObject("Units").transform; unitRoot.SetParent(transform, false);
            cursor = MakeSprite("Cursor", transform, Vector3.zero, 1.04f, new Color(1, 1, 0.3f, 0.35f), 7);
            Refresh();
        }

        public void Refresh() => Refresh(-1, 0, 0);

        /// <summary>Redraws owners and units. ghostId/gx/gy shows a unit at a tentative position.</summary>
        public void Refresh(int ghostId, int gx, int gy)
        {
            for (int i = 0; i < ownerMarks.Length; i++)
            {
                if (ownerMarks[i] == null) continue;
                var o = state.Owner[i];
                ownerMarks[i].color = o == Army.Red ? RedArmy : o == Army.Blue ? BlueArmy : Neutral;
                captureTexts[i].text = state.CapturingUnit[i] >= 0 ? state.CaptureProgress[i] + "/" + state.Rules.CaptureGoal : "";
            }
            var alive = new HashSet<int>();
            foreach (var u in state.Units)
            {
                if (u.IsCarried) continue;
                alive.Add(u.Id);
                if (!unitViews.TryGetValue(u.Id, out var v)) { v = MakeUnit(u); unitViews[u.Id] = v; }
                int x = u.Id == ghostId ? gx : u.X, y = u.Id == ghostId ? gy : u.Y;
                v.Root.transform.localPosition = new Vector3(x, -y, 0);
                var baseCol = u.Army == Army.Red ? RedArmy : BlueArmy;
                v.Body.color = u.Acted ? Color.Lerp(baseCol, Color.black, 0.55f) : baseCol;
                v.Count.text = u.Count < 10 ? u.Count.ToString() : "";
                v.Flag.text = u.Cargo.Count > 0 ? "T" : "";
            }
            var dead = new List<int>();
            foreach (var kv in unitViews) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead) { Destroy(unitViews[id].Root); unitViews.Remove(id); }
        }

        public void ClearHighlights() { foreach (var h in highlights) h.color = Color.clear; }

        public void Highlight(IEnumerable<int> tiles, Color c) { foreach (var i in tiles) highlights[i].color = c; }

        public void SetCursor(int x, int y, bool visible)
        {
            cursor.enabled = visible && state.InBounds(x, y);
            cursor.transform.localPosition = new Vector3(x, -y, 0);
        }

        UnitView MakeUnit(UnitState u)
        {
            var d = state.Def(u);
            var v = new UnitView { Root = new GameObject("U" + u.Id) };
            v.Root.transform.SetParent(unitRoot, false);
            v.Body = MakeSprite("Body", v.Root.transform, Vector3.zero, 0.7f, Color.white, 3);
            v.Label = MakeText("Label", v.Root.transform, new Vector3(0, 0.04f, 0), d.ShortName, 0.065f, Color.white, 4);
            v.Count = MakeText("Count", v.Root.transform, new Vector3(0.3f, -0.3f, 0), "", 0.06f, Color.yellow, 5);
            v.Flag = MakeText("Flag", v.Root.transform, new Vector3(-0.3f, 0.3f, 0), "", 0.05f, Color.white, 5);
            return v;
        }

        SpriteRenderer MakeSprite(string name, Transform parent, Vector3 pos, float size, Color c, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size, size, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = square; sr.color = c; sr.sortingOrder = order;
            return sr;
        }

        TextMesh MakeText(string name, Transform parent, Vector3 pos, string text, float size, Color c, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.fontSize = 48; tm.characterSize = size; tm.text = text; tm.color = c;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = order;
            return tm;
        }
    }
}
