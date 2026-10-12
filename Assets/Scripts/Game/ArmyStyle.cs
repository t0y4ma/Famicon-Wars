using FamiconWars.Core;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// How each army looks: board colour, interface colour, text colour, and which way its units face.
    /// One row per army in turn order (see Armies); a fifth army needs a row here and a row in the
    /// unit and property atlases.
    /// </summary>
    public static class ArmyStyle
    {
        static readonly (string board, string ui, string text)[] Rows =
        {
            ("#DB3833", "#C8423A", "#FF8A7E"),   // red
            ("#336BE6", "#3A6AD0", "#8FB2FF"),   // blue
            ("#2E9A48", "#2F8F48", "#86DA8F"),   // green
            ("#E3B21F", "#C79A1E", "#FFD978"),   // yellow
        };
        static readonly Color NeutralBoard = new Color(0.55f, 0.55f, 0.55f);
        static readonly Color NeutralUi = Hex("#8C8F86");

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static bool Has(Army a) => a != Army.None && (int)a < Rows.Length;

        /// <summary>Colour on the board (owner marks, the minimap, units without a picture).</summary>
        public static Color Board(Army a) => Has(a) ? Hex(Rows[(int)a].board) : NeutralBoard;
        /// <summary>Colour of panels and stripes in the interface.</summary>
        public static Color Ui(Army a) => Has(a) ? Hex(Rows[(int)a].ui) : NeutralUi;
        /// <summary>Colour for the army's name in text on dark panels.</summary>
        public static Color Text(Army a) => Has(a) ? Hex(Rows[(int)a].text) : NeutralUi;
        public static string TextHex(Army a) => "#" + ColorUtility.ToHtmlStringRGB(Text(a));
        /// <summary>Armies take turns facing right and left, so neighbours face each other.</summary>
        public static bool FacesLeft(Army a) => a != Army.None && ((int)a & 1) == 1;
    }
}
