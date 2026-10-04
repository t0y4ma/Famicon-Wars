using System.Diagnostics;
using System.IO;
using FamiconWars.Core;
using NUnit.Framework;
using UnityEngine;

namespace FamiconWars.Tests
{
    public class AiTests
    {
        static GameData Data()
        {
            var dir = Path.Combine(Application.dataPath, "Resources/Data");
            return GameData.Load(File.ReadAllText(Path.Combine(dir, "units.csv")), File.ReadAllText(Path.Combine(dir, "terrains.csv")),
                File.ReadAllText(Path.Combine(dir, "damage.csv")), File.ReadAllText(Path.Combine(dir, "rules.csv")));
        }

        public struct Result { public Army Winner; public int Day, Commands, Rejected; public long Ms; public long MaxStepMs; public string Reason; }

        /// <summary>Plays COM vs COM on map01 until someone wins or maxDays passes.</summary>
        public static Result Play(int redLevel, int blueLevel, uint seed, int maxDays)
            => Play(AiProfile.ForLevel(redLevel), AiProfile.ForLevel(blueLevel), seed, maxDays);

        public static Result Play(AiProfile red, AiProfile blue, uint seed, int maxDays)
        {
            var g = Data();
            var map = MapDef.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "Resources/Maps/map01.txt")), g);
            var s = GameState.Create(g, map, seed);
            RulesEngine.StartGame(s);
            var ais = new[] { new AiPlayer(Army.Red, red, seed * 7 + 1), new AiPlayer(Army.Blue, blue, seed * 13 + 5) };
            var res = new Result { Winner = Army.None };
            var total = Stopwatch.StartNew();
            while (!s.GameOver && s.Day <= maxDays && res.Commands < 50000)
            {
                var ai = ais[(int)s.Active];
                var sw = Stopwatch.StartNew();
                var c = ai.Next(s);
                res.MaxStepMs = System.Math.Max(res.MaxStepMs, sw.ElapsedMilliseconds);
                Assert.IsNotNull(c, "COM returned no command during its own phase");
                var r = RulesEngine.Apply(s, c);
                res.Commands++;
                if (!r.Ok) { res.Rejected++; ai.Rejected(c); }
                foreach (var e in r.Events) if (e is GameOverEvent go) res.Reason = go.Reason;
            }
            res.Ms = total.ElapsedMilliseconds;
            res.Day = s.Day;
            res.Winner = s.Winner;
            if (!s.GameOver)
            {
                // judge by army value when the day limit is hit
                long[] v = new long[2];
                foreach (var u in s.Units) if (u.Army != Army.None) v[(int)u.Army] += (long)s.Def(u).Price * u.Hp / 100;
                for (int i = 0; i < s.Owner.Length; i++) if (s.Owner[i] != Army.None) v[(int)s.Owner[i]] += 2000;
                res.Winner = v[0] == v[1] ? Army.None : v[0] > v[1] ? Army.Red : Army.Blue;
                res.Reason = "判定(" + v[0] + " vs " + v[1] + ")";
            }
            return res;
        }

        [Test]
        public void EveryLevelPlaysThirtyDaysWithoutGettingStuck()
        {
            for (int lv = 1; lv <= 4; lv++)
            {
                var r = Play(lv, lv, 100u + (uint)lv, 30);
                UnityEngine.Debug.Log($"[AI] Lv{lv} mirror: winner={r.Winner} day={r.Day} reason={r.Reason} commands={r.Commands} rejected={r.Rejected} total={r.Ms}ms maxStep={r.MaxStepMs}ms");
                Assert.LessOrEqual(r.Rejected, r.Commands / 20 + 2, "too many refused commands");
                Assert.Less(r.MaxStepMs, 3000, "a single COM step took too long");
            }
        }
        /// <summary>Round robin between levels (both sides, two seeds). Slow: run on demand.</summary>
        [Test, Explicit, Timeout(3600000)]
        public void Balance()
        {
            int[,] wins = new int[5, 5];
            int[,] games = new int[5, 5];
            for (int a = 1; a <= 4; a++)
                for (int b = a + 1; b <= 4; b++)
                    for (uint seed = 1; seed <= 2; seed++)
                    {
                        var r1 = Play(a, b, seed, 80);
                        var r2 = Play(b, a, seed, 80);
                        games[a, b] += 2; games[b, a] += 2;
                        if (r1.Winner == Army.Red) wins[a, b]++; else if (r1.Winner == Army.Blue) wins[b, a]++;
                        if (r2.Winner == Army.Red) wins[b, a]++; else if (r2.Winner == Army.Blue) wins[a, b]++;
                        UnityEngine.Debug.Log($"[BAL] Lv{a}(赤) vs Lv{b}(青): {r1.Winner} {r1.Reason} day{r1.Day} | Lv{b}(赤) vs Lv{a}(青): {r2.Winner} {r2.Reason} day{r2.Day}");
                    }
            for (int a = 1; a <= 4; a++)
                for (int b = a + 1; b <= 4; b++)
                    UnityEngine.Debug.Log($"[BAL] Lv{b} vs Lv{a}: Lv{b} {wins[b, a]}勝 / Lv{a} {wins[a, b]}勝 / {games[a, b]}戦");
        }

        /// <summary>Which evaluation term makes a profile weaker than Lv1? Each variant drops one term from Lv2.</summary>
        [Test, Explicit, Timeout(3600000)]
        public void Ablation()
        {
            var drops = new[] { AiFeature.None, AiFeature.SmartProduction, AiFeature.Threat };
            foreach (var drop in drops)
            {
                int w = 0, l = 0;
                for (uint seed = 1; seed <= 3; seed++)
                {
                    var p = AiProfile.ForLevel(2); p.Features &= ~drop; p.Noise = 0;
                    var r1 = Play(p, AiProfile.ForLevel(1), seed, 60);
                    var r2 = Play(AiProfile.ForLevel(1), p, seed, 60);
                    if (r1.Winner == Army.Red) w++; else if (r1.Winner == Army.Blue) l++;
                    if (r2.Winner == Army.Blue) w++; else if (r2.Winner == Army.Red) l++;
                }
                UnityEngine.Debug.Log($"[ABL] Lv2 without {drop}: {w} wins / {l} losses vs Lv1");
            }
        }

        /// <summary>Logs each army's composition, funds and properties every 5 days (for tuning).</summary>
        [Test, Explicit]
        public void Diagnose()
        {
            var g = Data();
            var map = MapDef.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "Resources/Maps/map01.txt")), g);
            var s = GameState.Create(g, map, 1);
            RulesEngine.StartGame(s);
            var ais = new[] { new AiPlayer(Army.Red, AiProfile.ForLevel(2), 8), new AiPlayer(Army.Blue, AiProfile.ForLevel(1), 18) };
            int lastDay = 0;
            var produced = new int[2]; var blockedPhases = new int[2]; var fresh = new System.Collections.Generic.HashSet<int>();
            while (!s.GameOver && s.Day <= 40)
            {
                var c = ais[(int)s.Active].Next(s);
                var r = RulesEngine.Apply(s, c);
                if (!r.Ok) ais[(int)s.Active].Rejected(c);
                if (r.Ok && c is ProduceCommand) { produced[(int)c.Army]++; foreach (var e in r.Events) if (e is ProducedEvent pe) fresh.Add(pe.UnitId); }
                if (r.Ok && c is EndPhaseCommand)
                {
                    // facilities of the army that just ended, still occupied by its own units
                    int a0 = (int)c.Army, occ = 0;
                    for (int y = 0; y < s.Height; y++) for (int x = 0; x < s.Width; x++)
                    {
                        var t = s.TerrainAt(x, y);
                        if (t.Produces != null && (int)s.Owner[s.Index(x, y)] == a0 && RulesEngine.InProductionRange(s, c.Army, x, y) && s.UnitAt(x, y) != null && (int)s.UnitAt(x, y).Army == a0 && !fresh.Contains(s.UnitAt(x, y).Id) && s.Funds[a0] >= 1000) occ++;
                    }
                    blockedPhases[a0] += occ; fresh.Clear();
                }
                if (s.Day != lastDay && s.Day % 5 == 0 && s.Active == Army.Red)
                {
                    lastDay = s.Day;
                    for (int a = 0; a < 2; a++)
                    {
                        var comp = new System.Collections.Generic.SortedDictionary<string, int>();
                        foreach (var u in s.Units) if ((int)u.Army == a) { comp.TryGetValue(s.Def(u).Id, out var n); comp[s.Def(u).Id] = n + 1; }
                        int props = 0; for (int i = 0; i < s.Owner.Length; i++) if ((int)s.Owner[i] == a) props++;
                        var parts = new System.Collections.Generic.List<string>(); foreach (var kv in comp) parts.Add(kv.Key + "x" + kv.Value);
                        UnityEngine.Debug.Log($"[DIAG] day{s.Day} {(Army)a} Lv{ais[a].Profile.Level}: funds={s.Funds[a]} props={props} produced={produced[a]} blockedFacilityPhases={blockedPhases[a]} units={string.Join(",", parts)}");
                    }
                }
            }
        }

        /// <summary>Weight search for the Lv2 profile against Lv1.</summary>
        [Test, Explicit, Timeout(3600000)]
        public void Tune()
        {
            var configs = new System.Collections.Generic.List<float[]>();
            foreach (var thr in new[] { 0f, 0.3f, 0.6f, 1f }) foreach (var adv in new[] { 1.6f, 2.4f }) configs.Add(new[] { thr, adv, 1f });
            configs.Add(new[] { 0.6f, 1.6f, 0f });
            foreach (var cfg in configs)
            {
                int w = 0, l = 0;
                for (uint seed = 1; seed <= 10; seed++)
                {
                    var p = AiProfile.ForLevel(2); p.Noise = 0; p.WThreat = cfg[0]; p.WAdvance = cfg[1]; if (cfg[2] == 0) p.Features &= ~AiFeature.SmartProduction;
                    var r1 = Play(p, AiProfile.ForLevel(1), seed, 50);
                    var r2 = Play(AiProfile.ForLevel(1), p, seed, 50);
                    if (r1.Winner == Army.Red) w++; else if (r1.Winner == Army.Blue) l++;
                    if (r2.Winner == Army.Blue) w++; else if (r2.Winner == Army.Red) l++;
                }
                UnityEngine.Debug.Log($"[TUNE2] netThreat={cfg[0]} advance={cfg[1]} smartProd={cfg[2]}: {w} wins / {l} losses vs Lv1");
            }
        }

        /// <summary>Every level above 1 should beat the weakest COM.</summary>
        [Test, Explicit, Timeout(3600000)]
        public void VsWeakest()
        {
            for (int lv = 2; lv <= 4; lv++)
            {
                int w = 0, l = 0;
                for (uint seed = 1; seed <= 2; seed++)
                {
                    var r1 = Play(lv, 1, seed, 60);
                    var r2 = Play(1, lv, seed, 60);
                    if (r1.Winner == Army.Red) w++; else if (r1.Winner == Army.Blue) l++;
                    if (r2.Winner == Army.Blue) w++; else if (r2.Winner == Army.Red) l++;
                }
                UnityEngine.Debug.Log($"[VSW] Lv{lv}: {w} wins / {l} losses vs Lv1");
            }
        }


        /// <summary>New production/formation rules against the previous Lv3 behaviour, knob by knob.</summary>
        [Test, Explicit, Timeout(3600000)]
        public void EconomyTune()
        {
            var E = AiFeature.Economy; var F = AiFeature.Formation;
            AiProfile Mk(System.Action<AiProfile> f) { var p = AiProfile.ForLevel(3); p.EcoSave = false; p.EcoCrowd = 99; p.EcoQuality = 0; f(p); return p; }
            var variants = new System.Collections.Generic.List<(string name, System.Func<AiProfile> make)>
            {
                ("share0.6", () => Mk(p => p.EcoInfShare = 0.6f)),
                ("share0.45", () => Mk(p => p.EcoInfShare = 0.45f)),
                ("share0.6 crowd4", () => Mk(p => { p.EcoInfShare = 0.6f; p.EcoCrowd = 4; })),
                ("share0.6 save", () => Mk(p => { p.EcoInfShare = 0.6f; p.EcoSave = true; })),
                ("default", () => AiProfile.ForLevel(3)),
            };
            foreach (var v in variants)
            {
                int w = 0, l = 0;
                for (uint seed = 1; seed <= 3; seed++)
                {
                    var old = AiProfile.ForLevel(3); old.Features &= ~(E | F);
                    var r1 = Play(v.make(), old, seed, 50);
                    old = AiProfile.ForLevel(3); old.Features &= ~(E | F);
                    var r2 = Play(old, v.make(), seed, 50);
                    if (r1.Winner == Army.Red) w++; else if (r1.Winner == Army.Blue) l++;
                    if (r2.Winner == Army.Blue) w++; else if (r2.Winner == Army.Red) l++;
                }
                UnityEngine.Debug.Log($"[ECO] {v.name}: {w} wins / {l} losses vs old Lv3");
            }
        }

        [Test]
        public void BatteredFrontUnitStepsBackForAFreshOne()
        {
            var g = Data();
            var text = "[tiles]\n......\n......\n......\n[owners]\n......\n......\n......\n[units]\nINF,Red,3,1,40\nINF,Blue,4,1,100\nINF,Red,1,1,100\n";
            var s = GameState.Create(g, MapDef.Parse(text, g), 3);
            s.Active = Army.Red;
            var weak = s.UnitAt(3, 1);
            var c = new AiPlayer(Army.Red, AiProfile.ForLevel(3), 1).Next(s) as UnitCommand;
            Assert.IsNotNull(c);
            Assert.AreEqual(weak.Id, c.UnitId, "the battered unit moves first");
            Assert.AreEqual(UnitAction.Wait, c.Action);
            Assert.Greater(Movement.Distance(c.ToX, c.ToY, 4, 1), 1, "it leaves the fight");
        }

        [Test]
        public void ComNeverMovesOutOfTurnAndEndsItsPhase()
        {
            var r = Play(2, 3, 7, 3);
            Assert.Greater(r.Commands, 4);
        }
    }
}
