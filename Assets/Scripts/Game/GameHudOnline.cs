using System;
using System.Collections.Generic;
using FamiconWars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>What the room screen shows (filled from the server's room status).</summary>
    public sealed class RoomView
    {
        public string Code, MapId;
        public bool Started, GameOver, IAmHost, IsPublic;
        /// <summary>Seats, map and BOTs can change: before a match or once it is over.</summary>
        public bool Editable => !Started || GameOver;
        public int MySeat;                        // 0 red, 1 blue, -1 watching
        public SeatKind[] SeatKinds = new SeatKind[2];
        public string[] SeatNames = new string[2];
        public int[] SeatLevels = new int[2];
        public int[] SeatStyles = new int[2];     // BOT 戦法: 0 random, 1-3
        public bool[] SeatOnline = new bool[2];
        public readonly List<(int id, string name, int seat, bool online, bool host, bool me)> Members = new List<(int, string, int, bool, bool, bool)>();
    }

    /// <summary>One public room in the lobby list. State: 0 waiting, 1 playing, 2 match over.</summary>
    public struct RoomListEntry
    {
        public string Code, MapName, Host;
        public int State, Members, SeatsTaken;
    }

    /// <summary>
    /// Online lobby (server, name, create / join, the list of public rooms) and the room screen: two seats and the members
    /// watching. The host picks the map, puts BOTs in empty seats, can hand the host role over and
    /// starts the match. Everyone can take a free seat or stand up to watch.
    /// </summary>
    public partial class GameHud
    {
        public event Action<string> OnOnlineCreate;          // address
        public event Action<string, string> OnOnlineJoin;    // address, code
        public event Action OnOnlineBack, OnOnlineRefresh, OnSpectatorBack, OnRoomWatch;
        public event Action<bool> OnRoomPublic;
        public event Action<int> OnRoomSit, OnRoomHost, OnRoomMap;   // seat / member id / map step (-1, +1)
        public event Action<int, int> OnRoomBot;             // seat, level 0-4
        public event Action<int, int> OnRoomBotStyle;        // seat, 戦法 0 random / 1-3
        public event Action OnRoomStand, OnRoomStart, OnRoomLeave;

        RectTransform onlineRoot, onlineForm, roomRoot, roomBody;
        TMP_InputField addressInput, codeInput, nameInput;
        TextMeshProUGUI onlineStatus, roomStatus;
        Button onlineCreate, onlineJoin, createPublicBtn, createPrivateBtn, roomPublicBtn;
        bool createPublic = true;
        RectTransform listPanel, listRows;
        TextMeshProUGUI listMessage;
        Func<string, Texture2D> roomPreview;
        RoomView lastRoomView;
        int botMenuSeat = -1;                       // seat whose BOT strength list is open (-1 = none)

        void BuildOnline()
        {
            onlineRoot = Panel("OnlineLobby", canvasRt, Backdrop);
            Fill(onlineRoot, 0, 0, 0, 0);

            var title = Label("Heading", onlineRoot, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "オンライン対戦";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -48), new Vector2(600, 60), pivot: new Vector2(0, 1));
            var sub = Label("Sub", onlineRoot, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            sub.text = "部屋を作って番号を相手に伝えるか、もらった番号で部屋に入ります(対戦中の部屋に入ると観戦になります)";
            Pin(sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -108), new Vector2(1500, 36), pivot: new Vector2(0, 1));

            // ---- form ----
            onlineForm = Panel("Form", onlineRoot, Plate);
            Pin(onlineForm, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(96, -10), new Vector2(880, 620), pivot: new Vector2(0, 0.5f));
            Edge(onlineForm, Brass);

            FormLabel("サーバー", -40);
            addressInput = MakeInput("Address", onlineForm, "サーバーのアドレス", TMP_InputField.ContentType.Standard);
            Pin((RectTransform)addressInput.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -40), new Vector2(620, 56), pivot: new Vector2(0, 1));

            FormLabel("名前", -112);
            nameInput = MakeInput("Name", onlineForm, "部屋で表示される名前", TMP_InputField.ContentType.Standard);
            nameInput.characterLimit = 12;
            Pin((RectTransform)nameInput.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -112), new Vector2(620, 56), pivot: new Vector2(0, 1));

            var create = MakeButton("Create", onlineForm, "部屋を作る", true, () => OnOnlineCreate?.Invoke(addressInput.text));
            onlineCreate = create;
            Pin((RectTransform)create.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -196), new Vector2(620, 64), pivot: new Vector2(0, 1));
            // public (listed in the lobby) or private (number only)
            createPublicBtn = MakeButton("Public", onlineForm, "公開", true, () => SetCreatePublic(true));
            Pin((RectTransform)createPublicBtn.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -270), new Vector2(130, 40), pivot: new Vector2(0, 1));
            createPrivateBtn = MakeButton("Private", onlineForm, "非公開", false, () => SetCreatePublic(false));
            Pin((RectTransform)createPrivateBtn.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(358, -270), new Vector2(130, 40), pivot: new Vector2(0, 1));
            var createNote = Label("CreateNote", onlineForm, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            createNote.text = "公開: 右の一覧に出ます／非公開: 番号を知る人だけ";
            createNote.textWrappingMode = TextWrappingModes.Normal;
            Pin(createNote.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(504, -266), new Vector2(340, 48), pivot: new Vector2(0, 1));

            var divider = Panel("Divider", onlineForm, PlateEdge);
            Pin(divider, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -318), new Vector2(-80, 2), pivot: new Vector2(0.5f, 1), stretchX: true);

            FormLabel("部屋番号", -356);
            codeInput = MakeInput("Code", onlineForm, "4桁の番号", TMP_InputField.ContentType.IntegerNumber);
            codeInput.characterLimit = 4;
            Pin((RectTransform)codeInput.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -356), new Vector2(260, 56), pivot: new Vector2(0, 1));
            onlineJoin = MakeButton("Join", onlineForm, "部屋に入る", false, () => OnOnlineJoin?.Invoke(addressInput.text, codeInput.text));
            Pin((RectTransform)onlineJoin.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(500, -356), new Vector2(340, 56), pivot: new Vector2(0, 1));
            var joinNote = Label("JoinNote", onlineForm, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            joinNote.text = "切断しても、同じブラウザで同じ番号に入り直せば続きから遊べます";
            joinNote.textWrappingMode = TextWrappingModes.Normal;
            Pin(joinNote.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -422), new Vector2(620, 30), pivot: new Vector2(0, 1));

            onlineStatus = Label("Status", onlineForm, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, false);
            onlineStatus.textWrappingMode = TextWrappingModes.Normal;
            Pin(onlineStatus.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 36), new Vector2(-80, 70), pivot: new Vector2(0.5f, 0), stretchX: true);

            BuildRoomList();

            var back = MakeButton("Back", onlineRoot, "戻る", false, () => OnOnlineBack?.Invoke());
            Pin((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(96, 56), new Vector2(200, 64), pivot: new Vector2(0, 0));
            var sound = MakeButton("Sound", onlineRoot, "サウンド", false, () => OnSoundSettings?.Invoke());
            Pin((RectTransform)sound.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(320, 56), new Vector2(220, 64), pivot: new Vector2(0, 0));

            // ---- room screen (its own full-screen page; built once, updated on every status) ----
            roomRoot = Panel("Room", canvasRt, Backdrop);
            Fill(roomRoot, 0, 0, 0, 0);
            roomBody = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            roomBody.SetParent(roomRoot, false);
            Fill(roomBody, 0, 0, 0, 0);
            BuildRoomPage();
            roomStatus = Label("Status", roomRoot, SizeBody, Danger, TextAlignmentOptions.Center, false);
            Pin(roomStatus.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 132), new Vector2(0, 36), pivot: new Vector2(0.5f, 0), stretchX: true);

            onlineRoot.gameObject.SetActive(false);
            roomRoot.gameObject.SetActive(false);
        }

        // ---- spectators: leave the board for the room screen (the room is kept) ----
        Button spectatorBackBtn;

        public void SetSpectatorBack(bool visible)
        {
            if (spectatorBackBtn == null)
            {
                if (!visible) return;
                spectatorBackBtn = MakeButton("SpectatorBack", canvasRt, "部屋に戻る", false, () => OnSpectatorBack?.Invoke());
                Pin((RectTransform)spectatorBackBtn.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-336, -92), new Vector2(260, 52), pivot: new Vector2(1, 1));
            }
            if (visible && !spectatorBackBtn.gameObject.activeSelf) spectatorBackBtn.transform.SetAsLastSibling();
            SetActive(spectatorBackBtn, visible);
        }

        // ---- host: end the current match (under the top bar, left of the zoom readout) ----
        public event Action OnAbortMatch;
        Button abortBtn;
        float abortArmedUntil = -1;

        /// <summary>Shown to the host during an online match. The first click asks, the second (within 4 s) ends it.</summary>
        public void SetAbortButton(bool visible)
        {
            if (abortBtn == null)
            {
                if (!visible) return;
                abortBtn = MakeButton("AbortMatch", canvasRt, "対局を終了", false, () =>
                {
                    if (Time.unscaledTime <= abortArmedUntil) { abortArmedUntil = -1; SetText(abortBtn.GetComponentInChildren<TextMeshProUGUI>(), "対局を終了"); OnAbortMatch?.Invoke(); return; }
                    abortArmedUntil = Time.unscaledTime + 4f;
                    SetText(abortBtn.GetComponentInChildren<TextMeshProUGUI>(), "<color=#F07A62>もう一度押すと終了</color>");
                });
                Pin((RectTransform)abortBtn.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-336, -92), new Vector2(260, 52), pivot: new Vector2(1, 1));
            }
            if (abortArmedUntil >= 0 && Time.unscaledTime > abortArmedUntil)
            {
                abortArmedUntil = -1;
                SetText(abortBtn.GetComponentInChildren<TextMeshProUGUI>(), "対局を終了");
            }
            if (visible && !abortBtn.gameObject.activeSelf) abortBtn.transform.SetAsLastSibling();
            SetActive(abortBtn, visible);
        }

        // ---- public rooms (right side of the lobby) ----
        const int ListRows = 8;

        void BuildRoomList()
        {
            listPanel = Panel("RoomList", onlineRoot, Plate);
            Pin(listPanel, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-96, -10), new Vector2(800, 620), pivot: new Vector2(1, 0.5f));
            Edge(listPanel, PlateEdge);
            var title = Label("Title", listPanel, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "公開されている部屋";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -20), new Vector2(500, 44), pivot: new Vector2(0, 1));
            var refresh = MakeButton("Refresh", listPanel, "更新", false, () => OnOnlineRefresh?.Invoke());
            Pin((RectTransform)refresh.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -18), new Vector2(120, 46), pivot: new Vector2(1, 1));
            listRows = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
            listRows.SetParent(listPanel, false);
            Pin(listRows, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -80), new Vector2(-48, ListRows * 66), pivot: new Vector2(0.5f, 1), stretchX: true);
            listMessage = Label("Message", listPanel, SizeBody, Muted, TextAlignmentOptions.Center, false);
            listMessage.textWrappingMode = TextWrappingModes.Normal;
            Pin(listMessage.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -20), new Vector2(-80, 120), pivot: new Vector2(0.5f, 0.5f), stretchX: true);
            listMessage.text = "サーバーに接続しています…";
        }

        void SetCreatePublic(bool on)
        {
            createPublic = on;
            StyleButton(createPublicBtn, on);
            StyleButton(createPrivateBtn, !on);
        }

        /// <summary>Whether a room made from the lobby is listed.</summary>
        public bool CreatePublic => createPublic;

        /// <summary>The server address typed in the lobby form.</summary>
        public string OnlineAddress => addressInput != null ? addressInput.text : "";

        /// <summary>Shows a message in place of the list (not connected, loading, ...).</summary>
        public void RoomListMessage(string text)
        {
            if (listRows == null) return;
            ClearChildren(listRows);
            listMessage.text = text ?? "";
        }

        static readonly string[] ListStates = { "<color=#9FD18B>待機中</color>", "<color=#FF8A7E>対戦中</color>", "<color=#D8A23A>対戦終了</color>" };

        /// <summary>The public rooms: waiting ones can be joined, playing ones watched.</summary>
        public void SetRoomList(List<RoomListEntry> rooms)
        {
            if (listRows == null) return;
            ClearChildren(listRows);
            listMessage.text = rooms.Count == 0 ? "公開されている部屋はまだありません。\n部屋を作ると、ここに表示されます" : "";
            for (int i = 0; i < rooms.Count && i < ListRows; i++)
            {
                var e = rooms[i];
                var row = Panel("Row" + i, listRows, new Color(0.09f, 0.11f, 0.085f, 1f));
                Pin(row, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -i * 66), new Vector2(0, 58), pivot: new Vector2(0.5f, 1), stretchX: true);
                row.GetComponent<Image>().raycastTarget = false;
                var code = Label("Code", row, SizeBody, Brass, TextAlignmentOptions.MidlineLeft, true);
                code.text = e.Code; code.characterSpacing = 6;
                Pin(code.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(100, 50), pivot: new Vector2(0, 0.5f));
                var name = Label("Map", row, SizeSmall, Paper, TextAlignmentOptions.TopLeft, true);
                name.text = e.MapName;
                Pin(name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(124, -5), new Vector2(480, 26), pivot: new Vector2(0, 1));
                var info = Label("Info", row, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
                info.richText = true;
                info.text = ListStates[Mathf.Clamp(e.State, 0, 2)] + "　席 " + e.SeatsTaken + "/2　" + e.Members + "人　部屋主 " + e.Host;
                Pin(info.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(124, -30), new Vector2(480, 26), pivot: new Vector2(0, 1));
                string c = e.Code;
                bool watch = e.State == 1;
                var go = MakeButton("Go", row, watch ? "観戦" : "入る", !watch, () => OnOnlineJoin?.Invoke(addressInput.text, c));
                Pin((RectTransform)go.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(130, 46), pivot: new Vector2(1, 0.5f));
            }
            SettleButtons(listRows);
        }

        void FormLabel(string text, float y)
        {
            var l = Label("L_" + text, onlineForm, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            l.text = text;
            Pin(l.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, y), new Vector2(160, 56), pivot: new Vector2(0, 1));
        }

        TMP_InputField MakeInput(string name, Transform parent, string placeholder, TMP_InputField.ContentType type)
        {
            var rt = Panel(name, parent, PlateRaised);
            rt.GetComponent<Image>().raycastTarget = true;
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var art = (RectTransform)area.transform; art.SetParent(rt, false);
            Fill(art, 16, 6, 16, 6);
            var ph = Label("Placeholder", art, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            ph.text = placeholder; Fill(ph.rectTransform, 0, 0, 0, 0);
            var tx = Label("Text", art, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, false);
            Fill(tx.rectTransform, 0, 0, 0, 0);
            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = art; input.textComponent = tx; input.placeholder = ph;
            input.contentType = type;
            if (font != null) input.fontAsset = font;
            input.pointSize = SizeBody;
            input.caretColor = Brass; input.customCaretColor = true; input.caretWidth = 3;
            input.selectionColor = new Color(Brass.r, Brass.g, Brass.b, 0.35f);
            var edge = Panel("Underline", rt, Brass); edge.GetComponent<Image>().raycastTarget = false;
            Pin(edge, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 2), pivot: new Vector2(0.5f, 0), stretchX: true);
            return input;
        }

        public void HideOnline()
        {
            if (onlineRoot != null) onlineRoot.gameObject.SetActive(false);
            if (roomRoot != null) roomRoot.gameObject.SetActive(false);
        }

        /// <summary>The lobby form. Fields keep what the player typed unless a value is given.</summary>
        public void ShowOnline(string address, string code, string status, bool error, string playerName = null)
        {
            roomRoot.gameObject.SetActive(false);
            ShowOnly(onlineRoot, onlineCreate);
            onlineForm.gameObject.SetActive(true);
            if (address != null && string.IsNullOrEmpty(addressInput.text)) addressInput.text = address;
            if (code != null) codeInput.text = code;
            if (playerName != null && string.IsNullOrEmpty(nameInput.text)) nameInput.text = playerName;
            OnlineStatus(status, error);
            SetOnlineBusy(false);
        }

        /// <summary>Replaces the name in the lobby form (debug mode switches to a throw-away name).</summary>
        public void SetOnlineName(string name) { if (nameInput != null) nameInput.text = name ?? ""; }

        /// <summary>The name typed in the lobby form.</summary>
        public string OnlinePlayerName => nameInput != null ? nameInput.text.Trim() : "";

        public void OnlineStatus(string text, bool error)
        {
            onlineStatus.text = text ?? "";
            onlineStatus.color = error ? Danger : Paper;
        }

        /// <summary>While connecting: the buttons are locked so a double click cannot make two rooms.</summary>
        public void SetOnlineBusy(bool busy)
        {
            onlineCreate.interactable = !busy;
            onlineJoin.interactable = !busy;
        }

        public bool OnlineLobbyVisible => onlineRoot != null && onlineRoot.gameObject.activeSelf;
        public bool RoomVisible => roomRoot != null && roomRoot.gameObject.activeSelf;

        public void RoomError(string text) { if (roomStatus != null) roomStatus.text = text ?? ""; }

        static readonly string[] BotLabels = { "なし", "弱い", "普通", "強い", "最強" };

        // ---- room page parts: built once, then only updated (no rebuilding, so nothing flickers) ----
        TextMeshProUGUI roomCodeLabel, roomInvite, roomMapName, roomMapNote, roomMembersTitle, roomHint;
        RawImage roomMapPreview;
        Button roomMapPrev, roomMapNext, roomLeave, roomStand, roomStart, roomWatch;
        RectTransform botBlocker, botStrip;
        readonly Button[] botLevelButtons = new Button[5];
        readonly Button[] botStyleButtons = new Button[4];
        static readonly string[] StyleLabels = { "ランダム", "速攻", "物量", "精鋭" };
        bool roomStartReady;
        sealed class SeatParts
        {
            public Image Icon; public TextMeshProUGUI Who, State; public Button Sit, Bot;
            public Vector2 CardPos;
        }
        readonly SeatParts[] seatParts = new SeatParts[2];
        const int MemberRows = 7;
        readonly TextMeshProUGUI[] memberRowLabels = new TextMeshProUGUI[MemberRows];
        readonly Button[] memberRowHost = new Button[MemberRows];
        readonly int[] memberRowIds = new int[MemberRows];

        void BuildRoomPage()
        {
            // header: room number and how to invite
            var title = Label("Heading", roomBody, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "部屋";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -40), new Vector2(120, 60), pivot: new Vector2(0, 1));
            roomCodeLabel = Label("Code", roomBody, 64, Brass, TextAlignmentOptions.MidlineLeft, true);
            roomCodeLabel.characterSpacing = 12; roomCodeLabel.overflowMode = TextOverflowModes.Overflow;
            Pin(roomCodeLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(210, -28), new Vector2(320, 84), pivot: new Vector2(0, 1));
            roomInvite = Label("Invite", roomBody, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            Pin(roomInvite.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(520, -48), new Vector2(860, 44), pivot: new Vector2(0, 1));
            roomPublicBtn = MakeButton("Visibility", roomBody, "公開", false, () => OnRoomPublic?.Invoke(lastRoomView == null || !lastRoomView.IsPublic));
            Pin((RectTransform)roomPublicBtn.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-96, -44), new Vector2(400, 52), pivot: new Vector2(1, 1));

            // map
            var mapPlate = Panel("Map", roomBody, Plate);
            Pin(mapPlate, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-96, -150), new Vector2(560, 300), pivot: new Vector2(1, 1));
            Edge(mapPlate, PlateEdge);
            var mapLabel = Label("Label", mapPlate, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            mapLabel.text = "マップ";
            Pin(mapLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -18), new Vector2(200, 28), pivot: new Vector2(0, 1));
            roomMapName = Label("Name", mapPlate, SizeTitle - 6, Paper, TextAlignmentOptions.TopLeft, true);
            Pin(roomMapName.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -46), new Vector2(-48, 44), pivot: new Vector2(0.5f, 1), stretchX: true);
            roomMapPreview = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            roomMapPreview.transform.SetParent(mapPlate, false);
            roomMapPreview.raycastTarget = false;
            Pin(roomMapPreview.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(300, 180), pivot: new Vector2(0, 0));
            roomMapNote = Label("Note", mapPlate, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            roomMapNote.textWrappingMode = TextWrappingModes.Normal;
            Pin(roomMapNote.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(340, 24), new Vector2(196, 180), pivot: new Vector2(0, 0));
            roomMapPrev = MakeButton("Prev", mapPlate, "◀", false, () => OnRoomMap?.Invoke(-1));
            Pin((RectTransform)roomMapPrev.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-92, -14), new Vector2(60, 46), pivot: new Vector2(1, 1));
            roomMapNext = MakeButton("Next", mapPlate, "▶", false, () => OnRoomMap?.Invoke(+1));
            Pin((RectTransform)roomMapNext.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -14), new Vector2(60, 46), pivot: new Vector2(1, 1));

            // seats
            for (int s = 0; s < 2; s++) seatParts[s] = BuildSeatCard(s, new Vector2(96 + s * 500, -150));

            // members
            var list = Panel("Members", roomBody, Plate);
            Pin(list, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -480), new Vector2(980, 380), pivot: new Vector2(0, 1));
            Edge(list, PlateEdge);
            roomMembersTitle = Label("Title", list, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            Pin(roomMembersTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -16), new Vector2(-48, 28), pivot: new Vector2(0.5f, 1), stretchX: true);
            for (int k = 0; k < MemberRows; k++)
            {
                float y = -52 - 46 * k;
                int row = k;
                memberRowLabels[k] = Label("Row" + k, list, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, false);
                Pin(memberRowLabels[k].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, y), new Vector2(700, 40), pivot: new Vector2(0, 1));
                memberRowHost[k] = MakeButton("Host" + k, list, "部屋主にする", false, () => OnRoomHost?.Invoke(memberRowIds[row]));
                Pin((RectTransform)memberRowHost[k].transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, y), new Vector2(200, 40), pivot: new Vector2(1, 1));
            }

            // footer
            roomLeave = MakeButton("Leave", roomBody, "部屋を出る", false, () => OnRoomLeave?.Invoke());
            Pin((RectTransform)roomLeave.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(96, 56), new Vector2(240, 64), pivot: new Vector2(0, 0));
            roomStand = MakeButton("Stand", roomBody, "観戦に回る", false, () => OnRoomStand?.Invoke());
            Pin((RectTransform)roomStand.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(360, 56), new Vector2(240, 64), pivot: new Vector2(0, 0));
            roomHint = Label("Hint", roomBody, SizeBody, Muted, TextAlignmentOptions.MidlineRight, false);
            Pin(roomHint.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-420, 66), new Vector2(760, 44), pivot: new Vector2(1, 0));
            roomStart = MakeButton("Start", roomBody, "対戦開始", true, () => OnRoomStart?.Invoke());
            Pin((RectTransform)roomStart.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-96, 56), new Vector2(300, 64), pivot: new Vector2(1, 0));
            // during a match, from the room screen back to the board
            roomWatch = MakeButton("Watch", roomBody, "観戦に戻る", true, () => OnRoomWatch?.Invoke());
            Pin((RectTransform)roomWatch.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-96, 56), new Vector2(300, 64), pivot: new Vector2(1, 0));

            // BOT strength list: a click outside closes it (last, so it is drawn over the page)
            var blocker = MakeButton("BotMenuBlocker", roomBody, "", false, () => SetBotMenu(-1));
            blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            botBlocker = (RectTransform)blocker.transform;
            Fill(botBlocker, 0, 0, 0, 0);
            const float w = 112, gap = 8;
            botStrip = Panel("BotMenu", roomBody, PlateRaised);
            Edge(botStrip, Brass);
            float stripW = BotLabels.Length * (w + gap) + gap;
            Pin(botStrip, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(stripW, 136), pivot: new Vector2(0, 1));
            for (int i = 0; i < BotLabels.Length; i++)
            {
                int lv = i;
                botLevelButtons[i] = MakeButton("Lv" + i, botStrip, BotLabels[i], false, () => PickBot(botMenuSeat, lv));
                Pin((RectTransform)botLevelButtons[i].transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(gap + i * (w + gap), -gap), new Vector2(w, 56), pivot: new Vector2(0, 1));
            }
            // second row: the BOT's 戦法 (random ones are not shown during the match)
            float sw = (stripW - gap) / StyleLabels.Length - gap;
            for (int i = 0; i < StyleLabels.Length; i++)
            {
                int st = i;
                botStyleButtons[i] = MakeButton("Style" + i, botStrip, StyleLabels[i], false, () => PickBotStyle(botMenuSeat, st));
                Pin((RectTransform)botStyleButtons[i].transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(gap + i * (sw + gap), -gap - 56 - gap), new Vector2(sw, 56), pivot: new Vector2(0, 1));
            }
            botBlocker.gameObject.SetActive(false);
            botStrip.gameObject.SetActive(false);
        }

        SeatParts BuildSeatCard(int s, Vector2 pos)
        {
            var p = new SeatParts { CardPos = pos };
            var army = (Army)s;
            var card = Panel("Seat" + s, roomBody, PlateRaised);
            Pin(card, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(480, 300), pivot: new Vector2(0, 1));
            var tab = Panel("Tab", card, ArmyColor(army));
            tab.GetComponent<Image>().raycastTarget = false;
            Pin(tab, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 8), pivot: new Vector2(0.5f, 1), stretchX: true);
            var head = Label("Army", card, SizeBody, ArmyTextColor(army), TextAlignmentOptions.TopLeft, true);
            head.text = Labels.Army(army) + (s == 0 ? "(先手)" : "(後手)");
            Pin(head.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -24), new Vector2(-48, 34), pivot: new Vector2(0.5f, 1), stretchX: true);
            p.Icon = Panel("Icon", card, Color.white).GetComponent<Image>();
            p.Icon.raycastTarget = false; p.Icon.preserveAspect = true;
            Pin(p.Icon.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(72, -118), new Vector2(96, 96), pivot: new Vector2(0.5f, 0.5f));
            if (s == 1) p.Icon.rectTransform.localScale = new Vector3(-1, 1, 1);
            p.Who = Label("Who", card, SizeTitle - 4, Paper, TextAlignmentOptions.MidlineLeft, true);
            Pin(p.Who.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(136, -78), new Vector2(320, 48), pivot: new Vector2(0, 1));
            p.State = Label("State", card, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            Pin(p.State.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(136, -128), new Vector2(320, 30), pivot: new Vector2(0, 1));
            p.Sit = MakeButton("Sit", card, "ここに座る", true, () => OnRoomSit?.Invoke(s));
            Pin((RectTransform)p.Sit.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(200, 56), pivot: new Vector2(0, 0));
            p.Bot = MakeButton("Bot", card, "BOT", false, () => SetBotMenu(botMenuSeat == s ? -1 : s));
            Pin((RectTransform)p.Bot.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(240, 24), new Vector2(216, 56), pivot: new Vector2(0, 0));
            return p;
        }

        static void SetActive(Component c, bool on) { if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }

        static void SetText(TextMeshProUGUI t, string text) { if (t.text != text) t.text = text; }

        /// <summary>Shows (or refreshes) the room screen: only texts, visibility and positions change.</summary>
        public void ShowRoom(RoomView v, Func<string, Texture2D> preview)
        {
            roomPreview = preview;
            lastRoomView = v;
            if (botMenuSeat >= 0 && (!v.IAmHost || !v.Editable || v.SeatKinds[botMenuSeat] == SeatKind.Human)) botMenuSeat = -1;
            onlineRoot.gameObject.SetActive(false);
            bool first = !RoomVisible;
            if (first) ShowOnly(roomRoot, null);
            roomRoot.SetAsLastSibling();
            roomStatus.text = "";

            SetText(roomCodeLabel, v.Code);
            SetText(roomInvite, v.Started ? (v.GameOver ? "対戦は終わりました。席とマップを決め直して次の対戦ができます" : "対戦中です。この番号で入ると観戦できます") : "この番号を伝えると、相手や観戦者が入れます");
            SetText(roomPublicBtn.GetComponentInChildren<TextMeshProUGUI>(), (v.IsPublic ? "公開中(一覧に表示)" : "非公開(番号を知る人だけ)") + (v.IAmHost ? "  切替" : ""));
            if (roomPublicBtn.interactable != v.IAmHost) roomPublicBtn.interactable = v.IAmHost;

            // map
            var entry = MapCatalog.Find(v.MapId);
            SetText(roomMapName, entry != null ? entry.Name : v.MapId);
            var tex = preview?.Invoke(v.MapId);
            SetActive(roomMapPreview, tex != null);
            if (tex != null && roomMapPreview.texture != tex)
            {
                roomMapPreview.texture = tex;
                float aspect = (float)tex.width / tex.height;
                roomMapPreview.rectTransform.sizeDelta = aspect >= 300f / 180f ? new Vector2(300, 300 / aspect) : new Vector2(180 * aspect, 180);
            }
            SetText(roomMapNote, entry != null ? entry.Width + " × " + entry.Height + "\n" + entry.Note : "");
            SetActive(roomMapPrev, v.IAmHost && v.Editable);
            SetActive(roomMapNext, v.IAmHost && v.Editable);

            for (int s = 0; s < 2; s++) UpdateSeat(v, s);

            // members
            SetText(roomMembersTitle, "部屋にいる人(" + v.Members.Count + ")   ★ = 部屋主");
            for (int k = 0; k < MemberRows; k++)
            {
                bool has = k < v.Members.Count;
                SetActive(memberRowLabels[k], has);
                SetActive(memberRowHost[k], has && v.IAmHost && !v.Members[k].me && v.Members[k].online);
                if (!has) continue;
                var m = v.Members[k];
                memberRowIds[k] = m.id;
                string role = m.seat == 0 ? "<color=#FF8A7E>レッド軍</color>" : m.seat == 1 ? "<color=#8FB2FF>ブルー軍</color>" : "観戦";
                SetText(memberRowLabels[k], (m.host ? "<color=#D8A23A>★</color> " : "　 ") + m.name + (m.me ? "<color=#A3AC92>(あなた)</color>" : "") + "　" + role + (m.online ? "" : "<color=#A3AC92>(接続待ち)</color>"));
                memberRowLabels[k].color = m.online ? Paper : Muted;
            }

            // footer
            SetActive(roomStand, v.Editable && v.MySeat >= 0);
            bool ready = v.SeatKinds[0] != SeatKind.Empty && v.SeatKinds[1] != SeatKind.Empty;
            bool canStart = v.IAmHost && v.Editable;
            SetActive(roomStart, canStart);
            if (canStart)
            {
                SetText(roomStart.GetComponentInChildren<TextMeshProUGUI>(), v.GameOver ? "次の対戦を開始" : "対戦開始");
                bool on = ready;
                if (roomStart.interactable != on) roomStart.interactable = on;
                // focus the button when it becomes usable (not on every refresh: that would steal the focus)
                if (on && (first || !roomStartReady)) SelectInstant(roomStart);
                roomStartReady = on;
            }
            else roomStartReady = false;
            bool watch = v.Started && !v.GameOver;
            SetActive(roomWatch, watch);
            if (watch) SetText(roomWatch.GetComponentInChildren<TextMeshProUGUI>(), v.MySeat >= 0 ? "対戦に戻る" : "観戦に戻る");
            SetText(roomHint, canStart ? (ready ? "" : "両方の席が埋まると開始できます(空席には BOT も置けます)")
                : v.Editable ? "部屋主が対戦を始めるのを待っています" : "");

            UpdateBotMenu(v);
            if (first) SettleButtons(roomBody);
        }

        void UpdateSeat(RoomView v, int s)
        {
            var p = seatParts[s];
            var army = (Army)s;
            var kind = v.SeatKinds[s];
            p.Icon.sprite = UnitIcons.Get(kind == SeatKind.Bot ? "TANK_A" : "INF", army);
            p.Icon.color = new Color(1, 1, 1, p.Icon.sprite == null || kind == SeatKind.Empty ? 0.18f : 1f);
            SetText(p.Who, kind == SeatKind.Empty ? "空席" : v.SeatNames[s]);
            p.Who.color = kind == SeatKind.Empty ? Muted : Paper;
            SetText(p.State, kind == SeatKind.Bot ? "戦法: " + StyleLabels[Math.Max(0, Math.Min(3, v.SeatStyles[s]))]
                : kind == SeatKind.Human ? (v.MySeat == s ? "あなたの席です" : v.SeatOnline[s] ? "準備できています" : "接続が切れています")
                : "誰でも座れます");
            bool sit = v.Editable && kind == SeatKind.Empty && v.MySeat != s;
            bool bot = v.Editable && v.IAmHost && kind != SeatKind.Human;
            SetActive(p.Sit, sit);
            SetActive(p.Bot, bot);
            if (bot)
            {
                float bx = sit ? 240 : 24;
                var brt = (RectTransform)p.Bot.transform;
                if (brt.anchoredPosition.x != bx) brt.anchoredPosition = new Vector2(bx, 24);
                int level = kind == SeatKind.Bot ? v.SeatLevels[s] : 0;
                SetText(p.Bot.GetComponentInChildren<TextMeshProUGUI>(), "BOT: " + BotLabels[level] + "  ▼");
            }
        }

        /// <summary>Opens the BOT strength list under a seat's BOT button (-1 closes it).</summary>
        void SetBotMenu(int seat)
        {
            botMenuSeat = seat;
            if (lastRoomView != null) UpdateBotMenu(lastRoomView);
        }

        void UpdateBotMenu(RoomView v)
        {
            int s = botMenuSeat;
            bool open = s >= 0;
            if (open && (!v.IAmHost || !v.Editable || v.SeatKinds[s] == SeatKind.Human)) { botMenuSeat = -1; open = false; }
            if (!open)
            {
                if (botStrip.gameObject.activeSelf && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                    && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(botStrip)) EventSystem.current.SetSelectedGameObject(null);
                SetActive(botBlocker, false);
                SetActive(botStrip, false);
                return;
            }
            int level = v.SeatKinds[s] == SeatKind.Bot ? v.SeatLevels[s] : 0;
            bool wasOpen = botStrip.gameObject.activeSelf;
            for (int i = 0; i < botLevelButtons.Length; i++) StyleButton(botLevelButtons[i], i == level);
            int style = v.SeatStyles[s];
            for (int i = 0; i < botStyleButtons.Length; i++)
            {
                StyleButton(botStyleButtons[i], level > 0 && i == style);
                if (botStyleButtons[i].interactable != level > 0) botStyleButtons[i].interactable = level > 0;
            }
            var bot = (RectTransform)seatParts[s].Bot.transform;
            // top-left of the BOT button in page coordinates (card 480 x 300 pinned top-left at CardPos)
            botStrip.anchoredPosition = seatParts[s].CardPos + new Vector2(bot.anchoredPosition.x, -300 + 24 + 56) + new Vector2(-8, -8);
            SetActive(botBlocker, true);
            SetActive(botStrip, true);
            if (!wasOpen) SelectInstant(botLevelButtons[level]);
            SettleButtons(botStrip);
        }

        /// <summary>Closes the list and shows the choice at once; the server's status confirms it.</summary>
        void PickBot(int s, int level)
        {
            if (s < 0) return;
            if (level == 0) botMenuSeat = -1;     // with a BOT, the list stays open for its 戦法
            if (lastRoomView != null && lastRoomView.SeatKinds[s] != SeatKind.Human)
            {
                lastRoomView.SeatKinds[s] = level > 0 ? SeatKind.Bot : SeatKind.Empty;
                lastRoomView.SeatLevels[s] = level;
                if (level > 0) lastRoomView.SeatNames[s] = "BOT " + AiProfile.Names[Math.Min(level, AiProfile.Names.Length) - 1];
                ShowRoom(lastRoomView, roomPreview);
            }
            OnRoomBot?.Invoke(s, level);
        }

        void PickBotStyle(int s, int style)
        {
            if (s < 0) return;
            if (lastRoomView != null)
            {
                lastRoomView.SeatStyles[s] = style;
                ShowRoom(lastRoomView, roomPreview);
            }
            OnRoomBotStyle?.Invoke(s, style);
        }
    }
}
