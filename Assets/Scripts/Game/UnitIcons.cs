using System.Collections.Generic;
using FamiconWars.Core;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// Pixel-art unit icons (32x32, facing right) from Resources/Units/units.png:
    /// one column per unit type, red army on the top row, blue on the bottom row.
    /// </summary>
    public static class UnitIcons
    {
        static readonly string[] Order = { "INF", "MECH", "TANK_A", "TANK_B", "APC", "ART_A", "ART_B", "AAM", "AAG", "SUP", "FTR_A", "FTR_B", "BMB", "HELI", "BSHIP", "LANDER" };
        const int Cell = 32;
        static Texture2D atlas;
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>The icon for a unit type and army, or null if the atlas has no such column.</summary>
        public static Sprite Get(string unitId, Army army)
        {
            string key = unitId + (army == Army.Blue ? "_b" : "_r");
            if (cache.TryGetValue(key, out var sp)) return sp;
            if (atlas == null)
            {
                atlas = Resources.Load<Texture2D>("Units/units");
                if (atlas == null) return null;
                atlas.filterMode = FilterMode.Point;
                atlas.wrapMode = TextureWrapMode.Clamp;
            }
            int col = System.Array.IndexOf(Order, unitId);
            if (col < 0) return null;
            int row = army == Army.Blue ? 0 : 1;   // texture rows count from the bottom
            sp = Sprite.Create(atlas, new Rect(col * Cell, row * Cell, Cell, Cell), new Vector2(0.5f, 0.5f), Cell);
            sp.name = key;
            cache[key] = sp;
            return sp;
        }
    }
}
