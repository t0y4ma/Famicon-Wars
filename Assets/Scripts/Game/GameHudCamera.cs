using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>Zoom readout and a reset button (back to the starting view), under the top bar on the right.</summary>
    public partial class GameHud
    {
        public event Action OnZoomReset, OnMatchSound;

        RectTransform zoomRoot;
        Button matchSoundBtn;
        TextMeshProUGUI zoomLabel;

        void BuildCameraWidget()
        {
            zoomRoot = Panel("Zoom", canvasRt, new Color(Plate.r, Plate.g, Plate.b, 0.9f));
            Pin(zoomRoot, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -92), new Vector2(300, 52), pivot: new Vector2(1, 1));
            Edge(zoomRoot, PlateEdge);
            zoomLabel = Label("Label", zoomRoot, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, false);
            zoomLabel.text = "倍率 100%";
            Pin(zoomLabel.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(150, 40), pivot: new Vector2(0, 0.5f));
            var reset = MakeButton("Reset", zoomRoot, "リセット", false, () => OnZoomReset?.Invoke());
            Pin((RectTransform)reset.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(124, 40), pivot: new Vector2(1, 0.5f));
            zoomRoot.gameObject.SetActive(false);

            // sound settings during a match (also while watching)
            matchSoundBtn = MakeButton("MatchSound", canvasRt, "サウンド", false, () => OnMatchSound?.Invoke());
            Pin((RectTransform)matchSoundBtn.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -152), new Vector2(300, 48), pivot: new Vector2(1, 1));
            matchSoundBtn.gameObject.SetActive(false);
        }

        /// <summary>Zoom relative to the starting view, in percent.</summary>
        public void SetZoom(int percent)
        {
            if (zoomLabel != null) zoomLabel.text = "倍率 " + percent + "%";
        }

        void ShowZoomWidget(bool on)
        {
            if (zoomRoot == null) return;
            zoomRoot.gameObject.SetActive(on);
            if (on) zoomRoot.SetAsLastSibling();
            if (matchSoundBtn != null)
            {
                matchSoundBtn.gameObject.SetActive(on);
                if (on) matchSoundBtn.transform.SetAsLastSibling();
            }
        }
    }
}
