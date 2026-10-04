using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>Online lobby: server address, create a room (get a 4-digit code) or join one, then wait.</summary>
    public partial class GameHud
    {
        public event Action<string> OnOnlineCreate;          // address
        public event Action<string, string> OnOnlineJoin;    // address, code
        public event Action OnOnlineBack, OnOnlineCancel;

        RectTransform onlineRoot, onlineForm, onlineWait;
        TMP_InputField addressInput, codeInput;
        TextMeshProUGUI onlineStatus, waitCode, waitNote;
        Button onlineCreate, onlineJoin, onlineCancel;

        void BuildOnline()
        {
            onlineRoot = Panel("OnlineLobby", canvasRt, Backdrop);
            Fill(onlineRoot, 0, 0, 0, 0);

            var title = Label("Heading", onlineRoot, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "オンライン対戦";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -48), new Vector2(600, 60), pivot: new Vector2(0, 1));
            var sub = Label("Sub", onlineRoot, SizeBody, Muted, TextAlignmentOptions.MidlineLeft, false);
            sub.text = "部屋を作って番号を相手に伝えるか、もらった番号で部屋に入ります";
            Pin(sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -108), new Vector2(1200, 36), pivot: new Vector2(0, 1));

            // ---- form ----
            onlineForm = Panel("Form", onlineRoot, Plate);
            Pin(onlineForm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(880, 560));
            Edge(onlineForm, Brass);

            FormLabel("サーバー", -40);
            addressInput = MakeInput("Address", onlineForm, "サーバーのアドレス", TMP_InputField.ContentType.Standard);
            Pin((RectTransform)addressInput.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -40), new Vector2(620, 56), pivot: new Vector2(0, 1));

            var create = MakeButton("Create", onlineForm, "部屋を作る", true, () => OnOnlineCreate?.Invoke(addressInput.text));
            onlineCreate = create;
            Pin((RectTransform)create.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -136), new Vector2(620, 64), pivot: new Vector2(0, 1));
            var createNote = Label("CreateNote", onlineForm, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            createNote.text = "作った人がレッド軍(先手)。マップは「はじめる」で選んだものを使います";
            Pin(createNote.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -208), new Vector2(620, 30), pivot: new Vector2(0, 1));

            var divider = Panel("Divider", onlineForm, PlateEdge);
            Pin(divider, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -262), new Vector2(-80, 2), pivot: new Vector2(0.5f, 1), stretchX: true);

            FormLabel("部屋番号", -300);
            codeInput = MakeInput("Code", onlineForm, "4桁の番号", TMP_InputField.ContentType.IntegerNumber);
            codeInput.characterLimit = 4;
            Pin((RectTransform)codeInput.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -300), new Vector2(260, 56), pivot: new Vector2(0, 1));
            onlineJoin = MakeButton("Join", onlineForm, "部屋に入る", false, () => OnOnlineJoin?.Invoke(addressInput.text, codeInput.text));
            Pin((RectTransform)onlineJoin.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(500, -300), new Vector2(340, 56), pivot: new Vector2(0, 1));
            var joinNote = Label("JoinNote", onlineForm, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            joinNote.text = "切断しても、同じブラウザで同じ番号に入り直せば続きから遊べます";
            joinNote.textWrappingMode = TextWrappingModes.Normal;
            Pin(joinNote.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -366), new Vector2(620, 30), pivot: new Vector2(0, 1));

            onlineStatus = Label("Status", onlineForm, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, false);
            onlineStatus.textWrappingMode = TextWrappingModes.Normal;
            Pin(onlineStatus.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 40), new Vector2(-80, 70), pivot: new Vector2(0.5f, 0), stretchX: true);

            // ---- waiting ----
            onlineWait = Panel("Waiting", onlineRoot, Plate);
            Pin(onlineWait, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(720, 420));
            Edge(onlineWait, Brass);
            var wl = Label("Label", onlineWait, SizeBody, Muted, TextAlignmentOptions.Center, false);
            wl.text = "部屋番号";
            Pin(wl.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -44), new Vector2(0, 36), pivot: new Vector2(0.5f, 1), stretchX: true);
            waitCode = Label("Code", onlineWait, 104, Brass, TextAlignmentOptions.Center, true);
            waitCode.overflowMode = TextOverflowModes.Overflow;
            waitCode.characterSpacing = 18;
            Pin(waitCode.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -84), new Vector2(0, 140), pivot: new Vector2(0.5f, 1), stretchX: true);
            waitNote = Label("Note", onlineWait, SizeBody, Paper, TextAlignmentOptions.Center, false);
            waitNote.textWrappingMode = TextWrappingModes.Normal;
            Pin(waitNote.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -236), new Vector2(-60, 70), pivot: new Vector2(0.5f, 1), stretchX: true);
            onlineCancel = MakeButton("Cancel", onlineWait, "やめる", false, () => OnOnlineCancel?.Invoke());
            Pin((RectTransform)onlineCancel.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(240, 60), pivot: new Vector2(0.5f, 0));

            var back = MakeButton("Back", onlineRoot, "戻る", false, () => OnOnlineBack?.Invoke());
            Pin((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(96, 56), new Vector2(200, 64), pivot: new Vector2(0, 0));

            onlineRoot.gameObject.SetActive(false);
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

        public void HideOnline() { if (onlineRoot != null) onlineRoot.gameObject.SetActive(false); }

        /// <summary>The lobby form. Fields keep what the player typed unless a value is given.</summary>
        public void ShowOnline(string address, string code, string status, bool error)
        {
            ShowOnly(onlineRoot, onlineCreate);
            onlineForm.gameObject.SetActive(true);
            onlineWait.gameObject.SetActive(false);
            if (address != null && string.IsNullOrEmpty(addressInput.text)) addressInput.text = address;
            if (code != null) codeInput.text = code;
            OnlineStatus(status, error);
            SetOnlineBusy(false);
        }

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

        public void ShowOnlineWaiting(string code, string note)
        {
            ShowOnly(onlineRoot, onlineCancel);
            onlineForm.gameObject.SetActive(false);
            onlineWait.gameObject.SetActive(true);
            waitCode.text = code;
            waitNote.text = note;
        }

        public bool OnlineLobbyVisible => onlineRoot != null && onlineRoot.gameObject.activeSelf;
    }
}
