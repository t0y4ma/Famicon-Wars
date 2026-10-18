using System.Collections.Generic;
using System.Linq;
using FamiconWars.Core;
using FamiconWars.Net;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// 内政 on the controller side: which facilities this screen may work on, the facility window,
    /// and the 内政 time after a COM's phase (offline it ends with 「内政終了」 or when the time runs out).
    /// </summary>
    public partial class GameController
    {
        int domX = -1, domY = -1;
        readonly int[] comDomesticKey = new int[Armies.Max];

        /// <summary>Offline: each COM does its 内政 once per window it is open to it (rule based, DomesticAi).</summary>
        void ComDomestic()
        {
            if (online || replaying || state == null || !state.Domestic || state.GameOver) return;
            int key = state.PhaseKey * 2 + (state.Interlude ? 1 : 0) + 1;     // +1: 0 stays "never" for a new match
            for (int a = 0; a < state.Players; a++)
            {
                if (ai[a] == null || comDomesticKey[a] == key || !RulesEngine.DomesticOpen(state, (Army)a)) continue;
                comDomesticKey[a] = key;
                foreach (var c in DomesticAi.Plan(state, (Army)a))
                {
                    var r = ApplyRecorded(c);
                    if (r.Ok) Report(r, null);
                }
                board.Refresh();
                RefreshHud();
            }
        }
        int interludeKey = -1;
        float interludeLeft = -1;

        /// <summary>This screen plays that army (offline: a person, not a COM; online: our seat).</summary>
        bool CanActFor(Army a)
        {
            if (a == Army.None || replaying || state == null || (int)a >= state.Players) return false;
            return online ? (int)a == myArmy : ai[(int)a] == null;
        }

        /// <summary>Offline: a person takes part, so a COM's phase is followed by a 内政 time.</summary>
        bool WantsInterlude()
        {
            if (online || replaying || !state.Domestic) return false;
            for (int a = 0; a < state.Players; a++) if (!state.IsOut((Army)a) && ai[a] == null) return true;
            return false;
        }

        string FacilityAt(int x, int y) => Labels.Get(state.TerrainAt(x, y).NameKey) + "(" + x + "," + y + ")";

        string ReserveText(int kind, int x, int y)
        {
            if (kind == GameState.ReserveLevelUp) return "レベル" + (state.LevelAt(x, y) + 1) + "へ";
            int t = kind - GameState.ReserveConvertBase;
            return t >= 0 && t < data.Terrains.Count ? Labels.Get(data.Terrains[t].NameKey) + "へ転向" : "";
        }

        /// <summary>Opens the 内政 window for a facility this screen may work on now. false: not one of those.</summary>
        bool OpenDomestic(int x, int y)
        {
            if (!InMatch || state == null || !state.Domestic || state.GameOver || !state.InBounds(x, y)) return false;
            var t = state.TerrainAt(x, y);
            if (!t.IsProperty) return false;
            int i = state.Index(x, y);
            var a = state.Owner[i];
            if (!CanActFor(a)) return false;
            if (!RulesEngine.DomesticOpen(state, a))
            {
                hud.Toast(state.Data.Economy.Timing == DomesticTiming.InterludeOnly ? "内政は内政の時間にだけできます" : "自分の手番中は内政できません", true);
                return false;
            }
            domX = x; domY = y;
            var e = state.Data.Economy;
            int lv = state.Level[i];
            string title = Labels.Get(t.NameKey) + " <size=70%>Lv" + lv + "</size>  <size=55%><color=#A3AC92>" + Labels.Army(a) + "  資金 " + state.Funds[(int)a].ToString("N0") + "</color></size>";
            var info = new List<string>();
            int inc = RulesEngine.Income(state, i);
            if (t.Income > 0)
                info.Add("収入 " + inc.ToString("N0") + "/日" + (lv < e.MaxLevel ? "  →  Lv" + (lv + 1) + "で " + e.IncomeFor(t.Income, lv + 1).ToString("N0") + "/日" : ""));
            if (t.Id == "HQ")
            {
                string R(int r) => r < 0 ? "どこでも" : r + "マス";
                info.Add("生産できる範囲 " + R(e.RadiusFor(lv)) + (lv < e.MaxLevel ? "  →  " + R(e.RadiusFor(lv + 1)) : ""));
            }
            else if (t.Produces != null && t.Id != e.ConvertFrom && lv < e.MaxLevel)
            {
                var next = RulesEngine.ProductionTable(state, x, y).Where(p => p.need == lv + 1).Select(p => Labels.Get(p.def.NameKey)).ToList();
                if (next.Count > 0) info.Add("Lv" + (lv + 1) + "で生産可: " + string.Join("・", next));
                if (e.FreeActLevel > 0 && e.FreeActTerrains.Contains(t.Id) && lv + 1 == e.FreeActLevel) info.Add("Lv" + e.FreeActLevel + ": 手番で最初に作った部隊がすぐ動ける");
            }
            if (state.Reserve[i] != 0)
                info.Add("<color=#D8A23A>予約中: " + ReserveText(state.Reserve[i], x, y) + "</color>");

            var items = new List<GameHud.DomesticItem>();
            if (state.Reserve[i] != 0)
            {
                bool canUndo = state.LastReserve[(int)a] == i;
                items.Add(new GameHud.DomesticItem
                {
                    Label = "予約を取り消す", Enabled = canUndo,
                    Note = canUndo ? state.ReservePaid[i].ToString("N0") + " 返金" : "直前の予約だけ取り消せます",
                    Act = () => DomesticTry(new CancelReserveCommand { Army = a })
                });
            }
            else
            {
                if (lv < e.MaxLevel)
                {
                    var err = RulesEngine.CheckReserve(state, a, x, y, null, out int cost);
                    items.Add(new GameHud.DomesticItem
                    {
                        Label = "レベル" + (lv + 1) + "にする", Primary = true, Enabled = err == null,
                        Note = err == null ? cost.ToString("N0") : err,
                        Act = () => DomesticTry(new LevelUpCommand { Army = a, X = x, Y = y })
                    });
                }
                if (t.Id == e.ConvertFrom)
                    foreach (var kv in e.ConvertCost)
                    {
                        string target = kv.Key;
                        TerrainDef to;
                        try { to = data.Terrain(target); } catch (KeyNotFoundException) { continue; }
                        var err = RulesEngine.CheckReserve(state, a, x, y, target, out int cost);
                        items.Add(new GameHud.DomesticItem
                        {
                            Label = Labels.Get(to.NameKey) + "に転向", Enabled = err == null,
                            Note = err == null ? kv.Value.ToString("N0") + "  Lv→" + System.Math.Max(1, lv - e.ConvertLevelDrop) : err,
                            Act = () => DomesticTry(new ConvertCommand { Army = a, X = x, Y = y, Target = target })
                        });
                    }
            }
            hud.ShowDomestic(title, string.Join("\n", info), items);
            return true;
        }

        /// <summary>After any move: the open window follows the game (closes when it no longer applies).</summary>
        void RefreshDomestic()
        {
            if (!hud.DomesticVisible) return;
            if (!OpenDomestic(domX, domY)) hud.HideDomestic();
        }

        /// <summary>A 内政 command: never waits for the turn (it is not this army's phase).</summary>
        bool DomesticTry(Command c)
        {
            if (online)
            {
                var err = RulesEngine.Check(state, c);
                if (err != null) { hud.Toast(err, true); return false; }
                FwClient.Play(c);
                return true;
            }
            var r = ApplyRecorded(c);
            if (!r.Ok) { hud.Toast(r.Error, true); return false; }
            Report(r, null);
            board.Refresh();
            RefreshHud();
            return true;
        }

        /// <summary>The plate of the 内政 time (and offline: its clock and the end of it).</summary>
        void UpdateInterlude()
        {
            if (state == null || !state.Interlude || state.GameOver || replaying || mode == Mode.GameOver)
            {
                hud.ShowInterlude(false);
                interludeKey = -1;
                return;
            }
            int limit = online ? onlineTurnSeconds : settings.TurnSeconds;
            if (interludeKey != state.PhaseKey) { interludeKey = state.PhaseKey; interludeLeft = limit; }
            string clock = "";
            if (limit > 0)
            {
                if (online || (!hud.HowToOverMatch && !hud.MatchupVisible && !hud.SoundPanelVisible)) interludeLeft -= Time.unscaledDeltaTime;
                int secs = Mathf.Max(0, Mathf.CeilToInt(interludeLeft));
                clock = "  <color=" + (secs <= 10 ? "#F07A62" : "#EFE6CC") + ">" + secs / 60 + ":" + (secs % 60).ToString("00") + "</color>";
            }
            if (online) { UpdateOnlineInterlude(clock); return; }
            hud.ShowInterlude(true, "<b>内政の時間</b>" + clock + "  <size=85%><color=#A3AC92>自軍の施設をクリックして予約</color></size>", "内政終了", true,
                () => EndInterludeOffline());
            if (limit > 0 && interludeLeft <= 0) EndInterludeOffline();
        }

        void EndInterludeOffline()
        {
            if (online || state == null || !state.Interlude) return;
            hud.HideDomestic();
            var r = ApplyRecorded(new EndInterludeCommand { Army = state.Active });
            if (!r.Ok) return;
            Report(r, null);
            board.Refresh();
            RefreshHud();
        }

        /// <summary>Online (the server ends the 内政 time): the count of who pressed 「内政終了」.</summary>
        void UpdateOnlineInterlude(string clock)
        {
            int ready = 0, people = 0;
            for (int a = 0; a < state.Players; a++)
            {
                if (state.IsOut((Army)a) || !SeatIsPerson(a)) continue;
                people++;
                if (state.InterludeReady[a]) ready++;
            }
            bool mine = myArmy >= 0 && myArmy < state.Players && !state.IsOut((Army)myArmy);
            bool pressed = mine && state.InterludeReady[myArmy];
            hud.ShowInterlude(true, "<b>内政の時間</b>" + clock + "  <size=85%><color=#A3AC92>内政終了 " + ready + "/" + people + "</color></size>",
                mine ? (pressed ? "待っています" : "内政終了") : null, mine && !pressed,
                () => DomesticTry(new InterludeDoneCommand { Army = (Army)myArmy }));
        }

        bool SeatIsPerson(int a) => room.HasValue && room.Value.seatKinds != null && a < room.Value.seatKinds.Length && (SeatKind)room.Value.seatKinds[a] == SeatKind.Human;
    }
}
