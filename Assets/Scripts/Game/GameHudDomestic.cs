using System;
using System.Collections.Generic;
using FamiconWars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>
    /// 内政: the window for one facility (level, what the next level gives, reserve / convert / take back),
    /// and the plate shown in the 内政 time after a COM's phase (time left, 「内政終了」).
    /// The window does not cover the board: the match goes on behind it.
    /// </summary>
    public partial class GameHud
    {
        RectTransform domesticPanel, domesticBody;
        TextMeshProUGUI domesticTitle, domesticInfo;
        RectTransform interludePlate;
        TextMeshProUGUI interludeText;
        Button interludeBtn;
        Action interludeAct;

        /// <summary>One line of the window: a button with its price (or why it cannot be done) on the right.</summary>
        public struct DomesticItem
        {
            public string Label, Note;
            public bool Enabled, Primary;
            public Action Act;
        }

        void BuildDomestic()
        {
            domesticPanel = Panel("Domestic", canvasRt, Plate);
            Pin(domesticPanel, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 40), new Vector2(580, 300), pivot: new Vector2(1, 0.5f));
            Edge(domesticPanel, Brass);
            domesticPanel.GetComponent<Image>().raycastTarget = true;
            var vl = domesticPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(22, 22, 18, 18); vl.spacing = 8;
            vl.childForceExpandHeight = false; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandWidth = true;
            var fit = domesticPanel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            domesticTitle = Label("Title", domesticPanel, SizeNumber, Paper, TextAlignmentOptions.MidlineLeft, true);
            domesticTitle.richText = true;
            domesticTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            domesticInfo = Label("Info", domesticPanel, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            domesticInfo.richText = true; domesticInfo.textWrappingMode = TextWrappingModes.Normal; domesticInfo.lineSpacing = 6;
            domesticBody = new GameObject("Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
            domesticBody.SetParent(domesticPanel, false);
            var bl = domesticBody.gameObject.AddComponent<VerticalLayoutGroup>();
            bl.spacing = 6; bl.childForceExpandHeight = false; bl.childControlHeight = true; bl.childControlWidth = true;
            domesticPanel.gameObject.SetActive(false);

            interludePlate = Panel("Interlude", canvasRt, PlateRaised);
            Pin(interludePlate, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(860, 64), pivot: new Vector2(0.5f, 1));
            Edge(interludePlate, Brass);
            interludePlate.GetComponent<Image>().raycastTarget = true;
            interludeText = Label("Text", interludePlate, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, false);
            interludeText.richText = true;
            Fill(interludeText.rectTransform, 20, 0, 260, 0);
            interludeBtn = MakeButton("Done", interludePlate, "内政終了", true, () => interludeAct?.Invoke());
            Pin((RectTransform)interludeBtn.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(230, 48), pivot: new Vector2(1, 0.5f));
            interludePlate.gameObject.SetActive(false);
        }

        public bool DomesticVisible => domesticPanel != null && domesticPanel.gameObject.activeSelf;

        public void ShowDomestic(string title, string info, List<DomesticItem> items)
        {
            if (domesticPanel == null) BuildDomestic();
            domesticTitle.text = title;
            domesticInfo.text = info;
            domesticInfo.gameObject.SetActive(!string.IsNullOrEmpty(info));
            ClearChildren(domesticBody);
            foreach (var it in items)
            {
                var item = it;
                var b = MakeButton(item.Label, domesticBody, item.Label, item.Primary && item.Enabled, () => item.Act?.Invoke());
                b.interactable = item.Enabled && item.Act != null;
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
                var txt = b.GetComponentInChildren<TextMeshProUGUI>();
                txt.alignment = TextAlignmentOptions.MidlineLeft;
                Fill(txt.rectTransform, 16, 0, 300, 0);
                if (!string.IsNullOrEmpty(item.Note))
                {
                    var note = Label("Note", b.transform, SizeSmall, item.Enabled ? (item.Primary ? Plate : Paper) : Danger, TextAlignmentOptions.MidlineRight, false);
                    note.richText = true;
                    note.text = item.Note;
                    Fill(note.rectTransform, 150, 0, 14, 0);
                }
            }
            var close = MakeButton("Close", domesticBody, "閉じる", false, HideDomestic);
            close.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;
            domesticPanel.SetAsLastSibling();
            domesticPanel.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(domesticPanel);
            SettleButtons(domesticPanel);
        }

        public void HideDomestic()
        {
            if (domesticPanel != null) domesticPanel.gameObject.SetActive(false);
        }

        /// <summary>The 内政 time plate (text: time left etc.). buttonText null hides the button (nothing for this screen to press).</summary>
        public void ShowInterlude(bool on, string text = null, string buttonText = null, bool buttonEnabled = true, Action onDone = null)
        {
            if (interludePlate == null) BuildDomestic();
            if (!on) { if (interludePlate.gameObject.activeSelf) interludePlate.gameObject.SetActive(false); return; }
            SetText(interludeText, text ?? "");
            interludeAct = onDone;
            SetActive(interludeBtn, buttonText != null);
            if (buttonText != null)
            {
                SetText(interludeBtn.GetComponentInChildren<TextMeshProUGUI>(), buttonText);
                if (interludeBtn.interactable != buttonEnabled) interludeBtn.interactable = buttonEnabled;
            }
            if (!interludePlate.gameObject.activeSelf)
            {
                interludePlate.SetAsLastSibling();
                interludePlate.gameObject.SetActive(true);
            }
        }

        public bool InterludeVisible => interludePlate != null && interludePlate.gameObject.activeSelf;
    }
}
