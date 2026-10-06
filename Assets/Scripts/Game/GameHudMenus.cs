using System;
using System.Collections.Generic;
using FamiconWars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>Title, map select and pre-match setup screens. Built once; choices update in place (no rebuild, no flicker).</summary>
    public partial class GameHud
    {
        public event Action OnTitleOnline, OnSoundSettings;
        public event Action OnTitleStart, OnMapBack, OnMapNext, OnSetupBack, OnSetupStart, OnBackToTitle;
        public event Action<string> OnMapPicked;

        RectTransform titleRoot, mapRoot, setupRoot, mapGrid;
        Button titleStart, mapNext, setupStart;
        TextMeshProUGUI setupMapName;
        readonly List<(MapEntry map, Button button, Image marker, TextMeshProUGUI name)> mapCards = new List<(MapEntry, Button, Image, TextMeshProUGUI)>();

        GameSettings editing;
        bool setupRowsBuilt;
        List<Button> segRed, segBlue, segSpeed, segAnim, segBgm;
        Slider setupBgmVol, setupSeVol;
        TextMeshProUGUI setupBgmVal, setupSeVal;
        static readonly int[] VolumeSteps = { 0, 25, 50, 75, 100 };
        static readonly Color Backdrop = new Color(0.055f, 0.065f, 0.05f, 0.9f);

        void BuildMenus()
        {
            BuildTitle();
            BuildMapSelect();
            BuildSetupScreen();
            AddTitleButtonToGameOver();
            BuildOnline();
            BuildReplay();
            BuildCameraWidget();
            BuildHowTo();
            BuildSoundPanel();
        }

        public void HideMenus()
        {
            titleRoot.gameObject.SetActive(false);
            mapRoot.gameObject.SetActive(false);
            setupRoot.gameObject.SetActive(false);
            HideOnline();
            if (recordsRoot != null) recordsRoot.gameObject.SetActive(false);
            if (replayBar != null) replayBar.gameObject.SetActive(false);
            if (howRoot != null) howRoot.gameObject.SetActive(false);
        }

        void ShowOnly(RectTransform root, Button focus)
        {
            HideMenus();
            HideProduce(); HideActionMenu(); Hint(null);
            overPanel.gameObject.SetActive(false);
            dim.gameObject.SetActive(false);
            ShowMatchChrome(false);
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
            if (focus != null) EventSystem.current?.SetSelectedGameObject(focus.gameObject);
        }

        // ================= title =================

        void BuildTitle()
        {
            titleRoot = Panel("TitleScreen", canvasRt, Backdrop);
            Fill(titleRoot, 0, 0, 0, 0);

            var over = Label("Overline", titleRoot, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            over.text = "ターン制ウォー・シミュレーション";
            Pin(over.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, 250), new Vector2(900, 40), pivot: new Vector2(0, 0.5f));

            var name = Label("Name", titleRoot, 128, Paper, TextAlignmentOptions.MidlineLeft, true);
            name.text = "フィールド・コマンド";
            Pin(name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(152, 140), new Vector2(1500, 160), pivot: new Vector2(0, 0.5f));

            // signature: the two armies as one split stripe under the name
            var red = Panel("StripeRed", titleRoot, ArmyColor(Army.Red));
            Pin(red, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, 46), new Vector2(440, 14), pivot: new Vector2(0, 0.5f));
            var blue = Panel("StripeBlue", titleRoot, ArmyColor(Army.Blue));
            Pin(blue, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(608, 46), new Vector2(440, 14), pivot: new Vector2(0, 0.5f));

            var sub = Label("Sub", titleRoot, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            sub.text = "どちらかの基地が落ちるまで。";
            Pin(sub.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, 0), new Vector2(900, 40), pivot: new Vector2(0, 0.5f));

            titleStart = MakeButton("Start", titleRoot, "はじめる", true, () => OnTitleStart?.Invoke());
            titleStart.GetComponentInChildren<TextMeshProUGUI>().fontSize = SizeNumber;
            Pin((RectTransform)titleStart.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, -120), new Vector2(340, 76), pivot: new Vector2(0, 0.5f));

            var online = MakeButton("Online", titleRoot, "オンライン対戦", false, () => OnTitleOnline?.Invoke());
            Pin((RectTransform)online.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, -212), new Vector2(340, 64), pivot: new Vector2(0, 0.5f));

            var sound = MakeButton("Sound", titleRoot, "サウンド", false, () => OnSoundSettings?.Invoke());
            Pin((RectTransform)sound.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, -452), new Vector2(340, 64), pivot: new Vector2(0, 0.5f));
            var how = MakeButton("HowTo", titleRoot, "あそびかた", false, () => ShowHowTo());
            Pin((RectTransform)how.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, -292), new Vector2(340, 64), pivot: new Vector2(0, 0.5f));

            var ver = Label("Version", titleRoot, SizeSmall, Muted, TextAlignmentOptions.BottomRight, false);
            ver.text = "beta v0.9";
            Pin(ver.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-32, 24), new Vector2(400, 30), pivot: new Vector2(1, 0));

            titleRoot.gameObject.SetActive(false);
        }

        public void ShowTitle() => ShowOnly(titleRoot, titleStart);
        public bool TitleVisible => titleRoot != null && titleRoot.gameObject.activeSelf;
        /// <summary>Screens that keep the title music: title, map select, match settings, how to play, match records.</summary>
        public bool TitleMusicScreen => TitleVisible || (mapRoot != null && mapRoot.gameObject.activeSelf)
            || (setupRoot != null && setupRoot.gameObject.activeSelf) || HowToVisible || RecordsVisible;

        // ================= map select =================

        void BuildMapSelect()
        {
            mapRoot = Panel("MapSelect", canvasRt, Backdrop);
            Fill(mapRoot, 0, 0, 0, 0);

            var title = Label("Heading", mapRoot, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "マップ選択";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -48), new Vector2(600, 60), pivot: new Vector2(0, 1));
            var sub = Label("Sub", mapRoot, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            sub.text = "遊ぶマップを選んでください";
            Pin(sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -108), new Vector2(800, 36), pivot: new Vector2(0, 1));

            var grid = new GameObject("Grid", typeof(RectTransform));
            mapGrid = (RectTransform)grid.transform; mapGrid.SetParent(mapRoot, false);
            Pin(mapGrid, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1440, 540));
            var gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(464, 258); gl.spacing = new Vector2(24, 24);
            gl.childAlignment = TextAnchor.UpperCenter; gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 3;

            var back = MakeButton("Back", mapRoot, "戻る", false, () => OnMapBack?.Invoke());
            Pin((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(96, 56), new Vector2(200, 64), pivot: new Vector2(0, 0));
            mapNext = MakeButton("Next", mapRoot, "次へ", true, () => OnMapNext?.Invoke());
            Pin((RectTransform)mapNext.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-96, 56), new Vector2(260, 64), pivot: new Vector2(1, 0));

            mapRoot.gameObject.SetActive(false);
        }

        /// <param name="preview">Mini map for a ready map id (null for none).</param>
        public void ShowMapSelect(MapEntry[] maps, string selectedId, Func<string, Texture2D> preview)
        {
            if (mapCards.Count == 0)
                foreach (var m in maps) mapCards.Add(MakeMapCard(m, preview));
            MarkMap(selectedId);
            ShowOnly(mapRoot, mapNext);
        }

        (MapEntry, Button, Image, TextMeshProUGUI) MakeMapCard(MapEntry m, Func<string, Texture2D> preview)
        {
            var b = MakeButton("Map_" + m.Id, mapGrid, "", false, () => { OnMapPicked?.Invoke(m.Id); MarkMap(m.Id); });
            b.interactable = m.Ready;
            var rt = (RectTransform)b.transform;
            var empty = b.transform.Find("Text");
            if (empty != null) empty.gameObject.SetActive(false);

            // the chosen card gets a brass frame on all four sides (the left focus bar would double it)
            var focusBar = b.transform.Find("Focus");
            if (focusBar != null) Destroy(focusBar.gameObject);
            var marker = Panel("Selected", rt, new Color(0, 0, 0, 0)).GetComponent<Image>();
            marker.raycastTarget = false;
            Fill((RectTransform)marker.transform, 0, 0, 0, 0);
            foreach (var side in new[] { new Vector4(0, 0, 1, 0), new Vector4(0, 1, 1, 1), new Vector4(0, 0, 0, 1), new Vector4(1, 0, 1, 1) })
            {
                var bar = Panel("FrameEdge", (RectTransform)marker.transform, Brass);
                bar.GetComponent<Image>().raycastTarget = false;
                bar.anchorMin = new Vector2(side.x, side.y); bar.anchorMax = new Vector2(side.z, side.w);
                bool horizontal = side.y == side.w;
                bar.pivot = new Vector2(side.x, side.y);
                bar.sizeDelta = horizontal ? new Vector2(0, 4) : new Vector2(4, 0);
                bar.anchoredPosition = Vector2.zero;
            }

            // preview area (left)
            var frame = Panel("PreviewFrame", rt, Plate);
            frame.GetComponent<Image>().raycastTarget = false;
            Pin(frame, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(200, 200), pivot: new Vector2(0, 0.5f));
            var tex = m.Ready ? preview?.Invoke(m.Id) : null;
            if (tex != null)
            {
                var ri = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                ri.transform.SetParent(frame, false);
                ri.texture = tex; ri.raycastTarget = false;
                float aspect = (float)tex.width / tex.height;
                var size = aspect >= 1 ? new Vector2(184, 184 / aspect) : new Vector2(184 * aspect, 184);
                Pin(ri.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            }
            else
            {
                var q = Label("Unknown", frame, 64, PlateEdge, TextAlignmentOptions.Center, true);
                q.text = "?";
                Fill(q.rectTransform, 0, 0, 0, 0);
            }

            var name = Label("Name", rt, SizeTitle - 6, m.Ready ? Paper : Muted, TextAlignmentOptions.TopLeft, true);
            name.text = m.Name;
            Pin(name.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(124, -26), new Vector2(-268, 44), pivot: new Vector2(0.5f, 1), stretchX: true);
            var size2 = Label("Size", rt, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            size2.text = m.Ready ? m.Width + " × " + m.Height : "";
            Pin(size2.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(124, -74), new Vector2(-268, 28), pivot: new Vector2(0.5f, 1), stretchX: true);
            var note = Label("Note", rt, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.text = m.Note;
            Pin(note.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(124, -108), new Vector2(-268, 120), pivot: new Vector2(0.5f, 1), stretchX: true);
            if (!m.Ready) WipBadge(rt, new Vector2(-16, 16), new Vector2(1, 0));   // bottom-right: never covers the name
            return (m, b, marker, name);
        }

        void MarkMap(string id)
        {
            foreach (var c in mapCards)
            {
                bool on = c.map.Id == id;
                c.marker.gameObject.SetActive(on);
                if (c.map.Ready) c.name.color = on ? Brass : Paper;
            }
        }

        // ================= pre-match setup =================

        RectTransform setupCard;

        void BuildSetupScreen()
        {
            setupRoot = Panel("SetupScreen", canvasRt, Backdrop);
            Fill(setupRoot, 0, 0, 0, 0);
            setupCard = Panel("Card", setupRoot, Plate);
            Pin(setupCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1120, 900));
            Edge(setupCard, Brass);
            var title = Label("Title", setupCard, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "対戦設定";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -20), new Vector2(300, 60), pivot: new Vector2(0, 1));
            setupMapName = Label("Map", setupCard, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            Pin(setupMapName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -32), new Vector2(600, 40), pivot: new Vector2(0, 1));

            var back = MakeButton("Back", setupCard, "戻る", false, () => OnSetupBack?.Invoke());
            Pin((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 32), new Vector2(200, 64), pivot: new Vector2(0, 0));
            setupStart = MakeButton("Start", setupCard, "開始", true, () => OnSetupStart?.Invoke());
            Pin((RectTransform)setupStart.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 32), new Vector2(260, 68), pivot: new Vector2(1, 0));
            var note = Label("Note", setupCard, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.text = "両軍をCOMにすると観戦。観戦中は Space で一時停止。設定は次回も残ります。";
            Pin(note.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(264, 40), new Vector2(520, 52), pivot: new Vector2(0, 0));

            setupRoot.gameObject.SetActive(false);
        }

        void BuildSetupRows()
        {
            setupRowsBuilt = true;
            string[] kinds = { "人間", "COM 弱い", "COM 普通", "COM 強い", "COM 最強" };
            ArmyTab(Army.Red, -104);
            segRed = Segment(-104, 64, kinds, i => editing.Players[0] = i);
            ArmyTab(Army.Blue, -184);
            segBlue = Segment(-184, 64, kinds, i => editing.Players[1] = i);
            Rule(-268);

            RowLabel("ゲーム速度", -292);
            segSpeed = Segment(-292, 56, GameSettings.SpeedNames, i => editing.Speed = i);
            RowLabel("戦闘アニメ", -364);
            segAnim = Segment(-364, 56, new[] { "オン", "オフ" }, i => editing.BattleAnimation = i == 0);
            Rule(-440);

            var head = RowLabel("サウンド", -460);
            head.color = Paper; head.fontStyle = FontStyles.Bold;
            var soundNote = Label("SoundNote", setupCard, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            soundNote.text = "変えるとすぐに反映されます(効果音は試しに鳴ります)";
            Pin(soundNote.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(240, -466), new Vector2(700, 32), pivot: new Vector2(0, 1));

            RowLabel("BGM", -512);
            segBgm = Segment(-512, 56, new[] { "オン", "オフ" }, i => { editing.BgmEnabled = i == 0; GameAudio.Apply(editing); });
            RowLabel("BGM 音量", -580);
            setupBgmVol = MakeSlider("BgmVolume", setupCard, new Vector2(240, -580), 780, out setupBgmVal, v => { editing.BgmVolume = v; GameAudio.Apply(editing); }, null);
            RowLabel("効果音 音量", -648);
            setupSeVol = MakeSlider("SeVolume", setupCard, new Vector2(240, -648), 780, out setupSeVal, v => { editing.SeVolume = v; GameAudio.Apply(editing); }, () => GameAudio.Se("capture_done", 1f, 0));
        }

        public void ShowSetupScreen(GameSettings s, string mapName)
        {
            editing = s;
            if (!setupRowsBuilt) BuildSetupRows();
            setupMapName.text = "マップ: " + mapName;
            Select(segRed, s.Players[0]);
            Select(segBlue, s.Players[1]);
            Select(segSpeed, s.Speed);
            Select(segAnim, s.BattleAnimation ? 0 : 1);
            Select(segBgm, s.BgmEnabled ? 0 : 1);
            SetSlider(setupBgmVol, setupBgmVal, s.BgmVolume);
            SetSlider(setupSeVol, setupSeVal, s.SeVolume);
            ShowOnly(setupRoot, setupStart);
        }

        static int Nearest(int v)
        {
            int best = VolumeSteps[0];
            foreach (var s in VolumeSteps) if (Math.Abs(s - v) < Math.Abs(best - v)) best = s;
            return best;
        }

        // ---------- small builders ----------

        void ArmyTab(Army a, float y)
        {
            var tab = Panel("Tab" + a, setupCard, ArmyColor(a));
            tab.GetComponent<Image>().raycastTarget = false;
            Pin(tab, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, y), new Vector2(180, 64), pivot: new Vector2(0, 1));
            var t = Label("Name", tab, SizeBody, Color.white, TextAlignmentOptions.Center, true);
            t.text = Labels.Army(a);
            Fill(t.rectTransform, 4, 0, 4, 0);
        }

        TextMeshProUGUI RowLabel(string text, float y)
        {
            var l = Label("Row_" + text, setupCard, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            l.text = text;
            Pin(l.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, y), new Vector2(190, 56), pivot: new Vector2(0, 1));
            return l;
        }

        void Rule(float y)
        {
            var r = Panel("Rule", setupCard, PlateEdge);
            r.GetComponent<Image>().raycastTarget = false;
            Pin(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, y), new Vector2(-80, 2), pivot: new Vector2(0.5f, 1), stretchX: true);
        }

        /// <summary>Segmented choice row. Picking one re-styles the row in place.</summary>
        List<Button> Segment(float y, float h, string[] labels, Action<int> pick)
        {
            var list = new List<Button>();
            for (int k = 0; k < labels.Length; k++)
            {
                int idx = k;
                var b = MakeButton("Seg_" + labels[k], setupCard, labels[k], false, () => { pick(idx); Select(list, idx); });
                Pin((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(240 + k * 160, y), new Vector2(150, h), pivot: new Vector2(0, 1));
                list.Add(b);
            }
            return list;
        }

        static void Select(List<Button> list, int idx)
        {
            for (int i = 0; i < list.Count; i++) Restyle(list[i], i == idx);
        }

        /// <summary>Switches a button between primary (brass) and normal styling without recreating it.</summary>
        static void Restyle(Button b, bool primary)
        {
            var cb = b.colors;
            var baseC = primary ? Brass : PlateRaised;
            cb.normalColor = baseC;
            cb.highlightedColor = primary ? Hex("#F0BE58") : Hex("#3E4B3A");
            cb.selectedColor = primary ? Hex("#F0BE58") : Hex("#4A5A3F");
            cb.pressedColor = primary ? Hex("#B9862A") : Hex("#263022");
            cb.disabledColor = new Color(baseC.r, baseC.g, baseC.b, 0.35f);
            b.colors = cb;   // the setter re-applies the current state instantly
            var label = b.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (label != null) label.color = primary ? Plate : Paper;
        }

        void WipBadge(RectTransform parent, Vector2 pos, Vector2 anchor)
        {
            var badge = Panel("WIP", parent, Danger);
            badge.GetComponent<Image>().raycastTarget = false;
            Pin(badge, anchor, anchor, pos, new Vector2(64, 28), pivot: anchor);
            var t = Label("Text", badge, SizeSmall, Plate, TextAlignmentOptions.Center, true);
            t.text = "WIP";
            Fill(t.rectTransform, 0, 0, 0, 0);
        }

        void AddTitleButtonToGameOver()
        {
            var again = overPanel.Find("Again") as RectTransform;
            if (again != null) again.anchoredPosition = new Vector2(130, 36);
            var toTitle = MakeButton("ToTitle", overPanel, "タイトルへ", false, () => OnBackToTitle?.Invoke());
            Pin((RectTransform)toTitle.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-130, 36), new Vector2(240, 60), pivot: new Vector2(0.5f, 0));
        }
    }
}
