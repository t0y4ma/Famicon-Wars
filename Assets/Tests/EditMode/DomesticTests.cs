using System.IO;
using System.Linq;
using FamiconWars.Core;
using NUnit.Framework;
using UnityEngine;

namespace FamiconWars.Tests
{
    /// <summary>内政: facility levels, reservations off-turn, cancellation by capture, conversion, the 内政 time.</summary>
    public class DomesticTests
    {
        static string DataDir => Path.Combine(Application.dataPath, "Resources/Data");
        static string Read(string f) => File.ReadAllText(Path.Combine(DataDir, f));

        static GameData LoadData()
        {
            var g = GameData.Load(Read("units.csv"), Read("terrains.csv"), Read("damage.csv"), Read("rules.csv"), Read("economy.csv"), Read("production.csv"));
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

        static ApplyResult Do(GameState s, Command c) => RulesEngine.Apply(s, c);
        static ApplyResult End(GameState s, bool interlude = false) => Do(s, new EndPhaseCommand { Army = s.Active, Interlude = interlude });
        static ApplyResult Up(GameState s, Army a, int x, int y) => Do(s, new LevelUpCommand { Army = a, X = x, Y = y });

        /// <summary>Red base, red city, red factory on the left; blue mirror on the right; sea at the bottom.</summary>
        static GameState Board(GameData g, params (string t, Army a, int x, int y)[] units) => Make(g,
            new[] { "HCX....XCH", "C........C", "~~~~~~~~~~" },
            new[] { "rrr....bbb", "r........b", ".........." },
            units.Length > 0 ? units : new[] { ("INF", Army.Red, 4, 0), ("INF", Army.Blue, 5, 0) });

        [Test]
        public void EveryFacilityStartsAtLevelOneAndIncomeFollowsTheFactor()
        {
            var g = LoadData();
            var s = Board(g);
            Assert.AreEqual(1, s.LevelAt(1, 0));
            Assert.AreEqual(0, s.LevelAt(4, 1), "plain");
            int city = s.Index(1, 0);
            Assert.AreEqual(1000, RulesEngine.Income(s, city));
            s.Level[city] = 2; Assert.AreEqual(1500, RulesEngine.Income(s, city));
            s.Level[city] = 4; Assert.AreEqual(2500, RulesEngine.Income(s, city));
        }

        [Test]
        public void ReservingIsOnlyAllowedOffTurnAndTakesEffectBeforeTheNextIncome()
        {
            var g = LoadData();
            var s = Board(g);
            Assert.AreEqual(Army.Red, s.Active);
            s.Funds[0] = 50000; s.Funds[1] = 50000;
            StringAssert.Contains("手番中", Up(s, Army.Red, 1, 0).Error ?? "");
            var r = Up(s, Army.Blue, 8, 0);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.AreEqual(46000, s.Funds[1], "paid at once");
            Assert.AreEqual(GameState.ReserveLevelUp, s.Reserve[s.Index(8, 0)]);
            StringAssert.Contains("予約済み", Up(s, Army.Blue, 8, 0).Error ?? "", "one level at a time");
            int before = s.Funds[1];
            End(s);                                     // blue's phase: the level applies, then the income
            Assert.AreEqual(Army.Blue, s.Active);
            Assert.AreEqual(2, s.LevelAt(8, 0));
            Assert.AreEqual(0, s.Reserve[s.Index(8, 0)]);
            // blue: base 4000 + cities 1500 (Lv2) + 1000 + factory 0
            Assert.AreEqual(before + 4000 + 1500 + 1000, s.Funds[1]);
        }

        [Test]
        public void OnlyTheLatestReservationCanBeTakenBack()
        {
            var g = LoadData();
            var s = Board(g);
            s.Funds[1] = 50000;
            Assert.IsTrue(Up(s, Army.Blue, 8, 0).Ok);
            Assert.IsTrue(Up(s, Army.Blue, 9, 1).Ok);
            Assert.AreEqual(42000, s.Funds[1]);
            Assert.IsTrue(Do(s, new CancelReserveCommand { Army = Army.Blue }).Ok);
            Assert.AreEqual(46000, s.Funds[1], "the city at 9,1 is refunded");
            Assert.AreEqual(0, s.Reserve[s.Index(9, 1)]);
            Assert.AreNotEqual(0, s.Reserve[s.Index(8, 0)], "the older one is fixed");
            Assert.IsFalse(Do(s, new CancelReserveCommand { Army = Army.Blue }).Ok, "only one undo");
        }

        [Test]
        public void ACaptureOrderCancelsTheReservationWithoutRefund()
        {
            var g = LoadData();
            // a red infantry right next to blue's city at 8,0
            var s = Board(g, ("INF", Army.Red, 7, 1), ("INF", Army.Blue, 5, 0));
            s.Funds[1] = 50000;
            Assert.IsTrue(Up(s, Army.Blue, 8, 0).Ok);
            var inf = s.Units.First(u => u.Army == Army.Red);
            var r = Do(s, new UnitCommand { Army = Army.Red, UnitId = inf.Id, ToX = 8, ToY = 0, Action = UnitAction.Capture });
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsTrue(r.Events.OfType<ReserveLostEvent>().Any());
            Assert.AreEqual(0, s.Reserve[s.Index(8, 0)]);
            Assert.AreEqual(46000, s.Funds[1], "the funds are gone");
            Assert.IsFalse(Do(s, new CancelReserveCommand { Army = Army.Blue }).Ok);
            // and while it is being captured (or an enemy stands on it) it cannot be reserved again
            StringAssert.Contains("占領", Up(s, Army.Blue, 8, 0).Error ?? "");
            End(s);
            Assert.AreEqual(1, s.LevelAt(8, 0));
        }

        [Test]
        public void AnEnemyUnitOnTheFacilityBlocksReserving()
        {
            var g = LoadData();
            var s = Board(g, ("INF", Army.Red, 7, 1), ("INF", Army.Blue, 5, 0));
            s.Funds[1] = 50000;
            var inf = s.Units.First(u => u.Army == Army.Red);
            Assert.IsTrue(Do(s, new UnitCommand { Army = Army.Red, UnitId = inf.Id, ToX = 8, ToY = 0, Action = UnitAction.Wait }).Ok);
            StringAssert.Contains("敵の部隊", Up(s, Army.Blue, 8, 0).Error ?? "");
        }

        [Test]
        public void TheFacilityLevelDecidesWhatItMakes()
        {
            var g = LoadData();
            var s = Board(g);
            s.Funds[0] = 90000;
            string[] Names(int x, int y) => RulesEngine.ProducibleAt(s, Army.Red, x, y).Select(d => d.Id).ToArray();
            CollectionHas(Names(2, 0), "INF"); CollectionLacks(Names(2, 0), "TANK_B");      // factory Lv1
            CollectionHas(Names(1, 0), "INF"); CollectionLacks(Names(1, 0), "TANK_B");      // city: infantry only
            CollectionHas(Names(0, 0), "TANK_A");                                           // the base makes everything
            StringAssert.Contains("レベル2", Do(s, new ProduceCommand { Army = Army.Red, X = 2, Y = 0, UnitType = "TANK_B" }).Error ?? "");
            s.Level[s.Index(2, 0)] = 2;
            CollectionHas(Names(2, 0), "TANK_B"); CollectionLacks(Names(2, 0), "TANK_A");
            s.Level[s.Index(1, 0)] = 4;
            CollectionLacks(Names(1, 0), "TANK_B");                                         // a city never gains units
        }

        [Test]
        public void TheBaseLevelWidensTheProductionRange()
        {
            var g = LoadData();
            var s = Make(g, new[] { "H..X.....H" }, new[] { "r..r.....b" }, ("INF", Army.Red, 1, 0), ("INF", Army.Blue, 8, 0));
            Assert.IsFalse(RulesEngine.InProductionRange(s, Army.Red, 3, 0), "3 tiles from a Lv1 base");
            s.Level[s.Index(0, 0)] = 2;
            Assert.IsTrue(RulesEngine.InProductionRange(s, Army.Red, 3, 0));
            Assert.IsFalse(RulesEngine.InProductionRange(s, Army.Red, 9, 0));
            s.Level[s.Index(0, 0)] = 4;
            Assert.IsTrue(RulesEngine.InProductionRange(s, Army.Red, 9, 0), "Lv4: anywhere");
        }

        [Test]
        public void OnlyALevelTwoCityConvertsAndAPortNeedsTheSea()
        {
            var g = LoadData();
            var s = Board(g);
            s.Funds[1] = 50000;
            StringAssert.Contains("レベル2", Do(s, new ConvertCommand { Army = Army.Blue, X = 8, Y = 0, Target = "FACTORY" }).Error ?? "");
            s.Level[s.Index(8, 0)] = 2; s.Level[s.Index(9, 1)] = 3;
            StringAssert.Contains("海", Do(s, new ConvertCommand { Army = Army.Blue, X = 8, Y = 0, Target = "PORT" }).Error ?? "");
            StringAssert.Contains("都市だけ", Do(s, new ConvertCommand { Army = Army.Blue, X = 7, Y = 0, Target = "AIRPORT" }).Error ?? "", "factory → airport is not allowed");
            Assert.IsTrue(Do(s, new ConvertCommand { Army = Army.Blue, X = 9, Y = 1, Target = "PORT" }).Ok, "next to the sea");
            Assert.IsTrue(Do(s, new ConvertCommand { Army = Army.Blue, X = 8, Y = 0, Target = "AIRPORT" }).Ok);
            Assert.AreEqual(50000 - 16000, s.Funds[1]);
            End(s);
            Assert.AreEqual("PORT", s.TerrainAt(9, 1).Id); Assert.AreEqual(2, s.LevelAt(9, 1));
            Assert.AreEqual("AIRPORT", s.TerrainAt(8, 0).Id); Assert.AreEqual(1, s.LevelAt(8, 0));
        }

        [Test]
        public void ACapturedFacilityDropsALevel()
        {
            var g = LoadData();
            var s = Board(g, ("INF", Army.Red, 7, 1), ("INF", Army.Blue, 5, 0));
            int city = s.Index(8, 0);
            s.Level[city] = 3;
            var inf = s.Units.First(u => u.Army == Army.Red);
            Assert.IsTrue(Do(s, new UnitCommand { Army = Army.Red, UnitId = inf.Id, ToX = 8, ToY = 0, Action = UnitAction.Capture }).Ok);
            End(s); End(s);
            Assert.IsTrue(Do(s, new UnitCommand { Army = Army.Red, UnitId = inf.Id, ToX = 8, ToY = 0, Action = UnitAction.Capture }).Ok);
            Assert.AreEqual(Army.Red, s.Owner[city]);
            Assert.AreEqual(2, s.Level[city]);
        }

        [Test]
        public void ATopLevelFactorysFirstUnitOfThePhaseCanMove()
        {
            var g = LoadData();
            var s = Board(g);
            s.Funds[0] = 90000;
            s.Level[s.Index(2, 0)] = 4;
            Assert.IsTrue(Do(s, new ProduceCommand { Army = Army.Red, X = 2, Y = 0, UnitType = "TANK_A" }).Ok);
            var first = s.UnitAt(2, 0);
            Assert.IsFalse(first.Acted, "ready at once");
            Assert.IsTrue(Do(s, new UnitCommand { Army = Army.Red, UnitId = first.Id, ToX = 3, ToY = 1, Action = UnitAction.Wait }).Ok);
            Assert.IsTrue(Do(s, new ProduceCommand { Army = Army.Red, X = 2, Y = 0, UnitType = "INF" }).Ok);
            Assert.IsTrue(s.UnitAt(2, 0).Acted, "only the first one");
            s.Level[s.Index(1, 0)] = 4;
            Assert.IsTrue(Do(s, new ProduceCommand { Army = Army.Red, X = 1, Y = 0, UnitType = "INF" }).Ok);
            Assert.IsTrue(s.UnitAt(1, 0).Acted, "cities have no such bonus");
        }

        [Test]
        public void AnInterludeAfterAComPhaseOnlyAllowsDomestic()
        {
            var g = LoadData();
            var s = Board(g);
            s.Funds[0] = 50000; s.Funds[1] = 50000;
            var r = End(s, interlude: true);
            Assert.IsTrue(s.Interlude);
            Assert.AreEqual(Army.Red, s.Active, "still red's turn number");
            Assert.IsTrue(r.Events.OfType<InterludeEvent>().Any(e => e.Started));
            var inf = s.Units.First(u => u.Army == Army.Red);
            Assert.IsFalse(Do(s, new UnitCommand { Army = Army.Red, UnitId = inf.Id, ToX = 4, ToY = 1, Action = UnitAction.Wait }).Ok);
            Assert.IsFalse(End(s).Ok);
            Assert.IsTrue(Up(s, Army.Red, 1, 0).Ok, "the army that just played may also reserve");
            Assert.IsTrue(Up(s, Army.Blue, 8, 0).Ok);
            Assert.IsTrue(Do(s, new InterludeDoneCommand { Army = Army.Blue }).Ok);
            Assert.IsTrue(s.InterludeReady[1]);
            Assert.IsFalse(Do(s, new InterludeDoneCommand { Army = Army.Blue }).Ok, "pressed once");
            Assert.IsTrue(Do(s, new EndInterludeCommand { Army = Army.Red }).Ok);
            Assert.IsFalse(s.Interlude);
            Assert.AreEqual(Army.Blue, s.Active);
            Assert.AreEqual(2, s.LevelAt(8, 0));
            Assert.AreEqual(1, s.LevelAt(1, 0), "red's applies at red's own phase");
            End(s);
            Assert.AreEqual(2, s.LevelAt(1, 0));
        }

        [Test]
        public void DomesticCommandsSurviveTheWire()
        {
            Command[] cs =
            {
                new LevelUpCommand { Army = Army.Green, X = 3, Y = 4 },
                new ConvertCommand { Army = Army.Blue, X = 1, Y = 2, Target = "PORT" },
                new CancelReserveCommand { Army = Army.Yellow },
                new InterludeDoneCommand { Army = Army.Red },
                new EndInterludeCommand { Army = Army.Red },
                new EndPhaseCommand { Army = Army.Blue, Interlude = true },
            };
            foreach (var c in cs)
            {
                var back = CommandCodec.Decode(CommandCodec.Encode(c));
                Assert.IsNotNull(back, CommandCodec.Encode(c));
                Assert.AreEqual(CommandCodec.Encode(c), CommandCodec.Encode(back));
            }
            Assert.IsTrue(((EndPhaseCommand)CommandCodec.Decode("E,1,1")).Interlude);
            Assert.IsFalse(((EndPhaseCommand)CommandCodec.Decode("E,1")).Interlude);
        }

        [Test]
        public void WithoutTheEconomyFileTheOriginalRulesHold()
        {
            var g = GameData.Load(Read("units.csv"), Read("terrains.csv"), Read("damage.csv"), Read("rules.csv"));
            var s = Board(g);
            Assert.IsFalse(s.Domestic);
            Assert.IsTrue(RulesEngine.ProducibleAt(s, Army.Red, 1, 0).Any(d => d.Id == "TANK_A"), "a city makes every ground unit");
            Assert.IsFalse(Up(s, Army.Blue, 8, 0).Ok);
        }

        static void CollectionHas(string[] a, string id) => Assert.IsTrue(a.Contains(id), id + " in " + string.Join(",", a));
        static void CollectionLacks(string[] a, string id) => Assert.IsFalse(a.Contains(id), id + " not in " + string.Join(",", a));
    }
}
