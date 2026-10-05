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
        public bool Started, GameOver, IAmHost;
        public int MySeat;                        // 0 red, 1 blue, -1 watching
        public SeatKind[] SeatKinds = new SeatKind[2];
        public string[] SeatNames = new string[2];
        public int[] SeatLevels = new int[2];
        public bool[] SeatOnline = new bool[2];
        public readonly List<(int id, string name, int seat, bool online, bool host, bool me)> Members = new List<(int, string, int, bool, bool, bool)>();
    }

    /// <summary>
    /// Online lobby (server, name, create / join) and the room screen: two seats and the members
    /// watching. The host picks the map, puts BOTs in empty seats, can hand the host role over and
    /// starts the match. Everyone can take a free seat or stand up to watch.
    /// </summary>
    public partial class GameHud
    {
        public event Action<string> OnOnlineCreate;          // address
        public event Action<string, string> OnOnlineJoin;    // address, code
        public event Action OnOnlineBack;
        public event Action<int> OnRoomSit, OnRoomHost, OnRoomMap;   // seat / member id / map step (-1, +1)
        public event Action<int, int> OnRoomBot;             // seat, level 0-4
        public event Action OnRoomStand, OnRoomStart, OnRoomLeave;

        RectTransform onlineRoot, onlineForm, roomRoot, roomBody;
        TMP_InputField addressInput, codeInput, nameInput;
        TextMeshProUGUI onlineStatus, roomStatus;
        Button onlineCreate, onlineJoin;
        Func<string, Texture2D> roomPreview;
        RoomView lastRoomView;
        int botMenuSeat = -1;                       // seat whose BOT strength list is open (-1 = none)
        readonly Vector2[] botButtonPos = new Vector2[2];

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
            Pin(onlineForm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(880, 620));
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
            var createNote = Label("CreateNote", onlineForm, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            createNote.text = "作った人が部屋主です。マップや BOT は部屋の中で決めます";
            Pin(createNote.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -268), new Vector2(620, 30), pivot: new Vector2(0, 1));

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

            var back = MakeButton("Back", onlineRoot, "戻る", false, () => OnOnlineBack?.Invoke());
            Pin((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(96, 56), new Vector2(200, 64), pivot: new Vector2(0, 0));

            // ---- room screen (its own full-screen page; rebuilt on every status) ----
            roomRoot = Panel("Room", canvasRt, Backdrop);
            Fill(roomRoot, 0, 0, 0, 0);
            roomBody = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            roomBody.SetParent(roomRoot, false);
            Fill(roomBody, 0, 0, 0, 0);
            roomStatus = Label("Status", roomRoot, SizeBody, Danger, TextAlignmentOptions.Center, false);
            Pin(roomStatus.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 132), new Vector2(0, 36), pivot: new Vector2(0.5f, 0), stretchX: true);

            onlineRoot.gameObject.SetActive(false);
            roomRoot.gameObject.SetActive(false);
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

        /// <summary>Shows (or refreshes) the room screen.</summary>
        public void ShowRoom(RoomView v, Func<string, Texture2D> preview)
        {
            roomPreview = preview;
            lastRoomView = v;
            if (botMenuSeat >= 0 && (!v.IAmHost || v.Started || v.SeatKinds[botMenuSeat] == SeatKind.Human)) botMenuSeat = -1;
            onlineRoot.gameObject.SetActive(false);
            ShowOnly(roomRoot, null);
            roomRoot.SetAsLastSibling();
            ClearChildren(roomBody);
            roomStatus.text = "";

            // ---- header: room number and how to invite ----
            var title = Label("Heading", roomBody, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "部屋";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -40), new Vector2(120, 60), pivot: new Vector2(0, 1));
            var code = Label("Code", roomBody, 64, Brass, TextAlignmentOptions.MidlineLeft, true);
            code.text = v.Code; code.characterSpacing = 12; code.overflowMode = TextOverflowModes.Overflow;
            Pin(code.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(210, -28), new Vector2(320, 84), pivot: new Vector2(0, 1));
            var invite = Label("Invite", roomBody, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            invite.text = v.Started ? (v.GameOver ? "対戦は終わりました" : "対戦中です。この番号で入ると観戦できます") : "この番号を伝えると、相手や観戦者が入れます";
            Pin(invite.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(520, -48), new Vector2(900, 44), pivot: new Vector2(0, 1));

            // ---- map ----
            var mapPlate = Panel("Map", roomBody, Plate);
            Pin(mapPlate, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-96, -150), new Vector2(560, 300), pivot: new Vector2(1, 1));
            Edge(mapPlate, PlateEdge);
            var entry = MapCatalog.Find(v.MapId);
            var mapLabel = Label("Label", mapPlate, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            mapLabel.text = "マップ";
            Pin(mapLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -18), new Vector2(200, 28), pivot: new Vector2(0, 1));
            var mapName = Label("Name", mapPlate, SizeTitle - 6, Paper, TextAlignmentOptions.TopLeft, true);
            mapName.text = entry != null ? entry.Name : v.MapId;
            Pin(mapName.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -46), new Vector2(-48, 44), pivot: new Vector2(0.5f, 1), stretchX: true);
            var tex = preview?.Invoke(v.MapId);
            if (tex != null)
            {
                var ri = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                ri.transform.SetParent(mapPlate, false);
                ri.texture = tex; ri.raycastTarget = false;
                float aspect = (float)tex.width / tex.height;
                var size = aspect >= 300f / 180f ? new Vector2(300, 300 / aspect) : new Vector2(180 * aspect, 180);
                Pin(ri.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), size, pivot: new Vector2(0, 0));
            }
            var mapNote = Label("Note", mapPlate, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            mapNote.textWrappingMode = TextWrappingModes.Normal;
            mapNote.text = entry != null ? entry.Width + " × " + entry.Height + "\n" + entry.Note : "";
            Pin(mapNote.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(340, 24), new Vector2(196, 180), pivot: new Vector2(0, 0));
            if (v.IAmHost && !v.Started)
            {
                var prev = MakeButton("Prev", mapPlate, "◀", false, () => OnRoomMap?.Invoke(-1));
                Pin((RectTransform)prev.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-92, -14), new Vector2(60, 46), pivot: new Vector2(1, 1));
                var next = MakeButton("Next", mapPlate, "▶", false, () => OnRoomMap?.Invoke(+1));
                Pin((RectTransform)next.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -14), new Vector2(60, 46), pivot: new Vector2(1, 1));
            }

            // ---- seats ----
            for (int s = 0; s < 2; s++) SeatCard(v, s, new Vector2(96 + s * 500, -150));

            // ---- members ----
            var list = Panel("Members", roomBody, Plate);
            Pin(list, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -480), new Vector2(980, 380), pivot: new Vector2(0, 1));
            Edge(list, PlateEdge);
            var lt = Label("Title", list, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            lt.text = "部屋にいる人(" + v.Members.Count + ")   ★ = 部屋主";
            Pin(lt.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -16), new Vector2(-48, 28), pivot: new Vector2(0.5f, 1), stretchX: true);
            float y = -52;
            foreach (var m in v.Members)
            {
                if (y < -350) break;
                var row = Label("Row", list, SizeBody, m.online ? Paper : Muted, TextAlignmentOptions.MidlineLeft, false);
                string role = m.seat == 0 ? "<color=#FF8A7E>レッド軍</color>" : m.seat == 1 ? "<color=#8FB2FF>ブルー軍</color>" : "観戦";
                row.text = (m.host ? "<color=#D8A23A>★</color> " : "　 ") + m.name + (m.me ? "<color=#A3AC92>(あなた)</color>" : "") + "　" + role + (m.online ? "" : "<color=#A3AC92>(接続待ち)</color>");
                Pin(row.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, y), new Vector2(700, 40), pivot: new Vector2(0, 1));
                if (v.IAmHost && !m.me && m.online)
                {
                    int id = m.id;
                    var give = MakeButton("Host" + id, list, "部屋主にする", false, () => OnRoomHost?.Invoke(id));
                    Pin((RectTransform)give.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, y), new Vector2(200, 40), pivot: new Vector2(1, 1));
                }
                y -= 46;
            }

            // ---- footer ----
            var leave = MakeButton("Leave", roomBody, "部屋を出る", false, () => OnRoomLeave?.Invoke());
            Pin((RectTransform)leave.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(96, 56), new Vector2(240, 64), pivot: new Vector2(0, 0));
            if (!v.Started && v.MySeat >= 0)
            {
                var stand = MakeButton("Stand", roomBody, "観戦に回る", false, () => OnRoomStand?.Invoke());
                Pin((RectTransform)stand.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(360, 56), new Vector2(240, 64), pivot: new Vector2(0, 0));
            }
            var hint = Label("Hint", roomBody, SizeBody, Muted, TextAlignmentOptions.MidlineRight, false);
            bool ready = v.SeatKinds[0] != SeatKind.Empty && v.SeatKinds[1] != SeatKind.Empty;
            if (v.IAmHost && !v.Started)
            {
                var start = MakeButton("Start", roomBody, "対戦開始", true, () => OnRoomStart?.Invoke());
                start.interactable = ready;
                Pin((RectTransform)start.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-96, 56), new Vector2(300, 64), pivot: new Vector2(1, 0));
                hint.text = ready ? "" : "両方の席が埋まると開始できます(空席には BOT も置けます)";
                if (ready) EventSystem.current?.SetSelectedGameObject(start.gameObject);
            }
            else if (v.Started && v.GameOver && v.IAmHost)
            {
                var again = MakeButton("Again", roomBody, "部屋に戻す", true, () => OnRoomStart?.Invoke());
                Pin((RectTransform)again.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-96, 56), new Vector2(300, 64), pivot: new Vector2(1, 0));
                hint.text = "席とマップを決め直して次の対戦ができます";
            }
            else hint.text = v.Started ? "" : "部屋主が対戦を始めるのを待っています";
            Pin(hint.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-420, 66), new Vector2(760, 44), pivot: new Vector2(1, 0));

            if (botMenuSeat >= 0) BotMenu(v, botMenuSeat);
        }

        /// <summary>The BOT strengths in a row under the seat's BOT button; a click elsewhere closes it.</summary>
        void BotMenu(RoomView v, int s)
        {
            var blocker = MakeButton("BotMenuBlocker", roomBody, "", false, () => { botMenuSeat = -1; ShowRoom(lastRoomView, roomPreview); });
            var bimg = blocker.GetComponent<Image>(); if (bimg != null) bimg.color = new Color(0, 0, 0, 0.25f);
            Fill((RectTransform)blocker.transform, 0, 0, 0, 0);
            int level = v.SeatKinds[s] == SeatKind.Bot ? v.SeatLevels[s] : 0;
            const float w = 112, gap = 8;
            var strip = Panel("BotMenu", roomBody, PlateRaised);
            Edge(strip, Brass);
            Pin(strip, new Vector2(0, 1), new Vector2(0, 1), botButtonPos[s] + new Vector2(-8, -8), new Vector2(BotLabels.Length * (w + gap) + gap, 72), pivot: new Vector2(0, 1));
            for (int i = 0; i < BotLabels.Length; i++)
            {
                int lv = i;
                var b = MakeButton("Lv" + i, strip, BotLabels[i], i == level, () => { botMenuSeat = -1; OnRoomBot?.Invoke(s, lv); });
                Pin((RectTransform)b.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(gap + i * (w + gap), 0), new Vector2(w, 56), pivot: new Vector2(0, 0.5f));
                if (i == level) EventSystem.current?.SetSelectedGameObject(b.gameObject);
            }
        }

        void SeatCard(RoomView v, int s, Vector2 pos)
        {
            var cardPos = pos;
            var army = (Army)s;
            var card = Panel("Seat" + s, roomBody, PlateRaised);
            Pin(card, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(480, 300), pivot: new Vector2(0, 1));
            var tab = Panel("Tab", card, ArmyColor(army));
            tab.GetComponent<Image>().raycastTarget = false;
            Pin(tab, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 8), pivot: new Vector2(0.5f, 1), stretchX: true);
            var head = Label("Army", card, SizeBody, ArmyTextColor(army), TextAlignmentOptions.TopLeft, true);
            head.text = Labels.Army(army) + (s == 0 ? "(先手)" : "(後手)");
            Pin(head.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -24), new Vector2(-48, 34), pivot: new Vector2(0.5f, 1), stretchX: true);

            var icon = Panel("Icon", card, Color.white).GetComponent<Image>();
            icon.raycastTarget = false; icon.preserveAspect = true;
            icon.sprite = UnitIcons.Get(v.SeatKinds[s] == SeatKind.Bot ? "TANK_A" : "INF", army);
            if (icon.sprite == null || v.SeatKinds[s] == SeatKind.Empty) icon.color = new Color(1, 1, 1, v.SeatKinds[s] == SeatKind.Empty ? 0.18f : 1f);
            Pin(icon.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(72, -118), new Vector2(96, 96), pivot: new Vector2(0.5f, 0.5f));
            if (s == 1) icon.rectTransform.localScale = new Vector3(-1, 1, 1);

            var who = Label("Who", card, SizeTitle - 4, v.SeatKinds[s] == SeatKind.Empty ? Muted : Paper, TextAlignmentOptions.MidlineLeft, true);
            who.text = v.SeatKinds[s] == SeatKind.Empty ? "空席" : v.SeatNames[s];
            Pin(who.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(136, -78), new Vector2(320, 48), pivot: new Vector2(0, 1));
            var state = Label("State", card, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            state.text = v.SeatKinds[s] == SeatKind.Bot ? "COM がサーバーで操作します"
                : v.SeatKinds[s] == SeatKind.Human ? (v.MySeat == s ? "あなたの席です" : v.SeatOnline[s] ? "準備できています" : "接続が切れています")
                : "誰でも座れます";
            Pin(state.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(136, -128), new Vector2(320, 30), pivot: new Vector2(0, 1));

            if (v.Started) return;
            float bx = 24;
            if (v.SeatKinds[s] == SeatKind.Empty && v.MySeat != s)
            {
                var sit = MakeButton("Sit", card, "ここに座る", true, () => OnRoomSit?.Invoke(s));
                Pin((RectTransform)sit.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(bx, 24), new Vector2(200, 56), pivot: new Vector2(0, 0));
                bx += 216;
            }
            if (v.IAmHost && v.SeatKinds[s] != SeatKind.Human)
            {
                int level = v.SeatKinds[s] == SeatKind.Bot ? v.SeatLevels[s] : 0;
                var bot = MakeButton("Bot", card, "BOT: " + BotLabels[level] + "  ▼", false, () => { botMenuSeat = botMenuSeat == s ? -1 : s; ShowRoom(lastRoomView, roomPreview); });
                // top-left of the button in the room page's coordinates (card is 480 x 300, pinned top-left at pos)
                botButtonPos[s] = cardPos + new Vector2(bx, -300 + 24 + 56);
                Pin((RectTransform)bot.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(bx, 24), new Vector2(216, 56), pivot: new Vector2(0, 0));
            }
        }
    }
}
