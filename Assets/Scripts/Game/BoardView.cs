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
        SpriteRenderer[] ownerMarks;      // a property's owner bar, or its whole tile when it has a picture
        bool[] pictured;                  // the property is drawn from Resources/Tiles (roof / flag in the owner's colour)
        SpriteRenderer[] highlights;
        SpriteRenderer[] rangeMarks;       // outlined squares: where a gun could fire from the tile under the cursor
        SpriteRenderer[] produceMarks;     // a badge on facilities that can build units for their owner
        Sprite ringSprite, produceSprite;
        TextMesh[] captureTexts;
        SpriteRenderer[] captureBacks;
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
            rangeMarks = new SpriteRenderer[n];
            produceMarks = new SpriteRenderer[n];
            ringSprite = RingSprite();
            produceSprite = ProduceSprite();
            captureTexts = new TextMesh[n];
            captureBacks = new SpriteRenderer[n];
            pictured = new bool[n];
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    int i = s.Index(x, y);
                    var t = s.TerrainAt(x, y);
                    ColorUtility.TryParseHtmlString(t.Color, out var col);
                    var tile = MakeSprite("T" + x + "_" + y, tileRoot, new Vector3(x, -y, 0), 0.96f, col, 0);
                    // picture tiles fill the whole square (the grid line is drawn into the picture)
                    var ground = t.IsProperty ? null : TerrainIcons.Get(s, x, y);
                    if (ground != null) { tile.sprite = ground; tile.color = Color.white; tile.transform.localScale = Vector3.one; }
                    if (ground != null)
                        foreach (var border in TerrainIcons.Borders(s, x, y))
                            MakeSprite("Border", tile.transform, Vector3.zero, 1f, Color.white, 1, border);
                    // a bridge over a river: the deck goes over the river and its borders
                    var deck = ground != null ? TerrainIcons.Deck(s, x, y) : null;
                    if (deck != null) MakeSprite("Deck", tile.transform, new Vector3(0, 0, -0.001f), 1f, Color.white, 1, deck);
                    if (t.IsProperty && TileIcons.Get(t.Id, s.Owner[i]) != null)
                    {
                        // picture tile: the building itself shows the owner (swapped in Refresh)
                        tile.sprite = TileIcons.Get(t.Id, s.Owner[i]);
                        tile.color = Color.white;
                        tile.transform.localScale = Vector3.one;
                        ownerMarks[i] = tile;
                        pictured[i] = true;
                        captureBacks[i] = MakeSprite("CapBack", tile.transform, new Vector3(0, 0.36f, 0), 0.3f, BadgeBack, 7, badge);
                        captureBacks[i].transform.localScale = new Vector3(0.62f, 0.26f, 1);
                        captureTexts[i] = MakeText("Cap", tile.transform, new Vector3(0, 0.36f, 0), "", 0.045f, Color.white, 8);
                    }
                    else if (t.IsProperty)
                    {
                        ownerMarks[i] = MakeSprite("Own", tile.transform, new Vector3(0, 0.3f, 0), 0.32f, Neutral, 1);
                        ownerMarks[i].transform.localScale = new Vector3(0.9f, 0.3f, 1);
                        MakeText("Name", tile.transform, new Vector3(0, -0.28f, 0), Labels.Get(t.NameKey), 0.05f, new Color(0, 0, 0, 0.75f), 1);
                        captureTexts[i] = MakeText("Cap", tile.transform, new Vector3(0, 0.36f, 0), "", 0.045f, Color.white, 8);
                    }
                    else if (ground == null && (t.Id == "MOUNTAIN" || t.Id == "FOREST"))
                        MakeText("Glyph", tile.transform, Vector3.zero, t.Id == "MOUNTAIN" ? "▲" : "♣", 0.12f, new Color(0, 0, 0, 0.35f), 1);
                    highlights[i] = MakeSprite("Hi", tile.transform, Vector3.zero, 1f, Color.clear, 2);
                    rangeMarks[i] = MakeSprite("Range", tile.transform, new Vector3(0, 0, -0.01f), 1f, new Color(1f, 0.55f, 0.15f, 0.95f), 2, ringSprite);
                    rangeMarks[i].enabled = false;
                    if (t.IsProperty && t.Produces != null)
                    {
                        produceMarks[i] = MakeSprite("Produce", tile.transform, new Vector3(-0.32f, -0.32f, 0), 0.3f, Color.white, 3, produceSprite);
                        produceMarks[i].enabled = false;
                    }
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
                if (pictured[i]) ownerMarks[i].sprite = TileIcons.Get(state.Data.Terrains[state.Terrain[i]].Id, o);
                else ownerMarks[i].color = o == Army.Red ? RedArmy : o == Army.Blue ? BlueArmy : Neutral;
                captureTexts[i].text = state.CapturingUnit[i] >= 0 ? state.CaptureProgress[i] + "/" + state.Rules.CaptureGoal : "";
                if (captureBacks[i] != null) captureBacks[i].enabled = captureTexts[i].text.Length > 0;
                if (produceMarks[i] != null)
                    produceMarks[i].enabled = o != Army.None && RulesEngine.InProductionRange(state, o, i % state.Width, i / state.Width);
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

        /// <summary>Outlines these tiles (null clears).</summary>
        public void ShowRange(IEnumerable<int> tiles)
        {
            if (rangeMarks == null) return;
            foreach (var m in rangeMarks) if (m != null) m.enabled = false;
            if (tiles == null) return;
            foreach (var i in tiles) if (i >= 0 && i < rangeMarks.Length) rangeMarks[i].enabled = true;
        }

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

        /// <summary>A hollow square (the outline of a tile).</summary>
        static Sprite RingSprite()
        {
            const int n = 32;
            var tex = new Texture2D(n, n) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int e = Mathf.Min(Mathf.Min(x, y), Mathf.Min(n - 1 - x, n - 1 - y));
                    // a bright outline with a dark rim outside it, and a faint fill
                    tex.SetPixel(x, y, e <= 1 ? new Color(1, 1, 1, 1) : e == 2 ? new Color(0.1f, 0.05f, 0, 0.8f) : new Color(1, 1, 1, 0.16f));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        /// <summary>The "can build here" badge: a hammer on a brass disc.</summary>
        static Sprite ProduceSprite()
        {
            const int n = 16;
            var tex = new Texture2D(n, n) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var brass = new Color(0.95f, 0.72f, 0.2f); var dark = new Color(0.16f, 0.11f, 0.07f); var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - 7.5f, dy = y - 7.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, r <= 6.6f ? brass : r <= 7.9f ? dark : clear);
                }
            // hammer (texture rows count from the bottom): head across the top, handle down the middle
            for (int x = 4; x <= 11; x++) for (int y = 9; y <= 11; y++) tex.SetPixel(x, y, dark);
            for (int y = 3; y <= 8; y++) for (int x = 7; x <= 8; x++) tex.SetPixel(x, y, dark);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
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
