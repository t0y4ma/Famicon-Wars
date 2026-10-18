using System;
using FamiconWars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>
    /// The matchup chart, opened over a match from the top bar: how hard each unit (rows, attacking) hits
    /// each other unit (columns, attacked) at full strength. With 「数値の表示」 on, the base damage in
    /// percent; off, a mark for each band (◎ ○ △ ▲, － cannot attack).
    /// </summary>
    public partial class GameHud
    {
        RectTransform matchupRoot, matchupGrid;
        TextMeshProUGUI matchupLegend;
        GameData matchupData;
        Func<bool> matchupNumbers;

        public bool MatchupVisible => matchupRoot != null && matchupRoot.gameObject.activeSelf;

        void BuildMatchup()
        {
            // full-screen dimmer: a click outside the chart closes it
            matchupRoot = Panel("Matchup", canvasRt, new Color(0, 0, 0, 0.6f));
            Fill(matchupRoot, 0, 0, 0, 0);
            matchupRoot.GetComponent<Image>().raycastTarget = true;
            matchupRoot.gameObject.AddComponent<Button>().onClick.AddListener(HideMatchup);

            var card = Panel("Card", matchupRoot, Plate);
            card.GetComponent<Image>().raycastTarget = true;
            card.gameObject.AddComponent<ClickSink>();
            Pin(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1210, 1000));
            Edge(card, Brass);
            var title = Label("Title", card, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "相性表";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -20), new Vector2(300, 56), pivot: new Vector2(0, 1));
            var sub = Label("Sub", card, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            sub.text = "縦: 攻撃する部隊　横: 攻撃を受ける部隊";
            Pin(sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -34), new Vector2(760, 32), pivot: new Vector2(0, 1));

            matchupGrid = new GameObject("Grid", typeof(RectTransform)).GetComponent<RectTransform>();
            matchupGrid.SetParent(card, false);
            Pin(matchupGrid, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -88), new Vector2(1110, 820), pivot: new Vector2(0, 1));

            matchupLegend = Label("Legend", card, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            Pin(matchupLegend.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 32), new Vector2(860, 40), pivot: new Vector2(0, 0));
            var close = MakeButton("Close", card, "閉じる", true, HideMatchup);
            Pin((RectTransform)close.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 24), new Vector2(200, 56), pivot: new Vector2(1, 0));
            matchupRoot.gameObject.SetActive(false);
        }

        /// <summary>Opens the chart for this game's units; <paramref name="numbers"/> is read again whenever the setting changes.</summary>
        public void ShowMatchup(GameData data, Func<bool> numbers)
        {
            if (matchupRoot == null) BuildMatchup();
            matchupData = data; matchupNumbers = numbers;
            HideActionMenu();
            RefreshMatchup();
            matchupRoot.SetAsLastSibling();
            matchupRoot.gameObject.SetActive(true);
            SettleButtons(matchupRoot);
        }

        public void HideMatchup() { if (matchupRoot != null) matchupRoot.gameObject.SetActive(false); }

        void RefreshMatchup()
        {
            if (matchupData == null) return;
            bool numbers = matchupNumbers == null || matchupNumbers();
            ClearChildren(matchupGrid);
            var units = matchupData.Units;
            int n = units.Count;
            const float head = 172, cellW = 58, cellH = 46;
            for (int c = 0; c < n; c++)
            {
                // column heads: the attacked unit's icon and short name
                HeadIcon(units[c], new Vector2(head + c * cellW + cellW / 2, -2), 34);
                var name = Label("H" + c, matchupGrid, 14, Muted, TextAlignmentOptions.Top, false);
                name.text = units[c].ShortName;
                name.overflowMode = TextOverflowModes.Overflow;
                Pin(name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(head + c * cellW, -38), new Vector2(cellW, 20), pivot: new Vector2(0, 1));
            }
            for (int r = 0; r < n; r++)
            {
                float y = -62 - r * cellH;
                var row = Panel("Row" + r, matchupGrid, r % 2 == 0 ? PlateRaised : Plate);
                row.GetComponent<Image>().raycastTarget = false;
                Pin(row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, y), new Vector2(head + n * cellW, cellH), pivot: new Vector2(0, 1));
                HeadIcon(units[r], new Vector2(22, y - 6), 34);
                var name = Label("Name", row, SizeSmall, Paper, TextAlignmentOptions.MidlineLeft, false);
                name.text = Labels.Get(units[r].NameKey);
                Pin(name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(44, 0), new Vector2(head - 48, cellH), pivot: new Vector2(0, 0.5f));
                for (int c = 0; c < n; c++)
                {
                    int dmg = matchupData.BaseDamage(units[r], units[c]);
                    var cell = Panel("C" + c, row, CellColor(dmg));
                    cell.GetComponent<Image>().raycastTarget = false;
                    Pin(cell, new Vector2(0, 0), new Vector2(0, 0), new Vector2(head + c * cellW + 2, 3), new Vector2(cellW - 4, cellH - 6), pivot: Vector2.zero);
                    var t = Label("V", cell, numbers ? SizeSmall : SizeBody, dmg >= 0 ? Paper : Muted, TextAlignmentOptions.Center, numbers && dmg >= 75);
                    t.text = dmg < 0 ? "－" : numbers ? dmg.ToString() : Mark(dmg);
                    Fill(t.rectTransform, 0, 0, 0, 0);
                }
            }
            matchupLegend.text = numbers
                ? "数字: 10機どうしで与えるダメージの目安(%)。－ は攻撃できない組み合わせ"
                : "◎ とても有利　○ 有利　△ 互角以下　▲ ほとんど効かない　－ 攻撃できない";
        }

        void HeadIcon(UnitDef d, Vector2 topCenter, float size)
        {
            var img = Panel("Icon_" + d.Id, matchupGrid, Color.white).GetComponent<Image>();
            img.raycastTarget = false; img.preserveAspect = true;
            var sp = UnitIcons.Get(d.Icon, Army.Red);
            if (sp != null) img.sprite = sp; else img.color = new Color(1, 1, 1, 0);
            Pin(img.rectTransform, new Vector2(0, 1), new Vector2(0, 1), topCenter, new Vector2(size, size), pivot: new Vector2(0.5f, 1));
        }

        static string Mark(int dmg) => dmg >= 75 ? "◎" : dmg >= 45 ? "○" : dmg >= 20 ? "△" : "▲";

        static Color CellColor(int dmg)
        {
            if (dmg < 0) return new Color(0, 0, 0, 0.18f);
            // the stronger the hit, the warmer the cell
            var cold = Hex("#33402F"); var hot = Hex("#9A5A2A");
            return Color.Lerp(cold, hot, Mathf.Clamp01(dmg / 100f));
        }
    }
}
