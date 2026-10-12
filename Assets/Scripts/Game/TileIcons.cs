using System.Collections.Generic;
using FamiconWars.Core;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// Pixel-art property tiles (32x32, the whole square) from Resources/Tiles/tiles.png:
    /// columns HQ, FACTORY, CITY, AIRPORT, PORT; rows (top to bottom) one per army in turn order, then neutral.
    /// The roof / flag shows who owns the property.
    /// </summary>
    public static class TileIcons
    {
        static readonly string[] Order = { "HQ", "FACTORY", "CITY", "AIRPORT", "PORT" };
        const int Cell = 32;
        static Texture2D atlas;
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>The tile for a property and its owner, or null if the atlas has no such column.</summary>
        public static Sprite Get(string terrainId, Army owner)
        {
            string key = terrainId + "_" + (int)owner;
            if (cache.TryGetValue(key, out var sp)) return sp;
            if (atlas == null)
            {
                atlas = Resources.Load<Texture2D>("Tiles/tiles");
                if (atlas == null) return null;
                atlas.filterMode = FilterMode.Point;
                atlas.wrapMode = TextureWrapMode.Clamp;
            }
            int col = System.Array.IndexOf(Order, terrainId);
            if (col < 0) return null;
            int rows = atlas.height / Cell;   // texture rows count from the bottom: neutral is row 0
            int row = owner == Army.None ? 0 : rows - 1 - ((int)owner % (rows - 1));
            sp = Sprite.Create(atlas, new Rect(col * Cell, row * Cell, Cell, Cell), new Vector2(0.5f, 0.5f), Cell);
            sp.name = key;
            cache[key] = sp;
            return sp;
        }
    }
}
