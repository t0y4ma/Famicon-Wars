using System.Collections.Generic;
using FamiconWars.Core;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// Board renderer built at runtime: coloured terrain squares, pixel-art unit icons with an HP
    /// badge, cargo and low-supply markers. Reads GameState only; never changes it.
    /// Tile (x, y) sits at world (x, -y).
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        public static readonly Color RedArmy = new Color(0.86f, 0.22f, 0.2f);
        public static readonly Color BlueArmy = new Color(0.2f, 0.42f, 0.9f);
        static readonly Color Neutral = new Color(0.55f, 0.55f, 0.55f);
        static readonly Color ActedTint = new Color(0.5f, 0.5f, 0.52f, 1f);
        static readonly Color BadgeBack = new Color(0.08f, 0.07f, 0.06f, 0.88f);
        static readonly Color Warn = new Color(1f, 0.62f, 0.12f);

        Sprite square, shadow, badge;
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
            public SpriteRenderer Icon, Shadow, HpBack, CargoBack, CargoIcon, WarnBack;
            public TextMesh Hp, WarnMark, Fallback;
            public float Phase, BaseY;
            public bool Idle;
        }

        public void Build(GameState s)
        {
            state = s;
            foreach (Transform c in transform) Destroy(c.gameObject);
            unitViews.Clear();
            square = SolidSprite();
            shadow = EllipseSprite();
            badge = RoundedSprite();
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
                        captureTexts[i] = MakeText("Cap", tile.transform, new Vector3(0, 0.36f, 0), "", 0.045f, Color.white, 8);
                    }
                    else if (t.Id == "MOUNTAIN" || t.Id == "FOREST")
                        MakeText("Glyph", tile.transform, Vector3.zero, t.Id == "MOUNTAIN" ? "▲" : "♣", 0.12f, new Color(0, 0, 0, 0.35f), 1);
                    highlights[i] = MakeSprite("Hi", tile.transform, Vector3.zero, 1f, Color.clear, 2);
                }
            unitRoot = new GameObject("Units").transform; unitRoot.SetParent(transform, false);
            cursor = MakeSprite("Cursor", transform, Vector3.zero, 1.04f, new Color(1, 1, 0.3f, 0.35f), 9);
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
                var d = state.Def(u);
                if (!unitViews.TryGetValue(u.Id, out var v)) { v = MakeUnit(u); unitViews[u.Id] = v; }
                int x = u.Id == ghostId ? gx : u.X, y = u.Id == ghostId ? gy : u.Y;
                v.Root.transform.localPosition = new Vector3(x, -y, 0);
                v.Icon.color = u.Acted ? ActedTint : Color.white;
                if (v.Fallback != null) v.Fallback.color = u.Acted ? ActedTint : Color.white;
                v.Idle = !u.Acted && u.Army == state.Active && u.Id != ghostId && !state.GameOver;
                if (!v.Idle) v.Icon.transform.localPosition = IconRest(d);

                bool showHp = u.Count < 10;
                v.HpBack.enabled = showHp;
                v.Hp.text = showHp ? u.Count.ToString() : "";
                v.Hp.color = u.Count <= 3 ? new Color(1f, 0.45f, 0.4f) : Color.white;

                UnitState cargo = u.Cargo.Count > 0 ? state.UnitById(u.Cargo[0]) : null;
                v.CargoBack.enabled = cargo != null;
                v.CargoIcon.enabled = cargo != null;
                if (cargo != null) v.CargoIcon.sprite = UnitIcons.Get(state.Def(cargo).Id, cargo.Army);

                bool lowFuel = d.Fuel > 0 && u.Fuel <= Mathf.Max(1, d.Fuel / 4) && (d.FuelPerPhase > 0 || d.Domain == Domain.Sea || u.Fuel <= d.Move);
                bool noAmmo = d.Ammo > 0 && u.Ammo == 0;
                v.WarnBack.enabled = lowFuel || noAmmo;
                v.WarnMark.text = lowFuel || noAmmo ? "!" : "";
            }
            var dead = new List<int>();
            foreach (var kv in unitViews) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead) { Destroy(unitViews[id].Root); unitViews.Remove(id); }
        }

        void Update()
        {
            if (state == null) return;
            float t = Time.time;
            foreach (var kv in unitViews)
            {
                var v = kv.Value;
                if (!v.Idle) continue;
                var rest = v.Icon.transform.localPosition;
                rest.y = v.BaseY + Mathf.Abs(Mathf.Sin(t * 3.2f + v.Phase)) * 0.045f;
                v.Icon.transform.localPosition = rest;
            }
        }

        static Vector3 IconRest(UnitDef d) => new Vector3(0, d.Domain == Domain.Air ? 0.12f : 0.04f, 0);

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
            var v = new UnitView { Root = new GameObject("U" + u.Id), Phase = (u.Id * 0.61f) % 6.28f };
            v.Root.transform.SetParent(unitRoot, false);
            bool air = d.Domain == Domain.Air;
            // aircraft hover: their shadow sits lower and smaller
            v.Shadow = MakeSprite("Shadow", v.Root.transform, new Vector3(0, air ? -0.36f : -0.3f, 0), 1f, new Color(0, 0, 0, air ? 0.22f : 0.32f), 3, shadow);
            v.Shadow.transform.localScale = new Vector3(air ? 0.55f : 0.78f, air ? 0.16f : 0.22f, 1);

            v.BaseY = IconRest(d).y;
            var sp = UnitIcons.Get(d.Id, u.Army);
            v.Icon = MakeSprite("Icon", v.Root.transform, IconRest(d), 1f, Color.white, 4, sp ?? square);
            v.Icon.flipX = u.Army == Army.Blue;     // armies face each other
            if (sp == null)
            {
                // no icon for this type: army-coloured plate with the short name
                v.Icon.transform.localScale = new Vector3(0.7f, 0.7f, 1);
                v.Icon.color = u.Army == Army.Red ? RedArmy : BlueArmy;
                v.Fallback = MakeText("Label", v.Root.transform, new Vector3(0, 0.04f, 0), d.ShortName, 0.065f, Color.white, 5);
            }

            v.HpBack = MakeSprite("HpBack", v.Root.transform, new Vector3(0.3f, -0.3f, 0), 0.36f, BadgeBack, 6, badge);
            v.Hp = MakeText("Hp", v.Root.transform, new Vector3(0.3f, -0.29f, 0), "", 0.06f, Color.white, 7);
            v.Hp.fontStyle = FontStyle.Bold;

            v.CargoBack = MakeSprite("CargoBack", v.Root.transform, new Vector3(-0.3f, 0.3f, 0), 0.4f, new Color(1f, 0.93f, 0.7f, 0.95f), 6, badge);
            v.CargoIcon = MakeSprite("Cargo", v.Root.transform, new Vector3(-0.3f, 0.3f, 0), 0.36f, Color.white, 7, square);

            v.WarnBack = MakeSprite("WarnBack", v.Root.transform, new Vector3(0.32f, 0.32f, 0), 0.28f, Warn, 6, badge);
            v.WarnMark = MakeText("Warn", v.Root.transform, new Vector3(0.32f, 0.33f, 0), "", 0.055f, new Color(0.15f, 0.08f, 0f), 7);
            v.WarnMark.fontStyle = FontStyle.Bold;
            return v;
        }

        SpriteRenderer MakeSprite(string name, Transform parent, Vector3 pos, float size, Color c, int order, Sprite sprite = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size, size, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite ?? square; sr.color = c; sr.sortingOrder = order;
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

        // ---------- procedural sprites (1 world unit across) ----------

        static Sprite SolidSprite()
        {
            var tex = new Texture2D(16, 16) { filterMode = FilterMode.Point };
            var px = new Color[256];
            for (int i = 0; i < 256; i++) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
        }

        static Sprite EllipseSprite()
        {
            const int n = 32;
            var tex = new Texture2D(n, n) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1 - r) * 2.2f)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        static Sprite RoundedSprite()
        {
            const int n = 16, rad = 4;
            var tex = new Texture2D(n, n) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int cx = x < rad ? rad - 1 : x >= n - rad ? n - rad : x;
                    int cy = y < rad ? rad - 1 : y >= n - rad ? n - rad : y;
                    float dx = x - cx, dy = y - cy;
                    tex.SetPixel(x, y, new Color(1, 1, 1, dx * dx + dy * dy <= rad * rad ? 1 : 0));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }
    }
}
