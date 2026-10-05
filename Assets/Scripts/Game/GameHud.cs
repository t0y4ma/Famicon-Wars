using System;
using System.Collections.Generic;
using FamiconWars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>
    /// Screen-space HUD ("field command board" look): opaque olive plates, paper-white text,
    /// brass for focus and the primary action, and the active army's colour as the signature tab.
    /// Built in code so it survives map/army changes; reads GameState, never writes it.
    /// </summary>
    public partial class GameHud : MonoBehaviour
    {
        // ---- design tokens ----
        public static readonly Color Plate = Hex("#20271F");
        public static readonly Color PlateRaised = Hex("#2E382C");
        public static readonly Color PlateEdge = Hex("#46523F");
        public static readonly Color Paper = Hex("#EFE6CC");
        public static readonly Color Muted = Hex("#A3AC92");
        public static readonly Color Brass = Hex("#D8A23A");
        public static readonly Color Danger = Hex("#F07A62");
        const int SizeSmall = 18, SizeBody = 22, SizeNumber = 30, SizeTitle = 36, SizeBanner = 52;

        TMP_FontAsset font;
        RectTransform canvasRt;

        // top bar
        Image armyTab, armyLine;
        TextMeshProUGUI armyText, dayText, fundsText, incomeText, controllerText;
        Button resupplyBtn, endBtn, surrenderBtn, speedBtn;

        // info card
        RectTransform infoCard;
        TextMeshProUGUI terrainName, terrainOwner, terrainDef, unitName, unitCount, unitCargo, forecastText;
        Image unitSwatch, fuelFill, ammoFill;
        GameObject unitBlock, forecastBlock;

        // menus
        RectTransform actionMenu, producePanel, dim, topBar;
        TextMeshProUGUI produceTitle;
        RectTransform produceList;
        RectTransform hintPlate; TextMeshProUGUI hintText;
        RectTransform toastPlate; TextMeshProUGUI toastText; float toastUntil;
        RectTransform bannerRt; CanvasGroup bannerGroup; Image bannerBand; TextMeshProUGUI bannerTitle, bannerSub; float bannerT = -1;
        RectTransform overPanel; TextMeshProUGUI overTitle, overReason; Image overStripe;

        public event Action OnResupply, OnEndPhase, OnSurrender, OnRestart, OnCloseProduce, OnSpeed;

        public bool PointerOverUi => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        public bool BannerShowing => bannerT >= 0;

        public void Build()
        {
            font = Resources.Load<TMP_FontAsset>("Fonts/NotoSansJP SDF");
            EnsureEventSystem();
            var cgo = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRt = (RectTransform)cgo.transform;

            BuildTopBar();
            BuildInfoCard();
            BuildHintAndToast();
            BuildActionMenu();
            BuildProduce();
            BuildBanner();
            BuildGameOver();
            BuildBattle();
            BuildMenus();
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        // ================= top bar =================

        void BuildTopBar()
        {
            var bar = Panel("TopBar", canvasRt, Plate);
            topBar = bar;
            Stretch(bar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), 0, 76);
            armyLine = Panel("ArmyLine", bar, Color.white).GetComponent<Image>();
            Stretch((RectTransform)armyLine.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), 0, 5);

            armyTab = Panel("ArmyTab", bar, Color.white).GetComponent<Image>();
            Pin((RectTransform)armyTab.transform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(260, 0), pivot: new Vector2(0, 0.5f), stretchY: true);
            armyText = Label("Army", armyTab.transform, SizeTitle, Color.white, TextAlignmentOptions.Center, true);
            Fill(armyText.rectTransform, 8, 18, 8, 0);
            controllerText = Label("Controller", armyTab.transform, SizeSmall, Color.white, TextAlignmentOptions.Bottom, false);
            Fill(controllerText.rectTransform, 8, 6, 8, 40);

            dayText = Label("Day", bar, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            Pin(dayText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(288, 0), new Vector2(170, 60), pivot: new Vector2(0, 0.5f));

            var fundsLabel = Label("FundsLabel", bar, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            fundsLabel.text = "資金";
            Pin(fundsLabel.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(470, 0), new Vector2(56, 40), pivot: new Vector2(0, 0.5f));
            fundsText = Label("Funds", bar, SizeNumber, Paper, TextAlignmentOptions.MidlineLeft, true);
            Pin(fundsText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(526, 0), new Vector2(200, 60), pivot: new Vector2(0, 0.5f));
            incomeText = Label("Income", bar, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            incomeText.textWrappingMode = TextWrappingModes.Normal;
            incomeText.lineSpacing = -8;
            Pin(incomeText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(730, 0), new Vector2(230, 64), pivot: new Vector2(0, 0.5f));

            surrenderBtn = MakeButton("Surrender", bar, "降伏", false, () => OnSurrender?.Invoke());
            Pin((RectTransform)surrenderBtn.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(110, 52), pivot: new Vector2(1, 0.5f));
            endBtn = MakeButton("EndPhase", bar, "手番終了", true, () => OnEndPhase?.Invoke());
            Pin((RectTransform)endBtn.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-150, 0), new Vector2(190, 52), pivot: new Vector2(1, 0.5f));
            resupplyBtn = MakeButton("Resupply", bar, "全補", false, () => OnResupply?.Invoke());
            Pin((RectTransform)resupplyBtn.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-356, 0), new Vector2(130, 52), pivot: new Vector2(1, 0.5f));
            speedBtn = MakeButton("Speed", bar, "", false, () => OnSpeed?.Invoke());
            Pin((RectTransform)speedBtn.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-502, 0), new Vector2(190, 52), pivot: new Vector2(1, 0.5f));
        }

        /// <param name="controller">"人間" or "COM 普通" etc.</param>
        /// <param name="humanTurn">false while a COM is playing: the phase buttons are locked.</param>
        public void SetTopBar(GameState s, int income, string controller, bool humanTurn, string speedLabel)
        {
            var c = ArmyColor(s.Active);
            armyTab.color = c; armyLine.color = c;
            armyText.text = Labels.Army(s.Active);
            controllerText.text = controller;
            dayText.text = s.Day + "<size=60%> 日目</size>";
            fundsText.text = s.Funds[(int)s.Active].ToString("N0");
            int units = RulesEngine.UnitCount(s, s.Active);
            incomeText.text = "収入 +" + income.ToString("N0") + "/日　部隊 " + (units >= s.Rules.UnitLimit ? "<color=#F07A62>" + units + "</color>" : units.ToString()) + "/" + s.Rules.UnitLimit + "\n相手資金 " + s.Funds[1 - (int)s.Active].ToString("N0");
            bool live = !s.GameOver && humanTurn;
            resupplyBtn.interactable = live && !s.ResupplyUsed[(int)s.Active];
            endBtn.interactable = live;
            surrenderBtn.interactable = live;
            speedBtn.GetComponentInChildren<TextMeshProUGUI>().text = "<size=75%><color=#A3AC92>速度</color></size> " + speedLabel;
        }

        // ================= info card =================

        void BuildInfoCard()
        {
            infoCard = Panel("InfoCard", canvasRt, Plate);
            Pin(infoCard, Vector2.zero, Vector2.zero, new Vector2(24, 24), new Vector2(400, 250), pivot: Vector2.zero);
            Edge(infoCard);

            terrainName = Label("Terrain", infoCard, SizeBody, Paper, TextAlignmentOptions.TopLeft, true);
            Pin(terrainName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -14), new Vector2(200, 34), pivot: new Vector2(0, 1));
            terrainOwner = Label("Owner", infoCard, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            Pin(terrainOwner.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(120, -18), new Vector2(150, 30), pivot: new Vector2(0, 1));
            terrainDef = Label("Def", infoCard, SizeSmall, Muted, TextAlignmentOptions.TopRight, false);
            Pin(terrainDef.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(150, 30), pivot: new Vector2(1, 1));

            unitBlock = new GameObject("Unit", typeof(RectTransform));
            var ub = (RectTransform)unitBlock.transform; ub.SetParent(infoCard, false);
            Pin(ub, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -58), new Vector2(0, 150), pivot: new Vector2(0.5f, 1), stretchX: true);
            var divider = Panel("Divider", ub, PlateEdge);
            Pin(divider, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(-36, 2), pivot: new Vector2(0.5f, 1), stretchX: true);
            unitSwatch = Panel("Icon", ub, Color.white).GetComponent<Image>();
            unitSwatch.preserveAspect = true; unitSwatch.raycastTarget = false;
            Pin((RectTransform)unitSwatch.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -6), new Vector2(52, 52), pivot: new Vector2(0, 1));
            unitName = Label("Name", ub, SizeBody, Paper, TextAlignmentOptions.TopLeft, true);
            Pin(unitName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(68, -14), new Vector2(220, 36), pivot: new Vector2(0, 1));
            unitCount = Label("Count", ub, SizeNumber, Paper, TextAlignmentOptions.TopRight, true);
            Pin(unitCount.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -8), new Vector2(120, 44), pivot: new Vector2(1, 1));
            fuelFill = Meter("Fuel", ub, "燃料", -64);
            ammoFill = Meter("Ammo", ub, "弾薬", -92);
            unitCargo = Label("Cargo", ub, SizeSmall, Muted, TextAlignmentOptions.TopLeft, false);
            Pin(unitCargo.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -116), new Vector2(-36, 28), pivot: new Vector2(0.5f, 1), stretchX: true);

            forecastBlock = new GameObject("Forecast", typeof(RectTransform), typeof(Image));
            var fb = (RectTransform)forecastBlock.transform; fb.SetParent(infoCard, false);
            forecastBlock.GetComponent<Image>().color = PlateRaised;
            forecastBlock.GetComponent<Image>().raycastTarget = false;
            Pin(fb, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 8), new Vector2(0, 96), pivot: new Vector2(0.5f, 0), stretchX: true);
            var brassTop = Panel("Brass", fb, Brass);
            Pin(brassTop, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 4), pivot: new Vector2(0.5f, 1), stretchX: true);
            forecastText = Label("Text", fb, SizeBody, Paper, TextAlignmentOptions.MidlineLeft, false);
            Fill(forecastText.rectTransform, 18, 6, 18, 0);
            forecastBlock.SetActive(false);
        }

        Image Meter(string name, RectTransform parent, string caption, float y)
        {
            var l = Label(name + "Label", parent, SizeSmall, Muted, TextAlignmentOptions.MidlineLeft, false);
            l.text = caption;
            Pin(l.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, y), new Vector2(60, 24), pivot: new Vector2(0, 1));
            var track = Panel(name + "Track", parent, PlateRaised);
            Pin(track, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, y - 6), new Vector2(-116, 12), pivot: new Vector2(0.5f, 1), stretchX: true);
            var fill = Panel(name + "Fill", track, Paper).GetComponent<Image>();
            Fill((RectTransform)fill.transform, 0, 0, 0, 0);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            fill.sprite = WhiteSprite();
            return fill;
        }

        public void ShowInfo(GameState s, int x, int y, string forecast)
        {
            if (!s.InBounds(x, y)) { infoCard.gameObject.SetActive(false); return; }
            infoCard.gameObject.SetActive(true);
            // stay on the side away from the cursor
            bool cursorLeft = x < s.Width / 2;
            infoCard.anchorMin = infoCard.anchorMax = infoCard.pivot = new Vector2(cursorLeft ? 1 : 0, 0);
            infoCard.anchoredPosition = new Vector2(cursorLeft ? -24 : 24, 24);

            var t = s.TerrainAt(x, y);
            terrainName.text = Labels.Get(t.NameKey);
            terrainOwner.text = t.IsProperty ? Labels.Army(s.Owner[s.Index(x, y)]) : "";
            terrainOwner.color = t.IsProperty ? ArmyTextColor(s.Owner[s.Index(x, y)]) : Muted;
            terrainDef.text = t.Cost[(int)MoveClass.Ship] > 0 && t.DefShip > 0 && t.Id != "PORT" ? "防御 艦" + t.DefShip
                : "防御 歩" + t.DefFoot + " 車" + t.DefVehicle + (t.DefShip > 1 ? " 艦" + t.DefShip : "");

            var u = s.UnitAt(x, y);
            unitBlock.SetActive(u != null);
            infoCard.sizeDelta = new Vector2(400, u != null ? 222 : 64);
            if (u != null)
            {
                var d = s.Def(u);
                var icon = UnitIcons.Get(d.Id, u.Army);
                unitSwatch.sprite = icon;
                unitSwatch.color = icon != null ? Color.white : ArmyColor(u.Army);
                unitSwatch.rectTransform.localScale = new Vector3(u.Army == Army.Blue ? -1 : 1, 1, 1);
                unitName.text = Labels.Get(d.NameKey);
                unitCount.text = "<size=60%>×</size>" + u.Count;
                unitCount.color = u.Count <= 3 ? Danger : Paper;
                fuelFill.fillAmount = d.Fuel > 0 ? (float)u.Fuel / d.Fuel : 0;
                fuelFill.color = u.Fuel <= d.Fuel / 5 ? Danger : Paper;
                ammoFill.fillAmount = d.Ammo > 0 ? (float)u.Ammo / d.Ammo : 0;
                ammoFill.color = d.Ammo > 0 && u.Ammo == 0 ? Danger : Paper;
                var cargo = new List<string>();
                foreach (var id in u.Cargo) { var c = s.UnitById(id); if (c != null) cargo.Add(Labels.Get(s.Def(c).NameKey) + "×" + c.Count); }
                unitCargo.text = cargo.Count > 0 ? "搭載: " + string.Join("、", cargo) : (u.Acted ? "行動済み" : "");
            }
            forecastBlock.SetActive(!string.IsNullOrEmpty(forecast));
            if (forecast != null) forecastText.text = forecast;
        }

        public void HideInfo() => infoCard.gameObject.SetActive(false);

        /// <summary>Top bar and info card belong to a running match; the setup screen hides them.</summary>
        public void ShowMatchChrome(bool on)
        {
            topBar.gameObject.SetActive(on);
            ShowZoomWidget(on);
            if (!on) infoCard.gameObject.SetActive(false);
        }

        // ================= hint / toast =================

        void BuildHintAndToast()
        {
            hintPlate = Panel("Hint", canvasRt, PlateRaised);
            Pin(hintPlate, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(560, 48), pivot: new Vector2(0.5f, 1));
            Edge(hintPlate, Brass);
            hintText = Label("Text", hintPlate, SizeBody, Paper, TextAlignmentOptions.Center, false);
            Fill(hintText.rectTransform, 12, 0, 12, 0);
            hintPlate.gameObject.SetActive(false);

            toastPlate = Panel("Toast", canvasRt, Plate);
            Pin(toastPlate, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(620, 64), pivot: new Vector2(0.5f, 0));
            Edge(toastPlate);
            toastText = Label("Text", toastPlate, SizeBody, Paper, TextAlignmentOptions.Center, false);
            Fill(toastText.rectTransform, 16, 6, 16, 6);
            toastPlate.gameObject.SetActive(false);
        }

        public void Hint(string text)
        {
            hintPlate.gameObject.SetActive(!string.IsNullOrEmpty(text));
            if (!string.IsNullOrEmpty(text)) hintText.text = text;
        }

        public void Toast(string text, bool warning = false)
        {
            toastText.text = text;
            toastText.color = warning ? Danger : Paper;
            int lines = text.Split('\n').Length;
            toastPlate.sizeDelta = new Vector2(620, 30 + 32 * lines);
            toastPlate.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + 3.2f;
        }

        // ================= action menu =================

        void BuildActionMenu()
        {
            actionMenu = Panel("ActionMenu", canvasRt, Plate);
            Pin(actionMenu, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(220, 100), pivot: new Vector2(0, 1));
            Edge(actionMenu);
            var vl = actionMenu.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(10, 10, 10, 10); vl.spacing = 6;
            vl.childForceExpandHeight = false; vl.childControlHeight = true; vl.childControlWidth = true;
            var fit = actionMenu.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            actionMenu.gameObject.SetActive(false);
        }

        /// <summary>Opens the command list beside the unit. screenPos is in screen pixels.</summary>
        public void ShowActionMenu(List<(string label, Action act)> items, Vector2 screenPos)
        {
            ClearChildren(actionMenu);
            Button first = null;
            foreach (var it in items)
            {
                var item = it;
                var b = MakeButton(item.label, actionMenu, item.label, item.label == "攻撃", () => item.act());
                var le = b.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 52;
                if (first == null) first = b;
            }
            actionMenu.gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPos, null, out var local);
            var size = canvasRt.rect.size;
            float h = 20 + items.Count * 58;
            float x = local.x + size.x / 2 + 70, y = local.y + size.y / 2 + 40;
            if (x + 220 > size.x - 16) x = local.x + size.x / 2 - 70 - 220;
            y = Mathf.Clamp(y, h + 16, size.y - 96);
            actionMenu.anchoredPosition = new Vector2(x, y);
            if (first != null) EventSystem.current?.SetSelectedGameObject(first.gameObject);
        }

        public void HideActionMenu() => actionMenu.gameObject.SetActive(false);

        // ================= production =================

        void BuildProduce()
        {
            dim = Panel("Dim", canvasRt, new Color(0, 0, 0, 0.45f));
            Fill(dim, 0, 0, 0, 0);
            dim.GetComponent<Image>().raycastTarget = true;
            dim.gameObject.SetActive(false);

            producePanel = Panel("Produce", canvasRt, Plate);
            Pin(producePanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680, 600));
            Edge(producePanel, Brass);
            produceTitle = Label("Title", producePanel, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            Pin(produceTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -12), new Vector2(-48, 60), pivot: new Vector2(0.5f, 1), stretchX: true);
            var close = MakeButton("Close", producePanel, "閉じる", false, () => OnCloseProduce?.Invoke());
            Pin((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(120, 46), pivot: new Vector2(1, 1));
            var listGo = new GameObject("List", typeof(RectTransform));
            produceList = (RectTransform)listGo.transform; produceList.SetParent(producePanel, false);
            Fill(produceList, 20, 20, 20, 84);
            var vl = listGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 6; vl.childForceExpandHeight = false; vl.childControlHeight = true; vl.childControlWidth = true;
            producePanel.gameObject.SetActive(false);
        }

        public void ShowProduce(string facilityName, int funds, List<UnitDef> units, Action<UnitDef> pick, Army army = Army.Red)
        {
            ClearChildren(produceList);
            produceTitle.text = "生産 <size=60%><color=#A3AC92>" + facilityName + "   資金 " + funds.ToString("N0") + "</color></size>";
            Button first = null;
            foreach (var d in units)
            {
                var def = d;
                bool afford = funds >= d.Price;
                var b = MakeButton(d.Id, produceList, "", false, () => pick(def));
                b.interactable = afford;
                var le = b.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 50;
                var txt = b.GetComponentInChildren<TextMeshProUGUI>();
                txt.alignment = TextAlignmentOptions.MidlineLeft;
                txt.text = Labels.Get(d.NameKey);
                Fill(txt.rectTransform, 70, 0, 250, 0);
                var pic = Panel("Icon", b.transform, Color.white).GetComponent<Image>();
                pic.raycastTarget = false; pic.preserveAspect = true;
                pic.sprite = UnitIcons.Get(d.Id, army);
                if (pic.sprite == null) pic.color = Color.clear;
                else if (!afford) pic.color = new Color(1, 1, 1, 0.4f);
                Pin(pic.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(46, 46), pivot: new Vector2(0, 0.5f));
                pic.rectTransform.localScale = new Vector3(army == Army.Blue ? -1 : 1, 1, 1);
                var stats = Label("Stats", b.transform, SizeSmall, afford ? Muted : Danger, TextAlignmentOptions.MidlineRight, false);
                stats.text = afford ? $"移{d.Move}  射{d.RangeMin}-{d.RangeMax}  燃{d.Fuel}" : "資金不足";
                Fill(stats.rectTransform, 220, 0, 150, 0);
                var price = Label("Price", b.transform, SizeBody, afford ? Paper : Muted, TextAlignmentOptions.MidlineRight, true);
                price.text = d.Price.ToString("N0");
                Fill(price.rectTransform, 500, 0, 18, 0);
                if (first == null && afford) first = b;
            }
            producePanel.sizeDelta = new Vector2(680, 110 + units.Count * 56);
            dim.gameObject.SetActive(true);
            dim.SetAsLastSibling();
            producePanel.SetAsLastSibling();
            producePanel.gameObject.SetActive(true);
            if (first != null) EventSystem.current?.SetSelectedGameObject(first.gameObject);
        }

        public void HideProduce() { producePanel.gameObject.SetActive(false); dim.gameObject.SetActive(false); }

        // ================= phase banner =================

        void BuildBanner()
        {
            var go = new GameObject("PhaseBanner", typeof(RectTransform), typeof(CanvasGroup));
            bannerRt = (RectTransform)go.transform; bannerRt.SetParent(canvasRt, false);
            Pin(bannerRt, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 40), new Vector2(0, 150), stretchX: true);
            bannerGroup = go.GetComponent<CanvasGroup>(); bannerGroup.blocksRaycasts = false; bannerGroup.interactable = false;
            bannerBand = Panel("Band", bannerRt, Color.white).GetComponent<Image>();
            Fill((RectTransform)bannerBand.transform, 0, 0, 0, 0);
            var stripeTop = Panel("Top", bannerRt, Plate); Pin(stripeTop, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 8), pivot: new Vector2(0.5f, 1), stretchX: true);
            var stripeBottom = Panel("Bottom", bannerRt, Plate); Pin(stripeBottom, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 8), pivot: new Vector2(0.5f, 0), stretchX: true);
            bannerTitle = Label("Title", bannerRt, SizeBanner, Color.white, TextAlignmentOptions.Center, true);
            Pin(bannerTitle.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 16), new Vector2(0, 80), stretchX: true);
            bannerSub = Label("Sub", bannerRt, SizeBody, Color.white, TextAlignmentOptions.Center, false);
            Pin(bannerSub.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -38), new Vector2(0, 36), stretchX: true);
            foreach (var g in go.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            go.SetActive(false);
        }

        public void PhaseBanner(Army army, int day, int income, string controller)
        {
            bannerBand.color = ArmyColor(army);
            bannerTitle.text = day + "日目　" + Labels.Army(army) + "のフェーズ";
            bannerSub.text = controller + "　収入 +" + income.ToString("N0");
            bannerRt.gameObject.SetActive(true);
            bannerT = 0;
        }

        // ================= game over =================

        void BuildGameOver()
        {
            overPanel = Panel("GameOver", canvasRt, Plate);
            Pin(overPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 300));
            overStripe = Panel("Stripe", overPanel, Color.white).GetComponent<Image>();
            Pin((RectTransform)overStripe.transform, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 14), pivot: new Vector2(0.5f, 1), stretchX: true);
            overTitle = Label("Title", overPanel, SizeBanner, Paper, TextAlignmentOptions.Center, true);
            Pin(overTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -44), new Vector2(0, 80), pivot: new Vector2(0.5f, 1), stretchX: true);
            overReason = Label("Reason", overPanel, SizeBody, Muted, TextAlignmentOptions.Center, false);
            Pin(overReason.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -130), new Vector2(0, 40), pivot: new Vector2(0.5f, 1), stretchX: true);
            var again = MakeButton("Again", overPanel, "もう一度", true, () => OnRestart?.Invoke());
            Pin((RectTransform)again.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(240, 60), pivot: new Vector2(0.5f, 0));
            overPanel.gameObject.SetActive(false);
        }

        public void ShowGameOver(Army winner, string reason)
        {
            dim.gameObject.SetActive(true);
            dim.SetAsLastSibling();
            overPanel.SetAsLastSibling();
            overStripe.color = winner == Army.None ? Muted : ArmyColor(winner);
            overTitle.text = winner == Army.None ? "引き分け" : Labels.Army(winner) + "の勝利";
            overReason.text = reason;
            overPanel.gameObject.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(overPanel.GetComponentInChildren<Button>().gameObject);
        }

        public void ResetScreens()
        {
            overPanel.gameObject.SetActive(false);
            HideMenus(); HideProduce(); HideActionMenu(); Hint(null);
            toastPlate.gameObject.SetActive(false);
        }

        void Update()
        {
            UpdateBattle();
            if (toastPlate != null && toastPlate.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastPlate.gameObject.SetActive(false);
            if (bannerT >= 0)
            {
                bannerT += Time.unscaledDeltaTime;
                const float inT = 0.18f, hold = 0.9f, outT = 0.35f;
                float a = bannerT < inT ? bannerT / inT : bannerT < inT + hold ? 1 : 1 - (bannerT - inT - hold) / outT;
                bannerGroup.alpha = Mathf.Clamp01(a);
                float slide = bannerT < inT ? (1 - bannerT / inT) * 120 : 0;
                bannerTitle.rectTransform.anchoredPosition = new Vector2(slide, 16);
                if (bannerT > inT + hold + outT) { bannerT = -1; bannerRt.gameObject.SetActive(false); }
            }
        }

        // ================= helpers =================

        public static Color ArmyColor(Army a) => a == Army.Red ? Hex("#C8423A") : a == Army.Blue ? Hex("#3A6AD0") : Muted;
        static Color ArmyTextColor(Army a) => a == Army.Red ? Hex("#FF8A7E") : a == Army.Blue ? Hex("#8FB2FF") : Muted;

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static Sprite white;
        static Sprite WhiteSprite()
        {
            if (white != null) return white;
            var t = new Texture2D(4, 4); var p = new Color[16]; for (int i = 0; i < 16; i++) p[i] = Color.white; t.SetPixels(p); t.Apply();
            white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return white;
        }

        static RectTransform Panel(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = c.a > 0.9f && name != "ArmyLine";
            return (RectTransform)go.transform;
        }

        /// <summary>One contrast strategy: opaque plate + a 3px edge line on top.</summary>
        static void Edge(RectTransform plate, Color? c = null)
        {
            var e = Panel("Edge", plate, c ?? PlateEdge);
            e.GetComponent<Image>().raycastTarget = false;
            var le = e.gameObject.AddComponent<LayoutElement>(); le.ignoreLayout = true;
            Pin(e, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 3), pivot: new Vector2(0.5f, 1), stretchX: true);
        }

        TextMeshProUGUI Label(string name, Transform parent, int size, Color c, TextAlignmentOptions align, bool bold)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size; t.color = c; t.alignment = align; t.raycastTarget = false;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        Button MakeButton(string name, Transform parent, string text, bool primary, Action onClick)
        {
            var rt = Panel(name, parent, primary ? Brass : PlateRaised);
            var img = rt.GetComponent<Image>(); img.raycastTarget = true; img.color = Color.white;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            var baseC = primary ? Brass : PlateRaised;
            cb.normalColor = baseC;
            cb.highlightedColor = primary ? Hex("#F0BE58") : Hex("#3E4B3A");
            cb.selectedColor = primary ? Hex("#F0BE58") : Hex("#4A5A3F");
            cb.pressedColor = primary ? Hex("#B9862A") : Hex("#263022");
            cb.disabledColor = new Color(baseC.r, baseC.g, baseC.b, 0.35f);
            cb.colorMultiplier = 1; cb.fadeDuration = 0.06f;
            b.colors = cb;
            img.canvasRenderer.SetColor(baseC);   // start at the final colour: no white flash on creation
            b.onClick.AddListener(() => onClick());
            // focus bar on the left edge: selected state must read from a distance
            var focus = Panel("Focus", rt, Brass); focus.GetComponent<Image>().raycastTarget = false;
            Pin(focus, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(6, 0), pivot: new Vector2(0, 0.5f), stretchY: true);
            focus.gameObject.AddComponent<FocusMarker>().Target = b;
            var label = Label("Text", rt, SizeBody, primary ? Plate : Paper, TextAlignmentOptions.Center, true);
            label.text = text;
            Fill(label.rectTransform, 12, 0, 12, 0);
            return b;
        }

        static void ClearChildren(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--)
            {
                var c = rt.GetChild(i);
                if (c.name == "Edge") continue;
                c.SetParent(null);
                Destroy(c.gameObject);
            }
        }

        static void Fill(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
        }

        static void Stretch(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, float y, float height)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.sizeDelta = new Vector2(0, height); rt.anchoredPosition = new Vector2(0, y);
        }

        static void Pin(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, Vector2? pivot = null, bool stretchX = false, bool stretchY = false)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            if (stretchY) { rt.anchorMin = new Vector2(aMin.x, 0); rt.anchorMax = new Vector2(aMax.x, 1); size.y = 0; }
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            if (stretchY) rt.pivot = pivot ?? new Vector2(0, 0.5f);
        }
    }

    /// <summary>Shows the brass focus bar while its button is the EventSystem selection.</summary>
    public class FocusMarker : MonoBehaviour
    {
        public Button Target;
        TMPro.TextMeshProUGUI label; Color labelOn; bool wasOn = true;
        Image img;
        void Awake() { img = GetComponent<Image>(); }
        void LateUpdate()
        {
            if (img == null || Target == null) return;
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            img.enabled = sel == Target.gameObject && Target.interactable;
            // disabled buttons also dim their caption (ColorTint only reaches the plate)
            if (label == null) { var t = Target.transform.Find("Text"); if (t != null) label = t.GetComponent<TMPro.TextMeshProUGUI>(); }
            if (label != null && Target.interactable != wasOn)
            {
                if (!Target.interactable) { labelOn = label.color; label.color = new Color(labelOn.r, labelOn.g, labelOn.b, 0.4f); }
                else label.color = labelOn;
                wasOn = Target.interactable;
            }
        }
    }
}
