using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>
    /// Sound settings: music on/off and the two volumes as sliders. A small window opened from the title
    /// screen and the online lobby; the match settings screen uses the same sliders. Changes are heard at
    /// once and saved.
    /// </summary>
    public partial class GameHud
    {
        RectTransform soundRoot;
        GameSettings soundSettings;
        Button soundBgmOn, soundBgmOff;
        Slider soundBgmVol, soundSeVol;
        TextMeshProUGUI soundBgmVal, soundSeVal;

        /// <summary>A horizontal 0-100 slider with its value shown on the right.</summary>
        Slider MakeSlider(string name, RectTransform parent, Vector2 pos, float width, out TextMeshProUGUI value, Action<int> changed, Action released)
        {
            var root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            Pin(root, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(width, 56), pivot: new Vector2(0, 1));

            var track = Panel("Track", root, PlateEdge);
            track.anchorMin = new Vector2(0, 0.5f); track.anchorMax = new Vector2(1, 0.5f); track.pivot = new Vector2(0.5f, 0.5f);
            track.offsetMin = new Vector2(12, -5); track.offsetMax = new Vector2(-112, 5);

            var fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(track, false);
            Fill(fillArea, 0, 0, 0, 0);
            var fill = Panel("Fill", fillArea, Brass);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0, 1); fill.offsetMin = fill.offsetMax = Vector2.zero;

            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(track, false);
            Fill(handleArea, 0, 0, 0, 0);
            var handle = Panel("Handle", handleArea, Paper);
            handle.sizeDelta = new Vector2(22, 40);
            var himg = handle.GetComponent<Image>();
            himg.raycastTarget = true;

            var trackImg = track.GetComponent<Image>();
            trackImg.raycastTarget = true;
            var s = track.gameObject.AddComponent<Slider>();
            s.fillRect = fill; s.handleRect = handle; s.targetGraphic = himg;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = 0; s.maxValue = 100; s.wholeNumbers = true;
            var cb = s.colors;
            cb.normalColor = Paper; cb.highlightedColor = Hex("#FFE9A8"); cb.pressedColor = Brass; cb.selectedColor = Hex("#FFE9A8");
            cb.fadeDuration = 0.06f;
            s.colors = cb;

            var val = Label("Value", root, SizeBody, Paper, TextAlignmentOptions.MidlineRight, true);
            Pin(val.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(80, 44), pivot: new Vector2(1, 0.5f));
            value = val;
            var shown = val;
            s.onValueChanged.AddListener(v => { shown.text = Mathf.RoundToInt(v).ToString(); changed(Mathf.RoundToInt(v)); });
            if (released != null) track.gameObject.AddComponent<SliderRelease>().Released = released;
            return s;
        }

        static void SetSlider(Slider s, TextMeshProUGUI value, int v)
        {
            s.SetValueWithoutNotify(v);
            value.text = v.ToString();
        }

        void BuildSoundPanel()
        {
            // full-screen dimmer: a click outside the window closes it
            soundRoot = Panel("SoundSettings", canvasRt, new Color(0, 0, 0, 0.55f));
            Fill(soundRoot, 0, 0, 0, 0);
            soundRoot.GetComponent<Image>().raycastTarget = true;
            soundRoot.gameObject.AddComponent<Button>().onClick.AddListener(HideSoundPanel);

            var card = Panel("Card", soundRoot, Plate);
            card.GetComponent<Image>().raycastTarget = true;
            card.gameObject.AddComponent<ClickSink>();             // clicks on the window do not reach the dimmer
            Pin(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 440));
            Edge(card, Brass);
            var title = Label("Title", card, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "サウンド";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -28), new Vector2(400, 56), pivot: new Vector2(0, 1));

            SoundRowLabel(card, "BGM", -112);
            soundBgmOn = MakeButton("BgmOn", card, "オン", false, () => SetSoundBgm(true));
            Pin((RectTransform)soundBgmOn.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(240, -112), new Vector2(150, 56), pivot: new Vector2(0, 1));
            soundBgmOff = MakeButton("BgmOff", card, "オフ", false, () => SetSoundBgm(false));
            Pin((RectTransform)soundBgmOff.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(400, -112), new Vector2(150, 56), pivot: new Vector2(0, 1));

            SoundRowLabel(card, "BGM 音量", -192);
            soundBgmVol = MakeSlider("BgmVolume", card, new Vector2(240, -192), 540, out soundBgmVal,
                v => { soundSettings.BgmVolume = v; ApplySound(); }, null);
            SoundRowLabel(card, "効果音 音量", -272);
            soundSeVol = MakeSlider("SeVolume", card, new Vector2(240, -272), 540, out soundSeVal,
                v => { soundSettings.SeVolume = v; ApplySound(); }, () => GameAudio.Se("capture_done", 1f, 0));

            var close = MakeButton("Close", card, "閉じる", true, HideSoundPanel);
            Pin((RectTransform)close.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 32), new Vector2(200, 60), pivot: new Vector2(1, 0));
            soundRoot.gameObject.SetActive(false);
        }

        void SoundRowLabel(RectTransform parent, string text, float y)
        {
            var l = Label("L_" + text, parent, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            l.text = text;
            Pin(l.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, y), new Vector2(190, 56), pivot: new Vector2(0, 1));
        }

        /// <summary>Opens the sound window over the current screen; changes apply at once and are saved.</summary>
        public void ShowSoundPanel(GameSettings s)
        {
            soundSettings = s;
            StyleButton(soundBgmOn, s.BgmEnabled);
            StyleButton(soundBgmOff, !s.BgmEnabled);
            SetSlider(soundBgmVol, soundBgmVal, s.BgmVolume);
            SetSlider(soundSeVol, soundSeVal, s.SeVolume);
            soundRoot.SetAsLastSibling();
            soundRoot.gameObject.SetActive(true);
            SettleButtons(soundRoot);
        }

        public void HideSoundPanel()
        {
            if (soundRoot == null || !soundRoot.gameObject.activeSelf) return;
            soundRoot.gameObject.SetActive(false);
            soundSettings?.Save();
        }

        public bool SoundPanelVisible => soundRoot != null && soundRoot.gameObject.activeSelf;

        void SetSoundBgm(bool on)
        {
            soundSettings.BgmEnabled = on;
            StyleButton(soundBgmOn, on);
            StyleButton(soundBgmOff, !on);
            ApplySound();
        }

        void ApplySound() => GameAudio.Apply(soundSettings);
    }

    /// <summary>Swallows clicks so they do not bubble up to a parent that closes the window.</summary>
    public sealed class ClickSink : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData e) { }
    }

    /// <summary>Reports when the pointer lets go of a slider (to play a sample of the new volume once).</summary>
    public sealed class SliderRelease : MonoBehaviour, IPointerUpHandler
    {
        public Action Released;
        public void OnPointerUp(PointerEventData e) => Released?.Invoke();
    }
}
