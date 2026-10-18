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
        List<Button> segSpeed, segAnim;
        /// <summary>Per army: its row (tab, choices, 戦法 button); rows past the map's armies are hidden.</summary>
        readonly List<(RectTransform tab, List<Button> kind, Button style)> armyRows = new List<(RectTransform, List<Button>, Button)>();
        int setupPlayers = 2;
        Slider setupTurn;
        TextMeshProUGUI setupTurnVal, setupNote;
        static readonly string[] StyleNames = { "ランダム", "速攻", "物量", "精鋭" };
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
            howOverMatch = false;
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

            var sound = MakeButton("Sound", titleRoot, "設定", false, () => OnSoundSettings?.Invoke());
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
            || (setupRoot != null && setupRoot.gameObject.activeSelf) || (HowToVisible && !howOverMatch) || RecordsVisible;

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

            // the cards scroll (wheel / drag) when there are more maps than fit (maps made with the map editor)
            var view = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image)).GetComponent<RectTransform>();
            view.SetParent(mapRoot, false);
            view.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            Pin(view, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(1460, 690));
            var grid = new GameObject("Grid", typeof(RectTransform));
            mapGrid = (RectTransform)grid.transform; mapGrid.SetParent(view, false);
            mapGrid.anchorMin = new Vector2(0.5f, 1); mapGrid.anchorMax = new Vector2(0.5f, 1); mapGrid.pivot = new Vector2(0.5f, 1);
            mapGrid.anchoredPosition = new Vector2(0, -5); mapGrid.sizeDelta = new Vector2(1440, 680);
            grid.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = mapGrid; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
            var gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(464, 208); gl.spacing = new Vector2(24, 20);
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
            Pin(frame, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(172, 172), pivot: new Vector2(0, 0.5f));
            var tex = m.Ready ? preview?.Invoke(m.Id) : null;
            if (tex != null)
            {
                var ri = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                ri.transform.SetParent(frame, false);
                ri.texture = tex; ri.raycastTarget = false;
                float aspect = (float)tex.width / tex.height;
                var size = aspect >= 1 ? new Vector2(158, 158 / aspect) : new Vector2(158 * aspect, 158);
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
            Pin(name.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(106, -18), new Vector2(-232, 44), pivot: new Vector2(0.5f, 1), stretchX: true);
            var size2 = Label("Size", rt, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            size2.text = m.Ready ? m.Width + " × " + m.Height + "　" + (m.Players > 2 ? "<color=#E4B04A>" + m.Players + "軍</color>" : "2軍") : "";
            Pin(size2.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(106, -62), new Vector2(-232, 28), pivot: new Vector2(0.5f, 1), stretchX: true);
            var note = Label("Note", rt, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.text = m.Note;
            Pin(note.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(106, -94), new Vector2(-232, 108), pivot: new Vector2(0.5f, 1), stretchX: true);
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
            Pin(setupCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1280, 880));
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
            setupNote = Label("Note", setupCard, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            setupNote.textWrappingMode = TextWrappingModes.Normal;
            setupNote.text = "全軍をCOMにすると観戦。観戦中は Space で一時停止。設定は次回も残ります。サウンドはタイトルの「設定」で変えられます。";
            Pin(setupNote.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(264, 36), new Vector2(660, 60), pivot: new Vector2(0, 0));

            setupRoot.gameObject.SetActive(false);
        }

        const float ArmyRowTop = -104, ArmyRowStep = 76;

        void BuildSetupRows()
        {
            setupRowsBuilt = true;
            string[] kinds = { "人間", "COM 弱い", "COM 普通", "COM 強い", "COM 最強", "なし" };
            for (int a = 0; a < Armies.Max; a++)
            {
                int army = a;
                float y = ArmyRowTop - a * ArmyRowStep;
                var tab = ArmyTab((Army)a, y);
                var kind = Segment(y, 64, kinds, i => { editing.Players[army] = i == 5 ? GameSettings.Nobody : i; UpdateArmyRows(); }, 128, 136);
                Button style = null;
                style = MakeButton("Style" + a, setupCard, "", false, () =>
                {
                    editing.Styles[army] = (editing.Styles[army] + 1) % StyleNames.Length;
                    SetText(style.GetComponentInChildren<TextMeshProUGUI>(), StyleText(editing.Styles[army]));
                });
                Pin((RectTransform)style.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1064, y), new Vector2(176, 64), pivot: new Vector2(0, 1));
                armyRows.Add((tab, kind, style));
            }
            float y0 = ArmyRowTop - Armies.Max * ArmyRowStep;
            Rule(y0 - 4);
            RowLabel("ゲーム速度", y0 - 24);
            segSpeed = Segment(y0 - 24, 56, GameSettings.SpeedNames, i => editing.Speed = i);
            RowLabel("戦闘アニメ", y0 - 96);
            segAnim = Segment(y0 - 96, 56, new[] { "オン", "オフ" }, i => editing.BattleAnimation = i == 0);
            RowLabel("制限時間", y0 - 168);
            setupTurn = MakeSlider("TurnTime", setupCard, new Vector2(240, y0 - 168), 860, out setupTurnVal,
                v => editing.TurnSeconds = TurnTime.FromStep(v), null, 0, TurnTime.Steps, v => TurnTime.Label(TurnTime.FromStep(v)), 150);
            var turnNote = Label("TurnNote", setupCard, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            turnNote.text = "人間の手番の持ち時間。時間が来ると手番終了になります";
            Pin(turnNote.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(252, y0 - 222), new Vector2(900, 32), pivot: new Vector2(0, 1));
        }

        static string StyleText(int style) => "<size=75%><color=#A3AC92>戦法</color></size> " + StyleNames[style];

        /// <param name="players">Armies on the chosen map (rows past it are hidden; 「なし」 only with three or more).</param>
        public void ShowSetupScreen(GameSettings s, string mapName, int players = 2)
        {
            editing = s;
            setupPlayers = Mathf.Clamp(players, 2, Armies.Max);
            if (!setupRowsBuilt) BuildSetupRows();
            setupMapName.text = "マップ: " + mapName + (setupPlayers > 2 ? "(" + setupPlayers + "軍)" : "");
            UpdateArmyRows();
            Select(segSpeed, s.Speed);
            Select(segAnim, s.BattleAnimation ? 0 : 1);
            SetSlider(setupTurn, setupTurnVal, TurnTime.ToStep(s.TurnSeconds));
            ShowOnly(setupRoot, setupStart);
        }

        /// <summary>Shows the map's armies; the 戦法 button only for a COM; at least two armies must play.</summary>
        void UpdateArmyRows()
        {
            if (editing == null || armyRows.Count == 0) return;
            editing.Seated(setupPlayers);   // keeps at least two armies in the game
            for (int a = 0; a < armyRows.Count; a++)
            {
                var row = armyRows[a];
                bool shown = a < setupPlayers;
                row.tab.gameObject.SetActive(shown);
                int p = editing.Players[a];
                for (int i = 0; i < row.kind.Count; i++)
                {
                    bool on = shown && (i < 5 || setupPlayers > 2);
                    if (row.kind[i].gameObject.activeSelf != on) row.kind[i].gameObject.SetActive(on);
                }
                Select(row.kind, p == GameSettings.Nobody ? 5 : p);
                bool com = shown && p > 0;
                row.style.gameObject.SetActive(com);
                if (com) SetText(row.style.GetComponentInChildren<TextMeshProUGUI>(), StyleText(editing.Styles[a]));
            }
        }

        // ---------- small builders ----------

        RectTransform ArmyTab(Army a, float y)
        {
            var tab = Panel("Tab" + a, setupCard, ArmyColor(a));
            tab.GetComponent<Image>().raycastTarget = false;
            Pin(tab, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, y), new Vector2(180, 64), pivot: new Vector2(0, 1));
            var t = Label("Name", tab, SizeBody, Color.white, TextAlignmentOptions.Center, true);
            t.text = Labels.Army(a);
            Fill(t.rectTransform, 4, 0, 4, 0);
            return tab;
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
        List<Button> Segment(float y, float h, string[] labels, Action<int> pick, float w = 150, float step = 160)
        {
            var list = new List<Button>();
            for (int k = 0; k < labels.Length; k++)
            {
                int idx = k;
                var b = MakeButton("Seg_" + labels[k], setupCard, labels[k], false, () => { pick(idx); Select(list, idx); });
                Pin((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(240 + k * step, y), new Vector2(w, h), pivot: new Vector2(0, 1));
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
