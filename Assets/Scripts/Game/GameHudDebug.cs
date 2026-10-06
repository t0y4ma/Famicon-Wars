using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>The "DEBUG" badge in the bottom-right corner while debug mode is on (above every screen).</summary>
    public partial class GameHud
    {
        RectTransform debugBadge;
        TextMeshProUGUI debugBadgeLabel;

        /// <summary>Shows the badge with the given text, or hides it (null).</summary>
        public void SetDebugBadge(string text)
        {
            if (debugBadge == null)
            {
                if (string.IsNullOrEmpty(text)) return;
                // its own canvas drawn after the HUD, so no screen can cover it
                var go = new GameObject("DebugBadge", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                go.transform.SetParent(transform, false);
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 50;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                var root = (RectTransform)go.transform;
                debugBadge = Panel("Plate", root, Danger);
                debugBadge.GetComponent<Image>().raycastTarget = false;
                Pin(debugBadge, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 16), new Vector2(300, 44), pivot: new Vector2(1, 0));
                debugBadgeLabel = Label("Text", debugBadge, SizeSmall, Paper, TextAlignmentOptions.Center, true);
                Fill(debugBadgeLabel.rectTransform, 12, 0, 12, 0);
            }
            bool on = !string.IsNullOrEmpty(text);
            debugBadge.parent.gameObject.SetActive(on);
            if (on) debugBadgeLabel.text = text;
        }
    }
}
