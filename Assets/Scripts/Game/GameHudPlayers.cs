using System;
using System.Collections.Generic;
using FamiconWars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>
    /// Matches with three or more armies: a list of the armies in turn order under the top bar (who plays
    /// them, funds, income, units; the army whose phase it is stands out, knocked-out armies are greyed).
    /// Also the clock for a phase with a time limit, next to 「手番終了」.
    /// </summary>
    public partial class GameHud
    {
        RectTransform actionPanelRt, playersRoot;
        TextMeshProUGUI turnClock, overRanking;
        readonly List<(RectTransform row, Image back, Image marker, TextMeshProUGUI name, TextMeshProUGUI funds, TextMeshProUGUI detail, CanvasGroup fade)> playerRows
            = new List<(RectTransform, Image, Image, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI, CanvasGroup)>();
        const float PlayerRowH = 58;

        /// <summary>Time left in the phase ("0:42"; null hides the clock). urgent: the last seconds, in red.</summary>
        public void SetTurnTimer(string text, bool urgent)
        {
            if (turnClock == null) return;
            bool on = text != null;
            if (turnClock.gameObject.activeSelf != on)
            {
                turnClock.gameObject.SetActive(on);
                actionPanelRt.sizeDelta = new Vector2(on ? 556 : 392, actionPanelRt.sizeDelta.y);
            }
            if (!on) return;
            if (turnClock.text != text) turnClock.text = text;
            var c = urgent ? Danger : Paper;
            if (turnClock.color != c) turnClock.color = c;
        }

        void BuildPlayers()
        {
            playersRoot = Panel("Players", canvasRt, new Color(Plate.r, Plate.g, Plate.b, 0.92f));
            Pin(playersRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -92), new Vector2(430, 16), pivot: new Vector2(0, 1));
            Edge(playersRoot, PlateEdge);
            playersRoot.GetComponent<Image>().raycastTarget = false;
            for (int a = 0; a < Armies.Max; a++)
            {
                var row = new GameObject("Army" + a, typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
                row.SetParent(playersRoot, false);
                Pin(row, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -8 - a * PlayerRowH), new Vector2(-16, PlayerRowH - 4), pivot: new Vector2(0.5f, 1), stretchX: true);
                var back = Panel("Back", row, PlateRaised).GetComponent<Image>();
                back.raycastTarget = false;
                Fill((RectTransform)back.transform, 0, 0, 0, 0);
                var marker = Panel("Colour", row, ArmyColor((Army)a)).GetComponent<Image>();
                marker.raycastTarget = false;
                Pin((RectTransform)marker.transform, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(8, 0), pivot: new Vector2(0, 0.5f), stretchY: true);
                var name = Label("Name", row, SizeBody, ArmyTextColor((Army)a), TextAlignmentOptions.TopLeft, true);
                Pin(name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -2), new Vector2(250, 32), pivot: new Vector2(0, 1));
                var funds = Label("Funds", row, SizeBody, Paper, TextAlignmentOptions.TopRight, true);
                Pin(funds.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -2), new Vector2(150, 32), pivot: new Vector2(1, 1));
                var detail = Label("Detail", row, SizeSmall, Muted, TextAlignmentOptions.BottomLeft, false);
                detail.richText = true;
                Pin(detail.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2), new Vector2(-32, 26), pivot: new Vector2(0.5f, 0), stretchX: true);
                detail.rectTransform.offsetMin = new Vector2(20, detail.rectTransform.offsetMin.y);
                detail.rectTransform.offsetMax = new Vector2(-12, detail.rectTransform.offsetMax.y);
                playerRows.Add((row, back, marker, name, funds, detail, row.GetComponent<CanvasGroup>()));
            }
            playersRoot.gameObject.SetActive(false);
        }

        /// <summary>The list fades out while the mouse is over it, so the corner of the board under it can be seen.</summary>
        void UpdatePlayersFade()
        {
            if (playersRoot == null || !playersRoot.gameObject.activeSelf) return;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool over = mouse != null && RectTransformUtility.RectangleContainsScreenPoint(playersRoot, mouse.position.ReadValue(), null);
            if (playersGroup == null) playersGroup = playersRoot.gameObject.AddComponent<CanvasGroup>();
            float target = over ? 0.12f : 1f;
            playersGroup.alpha = Mathf.MoveTowards(playersGroup.alpha, target, Time.unscaledDeltaTime * 6f);
        }
        CanvasGroup playersGroup;

        /// <summary>Shows the list for three or more armies (hidden for two: the top bar says it all).</summary>
        public void SetPlayers(GameState s, Func<Army, string> controller, Func<Army, int> income)
        {
            if (playersRoot == null) BuildPlayers();
            bool show = s != null && s.Players > 2 && topBar.gameObject.activeInHierarchy;
            if (playersRoot.gameObject.activeSelf != show) playersRoot.gameObject.SetActive(show);
            if (!show) return;
            int shown = 0;
            for (int a = 0; a < playerRows.Count; a++)
            {
                var r = playerRows[a];
                var army = (Army)a;
                // an army without a player from the start is left off the list
                bool inGame = a < s.Players && (!s.Out[a] || s.Eliminated.Contains(army));
                r.row.gameObject.SetActive(inGame);
                if (!inGame) continue;
                r.row.anchoredPosition = new Vector2(r.row.anchoredPosition.x, -8 - shown * PlayerRowH);
                shown++;
                bool active = s.Active == army && !s.GameOver;
                bool outNow = s.Out[a];
                r.back.color = active ? Hex("#3E4B3A") : new Color(0, 0, 0, 0);
                r.marker.rectTransform.sizeDelta = new Vector2(active ? 14 : 8, 0);
                SetText(r.name, Labels.Army(army) + (active ? "<size=70%><color=#E4B04A>　手番</color></size>" : ""));
                SetText(r.funds, outNow ? "" : s.Funds[a].ToString("N0"));
                int units = RulesEngine.UnitCount(s, army);
                SetText(r.detail, outNow
                    ? "脱落　<color=#A3AC92>" + controller(army) + "</color>"
                    : controller(army) + "　収入 +" + income(army).ToString("N0") + "　部隊 " + units);
                r.fade.alpha = outNow ? 0.45f : 1f;
            }
            playersRoot.sizeDelta = new Vector2(playersRoot.sizeDelta.x, 16 + shown * PlayerRowH);
        }
    }
}
