using System.Collections.Generic;
using FamiconWars.Core;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// Pixel-art ground tiles (32x32) from Resources/Tiles/terrain.png.
    /// Row 0 (top): plain a/b, forest, mountain, sea a/b, lake.
    /// Row 1: road, row 2: river, one column per set of connected sides (1 up, 2 right, 4 down, 8 left).
    /// Row 3: bridges (see BridgeCell). Row 4: beach, one column per set of sides that face water.
    /// Rows 5-11: borders between two kinds of ground (see Borders).
    /// A river is drawn like a lake that runs on: water in the middle with a grass bank on every side
    /// that faces land, open to the sides that join water (river, sea, a bridge, the map edge); where
    /// the water also lies diagonally (a wide river) the corner is filled in too, so a river two squares
    /// wide is one stretch of water. A bridge over a river is that river picture with only the deck on top.
    /// </summary>
    public static class TerrainIcons
    {
        const int Cell = 32;
        static Texture2D atlas;
        static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();
        static readonly int[] DX = { 0, 1, 0, -1 }, DY = { -1, 0, 1, 0 };

        static Sprite At(int col, int rowFromTop)
        {
            int key = rowFromTop * 64 + col;
            if (cache.TryGetValue(key, out var sp)) return sp;
            if (atlas == null)
            {
                atlas = Resources.Load<Texture2D>("Tiles/terrain");
                if (atlas == null) return null;
                atlas.filterMode = FilterMode.Point;
                atlas.wrapMode = TextureWrapMode.Clamp;
            }
            int rows = atlas.height / Cell;
            sp = Sprite.Create(atlas, new Rect(col * Cell, (rows - 1 - rowFromTop) * Cell, Cell, Cell), new Vector2(0.5f, 0.5f), Cell);
            cache[key] = sp;
            return sp;
        }

        static string Id(GameState s, int x, int y) => s.InBounds(x, y) ? s.TerrainAt(x, y).Id : null;
        static bool IsWater(string id) => id == "SEA" || id == "RIVER" || id == "LAKE";

        /// <summary>The picture for a non-property tile, or null when there is none (the plain colour is used).</summary>
        public static Sprite Get(GameState s, int x, int y)
        {
            string id = Id(s, x, y);
            int v = ((x * 7 + y * 13) & 1);
            switch (id)
            {
                case "PLAIN": return At(v, 0);
                case "FOREST": return At(2, 0);
                case "MOUNTAIN": return At(3, 0);
                case "SEA": return At(4 + v, 0);
                case "LAKE": return At(6, 0);
                case "BEACH": return At(Mask(s, x, y, IsWater), 4);     // surf on the sides that face water
                case "BRIDGE": { int c = BridgeCell(s, x, y); return c >= 8 ? At(Mask(s, x, y, RiverJoins), 2) : At(c, 3); }
                case "ROAD": return At(Mask(s, x, y, n => n == "ROAD" || n == "BRIDGE" || (n != null && s.Data.Terrains.Find(t => t.Id == n).IsProperty)), 1);
                case "RIVER": return At(Mask(s, x, y, RiverJoins), 2);
            }
            return null;
        }

        static bool RiverJoins(string n) => n == null || IsWater(n) || n == "BRIDGE";

        /// <summary>The deck of a bridge over a river (drawn over the river picture), else null.</summary>
        public static Sprite Deck(GameState s, int x, int y)
        {
            if (Id(s, x, y) != "BRIDGE") return null;
            int c = BridgeCell(s, x, y);
            return c >= 8 ? At(c, 3) : null;
        }

        /// <summary>Sides that connect; past the map edge a road or river carries on.</summary>
        static int Mask(GameState s, int x, int y, System.Func<string, bool> joins, bool edgeJoins = true)
        {
            int m = 0;
            for (int k = 0; k < 4; k++)
            {
                var n = Id(s, x + DX[k], y + DY[k]);
                if (n == null ? edgeJoins : joins(n)) m |= 1 << k;
            }
            return m;
        }

        /// <summary>
        /// Bridges are drawn like the original's map chips: a bridge and the bridges next to it form one
        /// bridge, laid the way that has land at both ends (else along its longer side). Railings only on
        /// the sides that face water, so bridges side by side make one wide deck; the water under it is
        /// river water when the bridge crosses a river.
        /// Atlas row 3: column = river*8 + vertical*4 + (railing on the top/left side) + 2*(on the bottom/right side).
        /// </summary>
        static int BridgeCell(GameState s, int x, int y)
        {
            var seen = new HashSet<int> { s.Index(x, y) };
            var stack = new Stack<int>(); stack.Push(s.Index(x, y));
            int x0 = x, x1 = x, y0 = y, y1 = y; bool river = false;
            while (stack.Count > 0)
            {
                int i = stack.Pop(), cx = i % s.Width, cy = i / s.Width;
                x0 = Mathf.Min(x0, cx); x1 = Mathf.Max(x1, cx); y0 = Mathf.Min(y0, cy); y1 = Mathf.Max(y1, cy);
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    var n = Id(s, nx, ny);
                    if (n == "RIVER") river = true;
                    if (n == "BRIDGE" && seen.Add(s.Index(nx, ny))) stack.Push(s.Index(nx, ny));
                }
            }
            bool Land(int lx, int ly) { var n = Id(s, lx, ly); return n != null && !IsWater(n) && n != "BEACH" && n != "BRIDGE"; }
            bool hl = false, hr = false, vt = false, vb = false;
            for (int cy = y0; cy <= y1; cy++) { hl |= Land(x0 - 1, cy); hr |= Land(x1 + 1, cy); }
            for (int cx = x0; cx <= x1; cx++) { vt |= Land(cx, y0 - 1); vb |= Land(cx, y1 + 1); }
            bool h = hl && hr, v = vt && vb;
            bool horizontal = h != v ? h : (x1 - x0) >= (y1 - y0);
            bool railA = horizontal ? Id(s, x, y - 1) != "BRIDGE" : Id(s, x - 1, y) != "BRIDGE";
            bool railB = horizontal ? Id(s, x, y + 1) != "BRIDGE" : Id(s, x + 1, y) != "BRIDGE";
            return (river ? 8 : 0) + (horizontal ? 0 : 4) + (railA ? 1 : 0) + (railB ? 2 : 0);
        }

        // ---------- borders between two kinds of ground ----------
        // The border picture a tile draws on its side that touches another kind of ground is looked up
        // by (kind of this tile, kind of the neighbour). Each style is 8 atlas cells from row 5 on, two
        // styles per row: 4 sides (0 up, 1 right, 2 down, 3 left), then 4 inner corners (UL, UR, DR, DL).
        enum Ground { None, Plain, Forest, Mountain, Road, Property, Port, Sand, Water, River, Bridge }
        const int Coast = 0, SandGrass = 8, Foothill = 16, ForestShade = 24, RockyCoast = 32, ForestLink = 40, Ridge = 48, Shallow = 56,
            SandForest = 64, SandFoot = 72, ForestFoot = 80, RiverFill = 88, BankShade = 96, BankFoot = 104;
        static readonly HashSet<int> NoCorners = new HashSet<int> { ForestLink, Ridge };
        static readonly Dictionary<(Ground, Ground), int> EdgeTable = BuildEdgeTable();

        static Dictionary<(Ground, Ground), int> BuildEdgeTable()
        {
            var t = new Dictionary<(Ground, Ground), int>();
            // sea / lake: shallow water and surf on the water side; rocks where a mountain meets the sea
            foreach (var g in new[] { Ground.Plain, Ground.Road, Ground.Property, Ground.Forest }) t[(Ground.Water, g)] = Coast;
            t[(Ground.Water, Ground.Sand)] = Shallow;    // the beach draws the surf itself: only the shallow band here
            t[(Ground.Water, Ground.Mountain)] = RockyCoast;
            // beach: it gives way to the neighbour's ground (grass, forest floor, a mountain's stony ground)
            t[(Ground.Sand, Ground.Plain)] = SandGrass;
            t[(Ground.Sand, Ground.Forest)] = SandForest;
            t[(Ground.Sand, Ground.Mountain)] = SandFoot;
            // grass-covered squares fade into the forest floor of woods and the stony ground of mountains
            foreach (var g in new[] { Ground.Plain, Ground.Road }) t[(g, Ground.Mountain)] = Foothill;
            foreach (var g in new[] { Ground.Plain, Ground.Road }) t[(g, Ground.Forest)] = ForestShade;
            // a river's grass bank takes on the ground of woods and mountains next to it (the bank only, not the water)
            t[(Ground.River, Ground.Mountain)] = BankFoot;
            t[(Ground.River, Ground.Forest)] = BankShade;
            t[(Ground.Forest, Ground.Mountain)] = ForestFoot;
            // the same ground on both sides: woods and mountain ranges run on into the next square
            t[(Ground.Forest, Ground.Forest)] = ForestLink;
            t[(Ground.Mountain, Ground.Mountain)] = Ridge;
            // (towns, bases and the other properties fill their whole square: no border with them)
            return t;
        }
        static readonly int[] DiagX = { -1, 1, 1, -1 }, DiagY = { -1, -1, 1, 1 };

        static Ground Class(string id)
        {
            switch (id)
            {
                case null: return Ground.None;
                case "SEA": case "LAKE": return Ground.Water;
                case "BEACH": return Ground.Sand;
                case "RIVER": return Ground.River;
                case "BRIDGE": return Ground.Bridge;
                case "PLAIN": return Ground.Plain;
                case "FOREST": return Ground.Forest;
                case "MOUNTAIN": return Ground.Mountain;
                case "ROAD": return Ground.Road;
                case "PORT": return Ground.Port;     // its harbour water joins the sea: no coast
                default: return Ground.Property;
            }
        }

        static Sprite Piece(int style, int piece) { int c = style + piece; return At(c % 16, 5 + c / 16); }

        /// <summary>The border pictures to draw over tile (x, y), from the edge table.</summary>
        public static List<Sprite> Borders(GameState s, int x, int y)
        {
            var list = new List<Sprite>();
            var me = Class(Id(s, x, y));
            var side = new int[4];
            for (int k = 0; k < 4; k++)
            {
                side[k] = EdgeTable.TryGetValue((me, Class(Id(s, x + DX[k], y + DY[k]))), out int st) ? st : -1;
                if (side[k] >= 0) list.Add(Piece(side[k], k));
            }
            // an inner corner: the other ground only diagonally, neither side next to it has a border
            for (int k = 0; k < 4; k++)
            {
                if (!EdgeTable.TryGetValue((me, Class(Id(s, x + DiagX[k], y + DiagY[k]))), out int st) || NoCorners.Contains(st)) continue;
                int sx = DiagX[k] < 0 ? 3 : 1, sy = DiagY[k] < 0 ? 0 : 2;
                if (side[sx] < 0 && side[sy] < 0) list.Add(Piece(st, 4 + k));
            }
            // a wide river: water on both sides and diagonally, so the corner is water too
            bool river = me == Ground.River || (me == Ground.Bridge && BridgeCell(s, x, y) >= 8);
            if (river)
                for (int k = 0; k < 4; k++)
                    if (RiverJoins(Id(s, x + DiagX[k], y)) && RiverJoins(Id(s, x, y + DiagY[k])) && RiverJoins(Id(s, x + DiagX[k], y + DiagY[k])))
                        list.Add(Piece(RiverFill, 4 + k));
            return list;
        }
    }
}
