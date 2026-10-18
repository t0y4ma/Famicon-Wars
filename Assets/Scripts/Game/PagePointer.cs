using System.Runtime.InteropServices;

namespace FamiconWars.Game
{
    /// <summary>
    /// Whether the page has focus and the cursor is over the game. On WebGL the engine keeps the last
    /// cursor position after the pointer leaves the canvas or the tab loses focus, so this asks the page
    /// (Plugins/WebGL/FwPage.jslib). Elsewhere Application.isFocused and the screen bounds are enough.
    /// </summary>
    public static class PagePointer
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int FwPointerInPage();
        public static bool Inside => FwPointerInPage() != 0;
#else
        public static bool Inside => true;
#endif
    }
}
