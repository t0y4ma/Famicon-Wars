using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>
    /// Reviewing a finished match (感想戦): a control bar to step through the moves one by one or a
    /// phase at a time, and the list of saved matches reachable from the title screen.
    /// </summary>
    public partial class GameHud
    {
        public event Action<int> OnReplayStep, OnReplayPhase, OnReplayEdge;   // -1 / +1
        public event Action OnReplayAuto, OnReplayExit, OnGameOverReplay, OnTitleRecords, OnRecordsBack;
        public event Action<int> OnRecordPick, OnRecordDelete;

        RectTransform replayBar, recordsRoot, recordsList;
        TextMeshProUGUI replayHead, replayMove;
        Button replayAutoButton;

        void BuildReplay()
        {
            // ---- control bar (bottom of the board) ----
            replayBar = Panel("ReplayBar", canvasRt, new Color(Plate.r, Plate.g, Plate.b, 0.94f));
            Pin(replayBar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(1240, 124), pivot: new Vector2(0.5f, 0));
            Edge(replayBar, Brass);
            replayHead = Label("Head", replayBar, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, true);
            Pin(replayHead.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -10), new Vector2(560, 34), pivot: new Vector2(0, 1));
            replayMove = Label("Move", replayBar, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            Pin(replayMove.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(590, -10), new Vector2(630, 34), pivot: new Vector2(0, 1));
            float x = 24;
            void Btn(string name, string text, float w, Action a)
            {
                var b = MakeButton(name, replayBar, text, false, a);
                Pin((RectTransform)b.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 18), new Vector2(w, 60), pivot: new Vector2(0, 0));
                x += w + 10;
            }
            Btn("First", "最初", 100, () => OnReplayEdge?.Invoke(-1));
            Btn("PrevPhase", "◀◀ 手番", 150, () => OnReplayPhase?.Invoke(-1));
            Btn("Prev", "◀ 1手", 130, () => OnReplayStep?.Invoke(-1));
            Btn("Next", "1手 ▶", 130, () => OnReplayStep?.Invoke(+1));
            Btn("NextPhase", "手番 ▶▶", 150, () => OnReplayPhase?.Invoke(+1));
            Btn("Last", "最後", 100, () => OnReplayEdge?.Invoke(+1));
            replayAutoButton = MakeButton("Auto", replayBar, "自動再生", true, () => OnReplayAuto?.Invoke());
            Pin((RectTransform)replayAutoButton.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 18), new Vector2(150, 60), pivot: new Vector2(0, 0));
            x += 160;
            var exit = MakeButton("Exit", replayBar, "終了", false, () => OnReplayExit?.Invoke());
            Pin((RectTransform)exit.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 18), new Vector2(120, 60), pivot: new Vector2(1, 0));
            replayBar.gameObject.SetActive(false);

            // ---- saved matches ----
            recordsRoot = Panel("Records", canvasRt, Backdrop);
            Fill(recordsRoot, 0, 0, 0, 0);
            var title = Label("Heading", recordsRoot, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "対戦の記録";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -48), new Vector2(600, 60), pivot: new Vector2(0, 1));
            var sub = Label("Sub", recordsRoot, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            sub.text = "このブラウザに保存された最近の対戦です。選ぶと一手ずつ振り返れます";
            Pin(sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -108), new Vector2(1400, 36), pivot: new Vector2(0, 1));
            recordsList = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
            recordsList.SetParent(recordsRoot, false);
            Pin(recordsList, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -170), new Vector2(1500, 760), pivot: new Vector2(0, 1));
            var back = MakeButton("Back", recordsRoot, "戻る", false, () => OnRecordsBack?.Invoke());
            Pin((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(96, 56), new Vector2(200, 64), pivot: new Vector2(0, 0));
            recordsRoot.gameObject.SetActive(false);

            // ---- entry points: the game-over panel and the title screen ----
            overPanel.sizeDelta = new Vector2(800, 300);
            var again = overPanel.Find("Again") as RectTransform;
            if (again != null) again.anchoredPosition = new Vector2(250, 36);
            var toTitle = overPanel.Find("ToTitle") as RectTransform;
            if (toTitle != null) toTitle.anchoredPosition = new Vector2(-250, 36);
            var review = MakeButton("Review", overPanel, "振り返る", false, () => OnGameOverReplay?.Invoke());
            Pin((RectTransform)review.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(220, 60), pivot: new Vector2(0.5f, 0));
            var records = MakeButton("Records", titleRoot, "対戦の記録", false, () => OnTitleRecords?.Invoke());
            Pin((RectTransform)records.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, -372), new Vector2(340, 64), pivot: new Vector2(0, 0.5f));
        }

        public void ShowReplayBar(bool on)
        {
            replayBar.gameObject.SetActive(on);
            if (on) replayBar.SetAsLastSibling();
            if (on) overPanel.gameObject.SetActive(false);
        }

        public void SetReplayInfo(string head, string move, bool auto)
        {
            replayHead.text = head;
            replayMove.text = move;
            replayAutoButton.GetComponentInChildren<TextMeshProUGUI>().text = auto ? "停止" : "自動再生";
        }

        public bool RecordsVisible => recordsRoot != null && recordsRoot.gameObject.activeSelf;

        /// <summary>The saved matches, newest first: title line and detail line for each.</summary>
        public void ShowRecords(List<(string title, string detail)> items)
        {
            ShowOnly(recordsRoot, null);
            ClearChildren(recordsList);
            if (items.Count == 0)
            {
                var none = Label("None", recordsList, SizeBody, Muted, TextAlignmentOptions.TopLeft, false);
                none.text = "まだ記録がありません。対戦が終わると自動で保存されます";
                Pin(none.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(1200, 40), pivot: new Vector2(0, 1));
                return;
            }
            Button first = null;
            for (int i = 0; i < items.Count; i++)
            {
                int k = i;
                var row = Panel("Row" + i, recordsList, Plate);
                Pin(row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -i * 74), new Vector2(1500, 66), pivot: new Vector2(0, 1));
                var t = Label("Title", row, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, true);
                t.text = items[i].title;
                Pin(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -4), new Vector2(1060, 32), pivot: new Vector2(0, 1));
                var d = Label("Detail", row, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
                d.text = items[i].detail;
                Pin(d.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -36), new Vector2(1060, 26), pivot: new Vector2(0, 1));
                var view = MakeButton("View", row, "振り返る", true, () => OnRecordPick?.Invoke(k));
                Pin((RectTransform)view.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-150, 0), new Vector2(200, 50), pivot: new Vector2(1, 0.5f));
                var del = MakeButton("Delete", row, "削除", false, () => OnRecordDelete?.Invoke(k));
                Pin((RectTransform)del.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(120, 50), pivot: new Vector2(1, 0.5f));
                if (first == null) first = view;
            }
            if (first != null) EventSystem.current?.SetSelectedGameObject(first.gameObject);
        }
    }
}
