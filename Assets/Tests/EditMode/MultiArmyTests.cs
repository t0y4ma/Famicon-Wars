using System.IO;
using System.Linq;
using FamiconWars.Core;
using NUnit.Framework;
using UnityEngine;

namespace FamiconWars.Tests
{
    /// <summary>Three or more armies: turn order, armies going out, and the last one standing.</summary>
    public class MultiArmyTests
    {
        static string DataDir => Path.Combine(Application.dataPath, "Resources/Data");

        static GameData LoadData()
        {
            var g = GameData.Load(
                File.ReadAllText(Path.Combine(DataDir, "units.csv")),
                File.ReadAllText(Path.Combine(DataDir, "terrains.csv")),
                File.ReadAllText(Path.Combine(DataDir, "damage.csv")),
                File.ReadAllText(Path.Combine(DataDir, "rules.csv")));
            g.Rules.RandMin = 0; g.Rules.RandMax = 0;
            return g;
        }

        static GameState Make(GameData g, string[] rows, string[] owners, params (string t, Army a, int x, int y)[] units)
        {
            var text = "[tiles]\n" + string.Join("\n", rows) + "\n[owners]\n" + string.Join("\n", owners) + "\n[units]\n"
                       + string.Join("\n", units.Select(u => $"{u.t},{u.a},{u.x},{u.y},100"));
            var s = GameState.Create(g, MapDef.Parse(text, g), 12345);
            RulesEngine.StartGame(s);
            return s;
        }

        static ApplyResult Act(GameState s, UnitState u, int tx, int ty, UnitAction a, int target = -1)
            => RulesEngine.Apply(s, new UnitCommand { Army = u.Army, UnitId = u.Id, ToX = tx, ToY = ty, Action = a, TargetId = target });

        static ApplyResult End(GameState s) => RulesEngine.Apply(s, new EndPhaseCommand { Army = s.Active });

        /// <summary>Four bases in a row (red, blue, green, yellow), an infantry beside each.</summary>
        static GameState Four(GameData g) => Make(g,
            new[] { "H.H.H.H.", "........" },
            new[] { "r.b.g.y.", "........" },
            ("INF", Army.Red, 0, 1), ("INF", Army.Blue, 2, 1), ("INF", Army.Green, 4, 1), ("INF", Army.Yellow, 6, 1));

        [Test]
        public void PlayerCountComesFromTheMap()
        {
            var g = LoadData();
            Assert.AreEqual(4, Four(g).Players);
            var three = Make(g, new[] { "H.H.H" }, new[] { "r.b.g" }, ("INF", Army.Red, 1, 0), ("INF", Army.Blue, 3, 0));
            Assert.AreEqual(3, three.Players, "green owns a base but has no unit yet");
            var two = Make(g, new[] { "H.H" }, new[] { "r.b" }, ("INF", Army.Red, 1, 0));
            Assert.AreEqual(2, two.Players);
        }

        [Test]
        public void FourArmiesTakeTurnsAndTheDayEndsAfterTheLast()
        {
            var g = LoadData();
            var s = Four(g);
            Assert.AreEqual(Army.Red, s.Active);
            Assert.AreEqual(4000, s.Funds[0], "income from day 1");
            End(s); Assert.AreEqual(Army.Blue, s.Active); Assert.AreEqual(1, s.Day);
            End(s); Assert.AreEqual(Army.Green, s.Active); Assert.AreEqual(4000, s.Funds[2]);
            End(s); Assert.AreEqual(Army.Yellow, s.Active); Assert.AreEqual(1, s.Day);
            End(s); Assert.AreEqual(Army.Red, s.Active); Assert.AreEqual(2, s.Day);
            Assert.AreEqual(8000, s.Funds[0]);
        }

        [Test]
        public void TakingABaseKnocksTheArmyOutAndHandsOverItsProperties()
        {
            var g = LoadData();
            // red infantry stands on blue's base; blue also owns a city; green plays on
            var s = Make(g, new[] { "H.C.H.H" }, new[] { "r.b.b.g" },
                ("INF", Army.Red, 4, 0), ("INF", Army.Blue, 1, 0), ("TANK_A", Army.Blue, 3, 0), ("INF", Army.Green, 5, 0));
            var inf = s.UnitAt(4, 0);
            Assert.IsTrue(Act(s, inf, 4, 0, UnitAction.Capture).Ok);
            inf.Acted = false;
            var r = Act(s, inf, 4, 0, UnitAction.Capture);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsFalse(s.GameOver, "green is still in");
            Assert.IsTrue(s.IsOut(Army.Blue));
            Assert.AreEqual(Army.Red, s.Owner[2], "blue's city goes to the army that took the base");
            Assert.AreEqual(Army.Red, s.Owner[4]);
            Assert.AreEqual("CITY", s.TerrainAt(4, 0).Id, "one base per army: the captured base becomes a city");
            Assert.IsFalse(s.Units.Any(u => u.Army == Army.Blue), "blue's units are gone");
            var ev = r.Events.OfType<EliminatedEvent>().Single();
            Assert.AreEqual(Army.Blue, ev.Army); Assert.AreEqual(Army.Red, ev.By);
            Assert.AreEqual(4, ev.X);
            // blue is skipped from now on
            End(s);
            Assert.AreEqual(Army.Green, s.Active);
            End(s);
            Assert.AreEqual(Army.Red, s.Active); Assert.AreEqual(2, s.Day);
        }

        [Test]
        public void ARoutedArmysPropertiesTurnNeutral()
        {
            var g = LoadData();
            var s = Make(g, new[] { "H.C.H.H" }, new[] { "r.g.b.g" },
                ("TANK_A", Army.Red, 1, 0), ("INF", Army.Green, 2, 0), ("INF", Army.Blue, 5, 0));
            // green's only unit is on the city (it owns the city and the base at x=6)
            s.UnitAt(2, 0).Hp = 10;
            var r = Act(s, s.UnitAt(1, 0), 1, 0, UnitAction.Attack, s.UnitAt(2, 0).Id);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsTrue(s.IsOut(Army.Green));
            Assert.IsFalse(s.GameOver);
            Assert.AreEqual(Army.None, s.Owner[2]);
            Assert.AreEqual(Army.None, s.Owner[6]);
            Assert.AreEqual("HQ", s.TerrainAt(6, 0).Id, "nobody took it: it stays a base (neutral)");
        }

        [Test]
        public void TheLastArmyStandingWins()
        {
            var g = LoadData();
            var s = Make(g, new[] { "H.H.H" }, new[] { "r.b.g" },
                ("INF", Army.Red, 1, 0), ("INF", Army.Blue, 3, 0), ("INF", Army.Green, 4, 1 - 1));
            Assert.IsTrue(RulesEngine.Apply(s, new SurrenderCommand { Army = Army.Blue }).Ok);
            Assert.IsFalse(s.GameOver);
            var r = RulesEngine.Apply(s, new SurrenderCommand { Army = Army.Green });
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsTrue(s.GameOver);
            Assert.AreEqual(Army.Red, s.Winner);
            Assert.AreEqual("降伏", r.Events.OfType<GameOverEvent>().Single().Reason);
            CollectionAssertOrder(s, Army.Blue, Army.Green);
        }

        static void CollectionAssertOrder(GameState s, params Army[] order)
        {
            Assert.AreEqual(order.Length, s.Eliminated.Count);
            for (int i = 0; i < order.Length; i++) Assert.AreEqual(order[i], s.Eliminated[i]);
        }

        [Test]
        public void SurrenderingOffTurnSkipsThatArmyAfterwards()
        {
            var g = LoadData();
            var s = Four(g);
            var r = RulesEngine.Apply(s, new SurrenderCommand { Army = Army.Blue });
            Assert.IsTrue(r.Ok, r.Error);
            Assert.AreEqual(Army.Red, s.Active, "red's phase goes on");
            Assert.AreEqual(Army.None, s.Owner[2], "a surrendered army's base turns neutral");
            End(s);
            Assert.AreEqual(Army.Green, s.Active);
            Assert.IsFalse(RulesEngine.Apply(s, new EndPhaseCommand { Army = Army.Blue }).Ok, "an army that is out cannot act");
        }

        [Test]
        public void AnArmyLosingItsLastUnitInItsOwnPhasePassesTheTurn()
        {
            var g = LoadData();
            var s = Make(g, new[] { "H.H.H.." }, new[] { "r.b.g.." },
                ("INF", Army.Red, 5, 0), ("TANK_A", Army.Blue, 6, 0), ("INF", Army.Green, 3, 0));
            var inf = s.UnitAt(5, 0);
            inf.Hp = 10;
            var r = Act(s, inf, 5, 0, UnitAction.Attack, s.UnitAt(6, 0).Id);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsTrue(s.IsOut(Army.Red), "the counterattack destroyed red's last unit");
            Assert.IsFalse(s.GameOver);
            Assert.AreEqual(Army.Blue, s.Active, "blue's phase starts at once");
            Assert.IsTrue(r.Events.OfType<PhaseStartEvent>().Any(e => e.Army == Army.Blue));
        }

        [Test]
        public void WhenTheFirstArmyIsOutTheDayStillAdvances()
        {
            var g = LoadData();
            var s = Four(g);
            RulesEngine.Apply(s, new SurrenderCommand { Army = Army.Red });
            Assert.AreEqual(Army.Blue, s.Active, "red gave up in its own phase");
            End(s); End(s);
            Assert.AreEqual(Army.Yellow, s.Active); Assert.AreEqual(1, s.Day);
            End(s);
            Assert.AreEqual(Army.Blue, s.Active); Assert.AreEqual(2, s.Day);
        }

        [Test]
        public void TwoArmiesBehaveAsBefore()
        {
            var g = LoadData();
            var s = Make(g, new[] { "H.H" }, new[] { "r.b" }, ("INF", Army.Red, 1, 0), ("INF", Army.Blue, 2, 0));
            var r = RulesEngine.Apply(s, new SurrenderCommand { Army = Army.Red });
            Assert.IsTrue(s.GameOver);
            Assert.AreEqual(Army.Blue, s.Winner);
            Assert.IsFalse(r.Events.OfType<EliminatedEvent>().Any(), "with two armies the game simply ends");
            Assert.AreEqual(Army.Red, s.Owner[0], "nothing changes hands when the game is over");
        }

        [Test]
        public void TurnTimeSliderRunsFrom20SecondsTo5MinutesThenUnlimited()
        {
            Assert.AreEqual(20, TurnTime.FromStep(0));
            Assert.AreEqual(300, TurnTime.FromStep(TurnTime.Steps - 1));
            Assert.AreEqual(TurnTime.Unlimited, TurnTime.FromStep(TurnTime.Steps));
            int prev = 0;
            for (int i = 0; i < TurnTime.Steps; i++) { int v = TurnTime.FromStep(i); Assert.Greater(v, prev, "step " + i); prev = v; }
            Assert.AreEqual(60, TurnTime.FromStep(TurnTime.ToStep(TurnTime.DefaultSeconds)), "the default (1 minute) is one of the steps");
            Assert.AreEqual(TurnTime.Steps, TurnTime.ToStep(TurnTime.Unlimited));
            Assert.AreEqual("1分", TurnTime.Label(60));
            Assert.AreEqual("2分30秒", TurnTime.Label(150));
            Assert.AreEqual("無制限", TurnTime.Label(0));
        }

        [Test]
        public void AnEmptySeatStartsOutWithItsPropertiesNeutral()
        {
            var g = LoadData();
            var text = "[tiles]\nH.H.H.H.\n........\n[owners]\nr.b.g.y.\n........\n[units]\nINF,Blue,2,1,100\nINF,Green,4,1,100";
            var s = GameState.Create(g, MapDef.Parse(text, g), 1, new[] { false, true, true, true });
            RulesEngine.StartGame(s);
            Assert.AreEqual(4, s.Players);
            Assert.IsTrue(s.IsOut(Army.Red));
            Assert.AreEqual(Army.None, s.Owner[0]);
            Assert.AreEqual(Army.Blue, s.Active, "red has no player: blue starts");
            Assert.AreEqual(0, s.Eliminated.Count, "an empty seat is not a knocked-out army");
            RulesEngine.Apply(s, new EndPhaseCommand { Army = Army.Blue });
            RulesEngine.Apply(s, new EndPhaseCommand { Army = Army.Green });
            RulesEngine.Apply(s, new EndPhaseCommand { Army = Army.Yellow });
            Assert.AreEqual(Army.Blue, s.Active); Assert.AreEqual(2, s.Day);
        }
    }
}
