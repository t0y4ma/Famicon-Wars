using System.Collections.Generic;
using FamiconWars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>What the battle cut-in needs. Captured before the command is applied (losers vanish after).</summary>
    public sealed class BattleSetup
    {
        public BattleEvent Event;
        public string AttName, DefName;
        public Army AttArmy, DefArmy;
        public UnitDef AttDef, DefDef;
        public TerrainDef AttTerrain, DefTerrain;
    }

    /// <summary>
    /// Battle scene in the spirit of the original: the screen splits, each side shows its machines
    /// (one figure per machine, up to 10) standing on its own terrain, and both sides open fire at the
    /// same moment — damage is simultaneous in this game. Direct fire crosses the divider; indirect fire
    /// is lobbed out of the top of the attacker's side and falls onto the defender, with no reply.
    /// A machine that is lost flashes and explodes when the round that killed it lands.
    /// Purely presentational: the result is already applied when this plays.
    /// </summary>
    public partial class GameHud
    {
        enum Sil { Soldier, Bazooka, Tank, HeavyTank, Artillery, Rocket, AntiAir, Truck, Jet, Bomber, Heli, Ship, Lander }

        sealed class Side
        {
            public RectTransform Scene, Units;
            public Image Sky, Ground, Tab;
            public TextMeshProUGUI Name, Count, TerrainLabel;
            public readonly RectTransform[] Figures = new RectTransform[10];
            public readonly Graphic[][] FigureParts = new Graphic[10][];
            public readonly Color[][] FigureColors = new Color[10][];
            public readonly float[] HitTime = new float[10];   // first impact on this slot (or -1)
            public readonly bool[] Lost = new bool[10];
            public int Before, After;
            public bool Left, Air;
            public Sil Sil;
            public Vector2 SceneCenter;
            public float W = SceneW;
        }

        sealed class Shot
        {
            public int From;            // 0 = attacker side fires, 1 = defender side
            public int Shooter, Target;
            public float Fire, Hit;
            public bool Indirect;
            public Image Proj, Flash, Blast;
        }

        RectTransform battleRt, battleFx, battleGap;
        CanvasGroup battleGroup;
        Side[] sides;
        TextMeshProUGUI battleMid;
        readonly List<Shot> shots = new List<Shot>();
        readonly List<Image> projPool = new List<Image>(), flashPool = new List<Image>(), blastPool = new List<Image>();
        float battleT = -1, battleScale = 1, battleEnd = 2;

        const float SceneW = 624, SceneH = 380, SceneY = -46, HeaderH = 84, SkyH = 70;
        const float DirectGap = 16, IndirectGap = 160;
        const float FadeIn = 0.22f, FadeOut = 0.3f, FigureScale = 1.35f;

        public bool BattlePlaying => battleT >= 0;

        void BuildBattle()
        {
            var go = new GameObject("BattleScene", typeof(RectTransform), typeof(CanvasGroup));
            battleRt = (RectTransform)go.transform; battleRt.SetParent(canvasRt, false);
            Pin(battleRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(SceneW * 2 + 40, SceneH + HeaderH + 40));
            battleGroup = go.GetComponent<CanvasGroup>(); battleGroup.blocksRaycasts = false; battleGroup.interactable = false;

            var frame = Panel("Frame", battleRt, Plate); Fill(frame, 0, 0, 0, 0);
            Edge(frame, Brass);

            sides = new[] { BuildSide(true), BuildSide(false) };
            battleGap = Panel("Gap", battleRt, Hex("#050606"));
            Pin(battleGap, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, SceneY), new Vector2(IndirectGap, SceneH));
            battleGap.gameObject.SetActive(false);

            battleMid = Label("Mid", battleRt, SizeBody, Brass, TextAlignmentOptions.Center, true);
            Pin(battleMid.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(240, 56), pivot: new Vector2(0.5f, 1));
            battleMid.textWrappingMode = TextWrappingModes.Normal;

            // effects layer over both scenes, clipped to them (shells lobbed upward leave through the top)
            var fx = new GameObject("Fx", typeof(RectTransform), typeof(RectMask2D));
            battleFx = (RectTransform)fx.transform; battleFx.SetParent(battleRt, false);
            Pin(battleFx, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, SceneY), new Vector2(SceneW * 2 + 16, SceneH));
            for (int i = 0; i < 24; i++)
            {
                projPool.Add(FxImage("Proj", Paper));
                flashPool.Add(FxImage("Flash", Hex("#FFE38A")));
                blastPool.Add(FxImage("Blast", Hex("#FF9A3C")));
            }

            foreach (var g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            go.SetActive(false);
        }

        Image FxImage(string name, Color c)
        {
            var img = Panel(name, battleFx, c).GetComponent<Image>();
            img.raycastTarget = false;
            img.gameObject.SetActive(false);
            return img;
        }

        Side BuildSide(bool left)
        {
            var s = new Side { Left = left };
            float cx = (left ? -1 : 1) * (SceneW / 2 + 8);
            s.SceneCenter = new Vector2(cx, SceneY);

            // header: army tab, unit name, machine count
            s.Tab = Panel("Tab", battleRt, Color.white).GetComponent<Image>();
            Pin((RectTransform)s.Tab.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx - (left ? 1 : -1) * (SceneW / 2 - 6), -26), new Vector2(12, 44), pivot: new Vector2(0.5f, 1));
            s.Name = Label("Name", battleRt, SizeTitle, Paper, left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight, true);
            Pin(s.Name.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx + (left ? 24 : -24), -26), new Vector2(SceneW - 40, 48), pivot: new Vector2(0.5f, 1));
            s.Count = Label("Count", battleRt, 52, Paper, left ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, true);
            Pin(s.Count.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx + (left ? -40 : 40), -18), new Vector2(SceneW - 120, 64), pivot: new Vector2(0.5f, 1));

            // scene: sky + ground, clipped
            var sc = new GameObject(left ? "AttackerScene" : "DefenderScene", typeof(RectTransform), typeof(RectMask2D));
            s.Scene = (RectTransform)sc.transform; s.Scene.SetParent(battleRt, false);
            Pin(s.Scene, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), s.SceneCenter, new Vector2(SceneW, SceneH));
            s.Sky = Panel("Sky", s.Scene, Hex("#1B2A33")).GetComponent<Image>(); Fill((RectTransform)s.Sky.transform, 0, 0, 0, 0);
            var horizon = Panel("Haze", s.Scene, new Color(1, 1, 1, 0.06f));
            Pin(horizon, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -SkyH + 24), new Vector2(0, 24), pivot: new Vector2(0.5f, 0), stretchX: true);
            s.Ground = Panel("Ground", s.Scene, Color.white).GetComponent<Image>();
            Pin((RectTransform)s.Ground.transform, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, SceneH - SkyH), pivot: new Vector2(0.5f, 0), stretchX: true);
            var groundEdge = Panel("GroundEdge", s.Ground.transform, new Color(0, 0, 0, 0.25f));
            Pin(groundEdge, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 4), pivot: new Vector2(0.5f, 1), stretchX: true);
            s.TerrainLabel = Label("Terrain", s.Scene, SizeSmall, Paper, left ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.BottomRight, true);
            Pin(s.TerrainLabel.rectTransform, new Vector2(left ? 0 : 1, 0), new Vector2(left ? 0 : 1, 0), new Vector2(left ? 14 : -14, 10), new Vector2(300, 30), pivot: new Vector2(left ? 0 : 1, 0));

            var units = new GameObject("Units", typeof(RectTransform));
            s.Units = (RectTransform)units.transform; s.Units.SetParent(s.Scene, false);
            Pin(s.Units, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SceneW, SceneH));
            return s;
        }

        // ---------------- figures ----------------

        static Sil SilFor(UnitDef d)
        {
            switch (d.Id)
            {
                case "INF": return Sil.Soldier;
                case "MECH": return Sil.Bazooka;
                case "TANK_A": return Sil.HeavyTank;
                case "TANK_B": return Sil.Tank;
                case "APC": case "SUP": return Sil.Truck;
                case "ART_A": case "ART_B": return Sil.Artillery;
                case "AAM": return Sil.Rocket;
                case "AAG": return Sil.AntiAir;
                case "BMB": return Sil.Bomber;
                case "HELI": return Sil.Heli;
                case "LANDER": return Sil.Lander;
                case "BSHIP": return Sil.Ship;
            }
            if (d.MoveClass == MoveClass.Foot) return Sil.Soldier;
            if (d.MoveClass == MoveClass.Air) return Sil.Jet;
            if (d.MoveClass == MoveClass.Ship) return Sil.Ship;
            return d.IsIndirect ? Sil.Artillery : Sil.Tank;
        }

        /// <summary>Muzzle offset (facing right) for each silhouette.</summary>
        static Vector2 Muzzle(Sil k)
        {
            switch (k)
            {
                case Sil.Soldier: return new Vector2(22, 4);
                case Sil.Bazooka: return new Vector2(22, 14);
                case Sil.Tank: return new Vector2(36, 6);
                case Sil.HeavyTank: return new Vector2(42, 8);
                case Sil.Artillery: return new Vector2(30, 26);
                case Sil.Rocket: return new Vector2(22, 26);
                case Sil.AntiAir: return new Vector2(18, 30);
                case Sil.Jet: case Sil.Bomber: return new Vector2(28, -4);
                case Sil.Heli: return new Vector2(24, -6);
                case Sil.Ship: case Sil.Lander: return new Vector2(32, 12);
            }
            return new Vector2(24, 4);
        }

        void BuildFigure(Side s, int slot, Color army)
        {
            if (s.Figures[slot] != null) Destroy(s.Figures[slot].gameObject);
            var root = new GameObject("Fig" + slot, typeof(RectTransform));
            var rt = (RectTransform)root.transform; rt.SetParent(s.Units, false);
            rt.sizeDelta = new Vector2(80, 60);
            var dark = Color.Lerp(army, Color.black, 0.45f);
            var metal = Hex("#2A2E2A");
            var parts = new List<(Graphic g, Color c)>();
            void Box(float x, float y, float w, float h, Color c, float rot = 0)
            {
                var p = Panel("P", rt, c);
                p.anchorMin = p.anchorMax = p.pivot = new Vector2(0.5f, 0.5f);
                p.sizeDelta = new Vector2(w, h); p.anchoredPosition = new Vector2(x, y);
                p.localRotation = Quaternion.Euler(0, 0, rot);
                var g = p.GetComponent<Image>(); g.raycastTarget = false;
                parts.Add((g, c));
            }
            switch (s.Sil)
            {
                case Sil.Soldier:
                case Sil.Bazooka:
                    Box(-2, -16, 6, 14, dark); Box(4, -16, 6, 14, dark);            // legs
                    Box(0, 0, 16, 22, army);                                         // body
                    Box(0, 17, 12, 12, Hex("#E7C9A0"));                              // face
                    Box(0, 22, 16, 6, dark);                                         // helmet
                    if (s.Sil == Sil.Soldier) Box(12, 4, 22, 4, metal);              // rifle
                    else Box(8, 14, 30, 8, metal);                                    // launcher on the shoulder
                    break;
                case Sil.Tank:
                case Sil.HeavyTank:
                {
                    float k = s.Sil == Sil.HeavyTank ? 1.18f : 1f;
                    Box(0, -16 * k, 60 * k, 10 * k, metal);                            // tracks
                    Box(0, -6 * k, 56 * k, 14 * k, army);                              // hull
                    Box(-4 * k, 6 * k, 26 * k, 12 * k, dark);                          // turret
                    Box(20 * k, 7 * k, 28 * k, 5 * k, metal);                          // gun
                    break;
                }
                case Sil.Artillery:
                    Box(0, -16, 58, 10, metal);
                    Box(-4, -6, 50, 14, army);
                    Box(-8, 6, 20, 12, dark);
                    Box(14, 16, 40, 6, metal, 35);                                    // raised barrel
                    break;
                case Sil.Rocket:
                    Box(0, -16, 58, 10, metal);
                    Box(0, -6, 54, 14, army);
                    Box(6, 14, 34, 14, dark, 30);                                     // launcher box
                    break;
                case Sil.AntiAir:
                    Box(0, -16, 58, 10, metal);
                    Box(0, -6, 54, 14, army);
                    Box(-4, 6, 20, 12, dark);
                    Box(8, 20, 30, 4, metal, 60); Box(14, 18, 30, 4, metal, 60);     // twin guns
                    break;
                case Sil.Truck:
                    Box(-12, -18, 12, 12, metal); Box(16, -18, 12, 12, metal);       // wheels
                    Box(-6, -2, 40, 24, army);                                        // cargo body
                    Box(22, -6, 16, 16, dark);                                        // cab
                    break;
                case Sil.Jet:
                case Sil.Bomber:
                {
                    float k = s.Sil == Sil.Bomber ? 1.25f : 1f;
                    Box(0, 0, 60 * k, 10 * k, army);                                   // fuselage
                    Box(-4 * k, -2 * k, 26 * k, 8 * k, dark, -20);                     // wing
                    Box(-26 * k, 8 * k, 12 * k, 10 * k, dark, 20);                     // tail fin
                    Box(24 * k, 2 * k, 10 * k, 6 * k, Hex("#9FD3F0"));                 // canopy
                    break;
                }
                case Sil.Heli:
                    Box(0, 0, 38, 18, army);
                    Box(-26, 4, 30, 5, dark);
                    Box(0, 14, 4, 8, metal);
                    Box(0, 18, 60, 3, metal);                                         // rotor
                    Box(14, 2, 10, 8, Hex("#9FD3F0"));
                    break;
                case Sil.Ship:
                case Sil.Lander:
                    Box(0, -12, 66, 14, army);                                        // hull
                    Box(-6, 2, 24, 14, dark);                                         // bridge
                    if (s.Sil == Sil.Ship) Box(20, 8, 26, 5, metal);                  // gun
                    else Box(26, -2, 12, 10, dark);                                   // ramp
                    break;
            }
            s.Figures[slot] = rt;
            s.FigureParts[slot] = new Graphic[parts.Count];
            s.FigureColors[slot] = new Color[parts.Count];
            for (int i = 0; i < parts.Count; i++) { s.FigureParts[slot][i] = parts[i].g; s.FigureColors[slot][i] = parts[i].c; }
            rt.localScale = new Vector3(s.Left ? FigureScale : -FigureScale, FigureScale, 1);
        }

        /// <summary>Slot position inside the scene. Slot 0 is the rear; the highest slots stand nearest the enemy.</summary>
        /// <remarks>
        /// As in the original's battle screen, each side stands in two files of five, one above the other:
        /// a small unit fills the front file first; the highest slots (the first to fall) stand in front.
        /// </remarks>
        static Vector2 SlotPos(Side s, int slot)
        {
            int rank = Mathf.Max(0, s.Before - 1 - slot);    // highest slot (first to fall) = front file, top row
            int file = rank < 5 ? 1 : 0, row = rank % 5;
            float inner = s.W / 2;
            float x = inner - (file == 1 ? 96 : 236) - (row % 2 == 1 ? 34 : 0);
            float y = SceneH / 2 - SkyH - 30 - row * 58;
            if (s.Air) y += 34;
            if (!s.Left) x = -x;
            return new Vector2(x, y);
        }

        Vector2 SlotFx(Side s, int slot) => s.SceneCenter - new Vector2(0, SceneY) + SlotPos(s, slot);

        Vector2 MuzzleFx(Side s, int slot)
        {
            var m = Muzzle(s.Sil) * FigureScale;
            if (!s.Left) m.x = -m.x;
            return SlotFx(s, slot) + m;
        }

        // ---------------- play ----------------

        /// <param name="timeScale">Seconds-per-unit of the timeline (GameSettings.BattleTime).</param>
        public void PlayBattle(BattleSetup b, float timeScale)
        {
            var e = b.Event;
            SetupSide(sides[0], b.AttName, b.AttArmy, b.AttDef, b.AttTerrain, e.AttackerCountBefore, e.AttackerCountAfter);
            SetupSide(sides[1], b.DefName, b.DefArmy, b.DefDef, b.DefTerrain, e.DefenderCountBefore, e.DefenderCountAfter);
            bool indirect = b.AttDef.IsIndirect;
            float gap = indirect ? IndirectGap : DirectGap;
            float w = (SceneW * 2 + DirectGap - gap) / 2;
            for (int si = 0; si < 2; si++)
            {
                var sd = sides[si];
                sd.W = w;
                sd.SceneCenter = new Vector2((sd.Left ? -1 : 1) * (gap / 2 + w / 2), SceneY);
                sd.Scene.sizeDelta = new Vector2(w, SceneH);
                sd.Scene.anchoredPosition = sd.SceneCenter;
                sd.Units.sizeDelta = new Vector2(w, SceneH);
            }
            battleGap.gameObject.SetActive(indirect);
            battleMid.text = indirect ? "間接攻撃" : e.Counter ? "同時攻撃" : "<color=#A3AC92>反撃なし</color>";

            // schedule shots: everyone who stands fires once; rounds go to the doomed first so each loss gets its hit
            foreach (var sh in shots) { sh.Proj.gameObject.SetActive(false); sh.Flash.gameObject.SetActive(false); sh.Blast.gameObject.SetActive(false); }
            shots.Clear();
            int pool = 0;
            float last = 0;
            void Volley(int from, float start, bool lob)
            {
                var me = sides[from]; var them = sides[1 - from];
                if (them.Before <= 0) return;
                var order = new List<int>();
                for (int i = them.Before - 1; i >= them.After; i--) order.Add(i);   // lost ones (front first)
                for (int i = them.After - 1; i >= 0; i--) order.Add(i);
                for (int k = 0; k < me.Before && pool < projPool.Count; k++)
                {
                    int shooter = me.Before - 1 - k;                               // front rank fires first
                    var sh = new Shot { From = from, Shooter = shooter, Target = order[k % order.Count], Indirect = lob };
                    sh.Fire = start + k * 0.05f;
                    sh.Hit = sh.Fire + (lob ? 0.85f : 0.34f);
                    sh.Proj = projPool[pool]; sh.Flash = flashPool[pool]; sh.Blast = blastPool[pool]; pool++;
                    var proj = sh.Proj.rectTransform;
                    proj.sizeDelta = ProjSize(me.Sil);
                    sh.Proj.color = me.Sil == Sil.Soldier || me.Sil == Sil.Heli ? Hex("#FFE9A8") : Paper;
                    sh.Flash.rectTransform.sizeDelta = new Vector2(18, 12);
                    sh.Blast.rectTransform.sizeDelta = new Vector2(28, 28);
                    shots.Add(sh);
                    if (them.HitTime[sh.Target] < 0 || sh.Hit < them.HitTime[sh.Target]) them.HitTime[sh.Target] = sh.Hit;
                    last = Mathf.Max(last, sh.Hit);
                }
            }
            Volley(0, 0.35f, indirect);
            if (e.Counter) Volley(1, 0.42f, false);
            // fewer rounds than losses (2 machines can still wipe out 5): the rest go down with the last impact
            for (int si = 0; si < 2; si++)
            {
                float lastOn = -1;
                foreach (var sh in shots) if (sh.From != si) lastOn = Mathf.Max(lastOn, sh.Hit);
                for (int i = 0; i < 10; i++) if (sides[si].Lost[i] && sides[si].HitTime[i] < 0) sides[si].HitTime[i] = lastOn < 0 ? 0.8f : lastOn;
            }
            battleEnd = last + 0.6f + FadeOut;

            battleScale = Mathf.Max(0.05f, timeScale);
            battleT = 0;
            battleGroup.alpha = 0;
            battleRt.SetAsLastSibling();
            battleRt.gameObject.SetActive(true);
            ApplyBattleFrame(0);
        }

        static Vector2 ProjSize(Sil k)
        {
            switch (k)
            {
                case Sil.Soldier: case Sil.Heli: return new Vector2(9, 3);
                case Sil.Bazooka: return new Vector2(16, 6);
                case Sil.Artillery: case Sil.Rocket: case Sil.Ship: return new Vector2(12, 12);
                case Sil.AntiAir: return new Vector2(8, 4);
                case Sil.Jet: case Sil.Bomber: return new Vector2(18, 5);
            }
            return new Vector2(14, 6);
        }

        void SetupSide(Side s, string name, Army army, UnitDef def, TerrainDef ter, int before, int after)
        {
            s.Before = Mathf.Clamp(before, 0, 10); s.After = Mathf.Clamp(after, 0, s.Before);
            s.Sil = SilFor(def);
            s.Air = def.Domain == Domain.Air;
            s.Tab.color = ArmyColor(army);
            s.Name.text = name;
            s.Name.color = ArmyTextColor(army);
            ColorUtility.TryParseHtmlString(ter != null ? ter.Color : "#9ccc65", out var gc);
            if (def.Domain == Domain.Sea && ter != null && ter.Id != "SEA") ColorUtility.TryParseHtmlString("#2f6fb3", out gc);
            s.Ground.color = Color.Lerp(gc, Color.black, 0.15f);
            s.TerrainLabel.text = ter != null ? Labels.Get(ter.NameKey) + (def.Domain != Domain.Air ? "<color=#EFE6CCAA>  防御 " + ter.DefenseFor(def) + "</color>" : "") : "";
            var col = ArmyColor(army);
            for (int i = 0; i < 10; i++)
            {
                s.HitTime[i] = -1;
                s.Lost[i] = i >= s.After && i < s.Before;
                if (i < s.Before)
                {
                    BuildFigure(s, i, col);
                    s.Figures[i].anchoredPosition = SlotPos(s, i);
                    s.Figures[i].gameObject.SetActive(true);
                }
                else if (s.Figures[i] != null) s.Figures[i].gameObject.SetActive(false);
            }
            // draw the rear rank first so the front rank overlaps it
            // lower rows are nearer the viewer: draw them last
            var order = new List<int>(); for (int i = 0; i < s.Before; i++) order.Add(i);
            order.Sort((x, y) => SlotPos(s, y).y.CompareTo(SlotPos(s, x).y));
            foreach (var i in order) s.Figures[i].SetAsLastSibling();
        }

        public void SkipBattle()
        {
            if (battleT < 0) return;
            battleT = -1;
            battleRt.gameObject.SetActive(false);
        }

        void UpdateBattle()
        {
            if (battleT < 0) return;
            battleT += Time.unscaledDeltaTime / battleScale;
            if (battleT >= battleEnd) { SkipBattle(); return; }
            ApplyBattleFrame(battleT);
        }

        void ApplyBattleFrame(float t)
        {
            float fadeStart = battleEnd - FadeOut;
            battleGroup.alpha = t < FadeIn ? t / FadeIn : t > fadeStart ? Mathf.Clamp01(1 - (t - fadeStart) / FadeOut) : 1;

            for (int si = 0; si < 2; si++)
            {
                var s = sides[si];
                int shown = s.Before;
                for (int i = 0; i < s.Before; i++)
                {
                    var fig = s.Figures[i];
                    var basePos = SlotPos(s, i);
                    float hit = s.HitTime[i];
                    bool dead = s.Lost[i] && hit >= 0 && t >= hit + 0.14f;
                    if (s.Lost[i] && hit >= 0 && t >= hit) shown--;
                    fig.gameObject.SetActive(!dead);
                    if (dead) continue;
                    var off = Vector2.zero;
                    if (s.Air) off.y = Mathf.Sin((t * 3.2f + i * 0.7f)) * 4;
                    // recoil when this figure fires
                    foreach (var sh in shots)
                        if (sh.From == si && sh.Shooter == i && t >= sh.Fire && t < sh.Fire + 0.12f)
                            off.x += (s.Left ? -1 : 1) * 5 * (1 - (t - sh.Fire) / 0.12f);
                    // shake when struck; a doomed machine flashes white just before it blows up
                    bool flash = s.Lost[i] && hit >= 0 && t >= hit;
                    foreach (var sh in shots)
                        if (sh.From != si && sh.Target == i && t >= sh.Hit && t < sh.Hit + 0.2f)
                            off.x += Mathf.Sin((t - sh.Hit) * 90) * 3;
                    fig.anchoredPosition = basePos + off;
                    var parts = s.FigureParts[i]; var cols = s.FigureColors[i];
                    for (int p = 0; p < parts.Length; p++) parts[p].color = flash ? Color.white : cols[p];
                }
                s.Count.text = "<size=55%>×</size>" + Mathf.Max(0, shown);
                s.Count.color = shown <= 0 ? Danger : shown < s.Before ? Hex("#FFD27A") : Paper;
            }

            foreach (var sh in shots) DrawShot(sh, t);
        }

        void DrawShot(Shot sh, float t)
        {
            var me = sides[sh.From]; var them = sides[1 - sh.From];
            var from = MuzzleFx(me, sh.Shooter);
            var to = SlotFx(them, sh.Target) + new Vector2(0, 4);

            // muzzle flash
            bool flashOn = t >= sh.Fire && t < sh.Fire + 0.09f;
            sh.Flash.gameObject.SetActive(flashOn);
            if (flashOn) sh.Flash.rectTransform.anchoredPosition = from;

            // projectile
            bool flying = t >= sh.Fire && t < sh.Hit;
            var pr = sh.Proj.rectTransform;
            if (flying)
            {
                if (!sh.Indirect)
                {
                    float u = (t - sh.Fire) / (sh.Hit - sh.Fire);
                    var p = Vector2.Lerp(from, to, u);
                    p.y += Mathf.Sin(u * Mathf.PI) * 18;
                    pr.anchoredPosition = p;
                    pr.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg);
                    sh.Proj.gameObject.SetActive(true);
                }
                else
                {
                    // up and out of the top of our scene, then down onto the target from above
                    float up = 0.32f, gap = 0.18f;
                    float e = t - sh.Fire;
                    float top = SceneH / 2 + 40;
                    if (e < up)
                    {
                        float u = e / up;
                        var dir = me.Left ? 1 : -1;
                        pr.anchoredPosition = from + new Vector2(dir * 90 * u, (top - from.y) * u);
                        pr.localRotation = Quaternion.Euler(0, 0, me.Left ? 70 : 110);
                        sh.Proj.gameObject.SetActive(true);
                    }
                    else if (e < up + gap) sh.Proj.gameObject.SetActive(false);
                    else
                    {
                        float u = Mathf.Clamp01((e - up - gap) / (sh.Hit - sh.Fire - up - gap));
                        pr.anchoredPosition = Vector2.Lerp(new Vector2(to.x + (me.Left ? -60 : 60), top), to, u * u);
                        pr.localRotation = Quaternion.Euler(0, 0, me.Left ? -70 : -110);
                        sh.Proj.gameObject.SetActive(true);
                    }
                }
            }
            else sh.Proj.gameObject.SetActive(false);

            // impact: a burst, larger and smokier when it destroys a machine
            float bt = t - sh.Hit;
            bool kill = them.Lost[sh.Target] && Mathf.Approximately(them.HitTime[sh.Target], sh.Hit);
            float dur = kill ? 0.5f : 0.22f;
            bool blastOn = bt >= 0 && bt < dur;
            sh.Blast.gameObject.SetActive(blastOn);
            if (blastOn)
            {
                float u = bt / dur;
                var br = sh.Blast.rectTransform;
                br.anchoredPosition = to + (kill ? new Vector2(0, u * 14) : Vector2.zero);
                float size = (kill ? 54 : 22) * (0.4f + u);
                br.sizeDelta = new Vector2(size, size);
                br.localRotation = Quaternion.Euler(0, 0, 45 * u);
                var c = Color.Lerp(Hex("#FFE38A"), kill ? Hex("#5A5148") : Hex("#FF9A3C"), u);
                c.a = 1 - u * u;
                sh.Blast.color = c;
            }
        }
    }
}
