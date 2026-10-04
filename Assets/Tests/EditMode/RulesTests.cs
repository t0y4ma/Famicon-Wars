using System.IO;
using System.Linq;
using FamiconWars.Core;
using NUnit.Framework;
using UnityEngine;

namespace FamiconWars.Tests
{
    public class RulesTests
    {
        static string DataDir => Path.Combine(Application.dataPath, "Resources/Data");

        static GameData LoadData(int randMin = 0, int randMax = 0)
        {
            var g = GameData.Load(
                File.ReadAllText(Path.Combine(DataDir, "units.csv")),
                File.ReadAllText(Path.Combine(DataDir, "terrains.csv")),
                File.ReadAllText(Path.Combine(DataDir, "damage.csv")),
                File.ReadAllText(Path.Combine(DataDir, "rules.csv")));
            g.Rules.RandMin = randMin;
            g.Rules.RandMax = randMax;
            return g;
        }

        /// <summary>Builds a state from tile rows. Units: (type, army, x, y).</summary>
        static GameState Make(GameData g, string[] rows, string[] owners, params (string t, Army a, int x, int y)[] units)
        {
            var text = "[tiles]\n" + string.Join("\n", rows) + "\n[owners]\n" + string.Join("\n", owners ?? rows.Select(r => new string('.', r.Length)).ToArray()) + "\n[units]\n"
                       + string.Join("\n", units.Select(u => $"{u.t},{u.a},{u.x},{u.y},100"));
            var s = GameState.Create(g, MapDef.Parse(text, g), 12345);
            s.Active = Army.Red;
            return s;
        }

        static UnitState At(GameState s, int x, int y) => s.UnitAt(x, y);

        static ApplyResult Act(GameState s, UnitState u, int tx, int ty, UnitAction a, int target = -1)
            => RulesEngine.Apply(s, new UnitCommand { Army = u.Army, UnitId = u.Id, ToX = tx, ToY = ty, Action = a, TargetId = target });

        // ---------- combat ----------

        [Test]
        public void InfantryOnRoadAttacksInfantryInCity()
        {
            var g = LoadData();
            var s = Make(g, new[] { "=C" }, null, ("INF", Army.Red, 0, 0), ("INF", Army.Blue, 1, 0));
            var red = At(s, 0, 0); var blue = At(s, 1, 0);
            var r = Act(s, red, 0, 0, UnitAction.Attack, blue.Id);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.AreEqual(73, blue.Hp, "45 x 0.6 = 27");
            Assert.AreEqual(55, red.Hp, "counter 45 on a road (defence 0)");
            Assert.AreEqual(8, blue.Count);
            Assert.AreEqual(6, red.Count);
        }

        [Test]
        public void InfantryOnPlainsTradeSixForSix()
        {
            var g = LoadData();
            var s = Make(g, new[] { ".." }, null, ("INF", Army.Red, 0, 0), ("INF", Army.Blue, 1, 0));
            var red = At(s, 0, 0); var blue = At(s, 1, 0);
            Assert.IsTrue(Act(s, red, 0, 0, UnitAction.Attack, blue.Id).Ok);
            Assert.AreEqual(6, red.Count);
            Assert.AreEqual(6, blue.Count);
        }

        [Test]
        public void CounterUsesPreBattleHp()
        {
            var g = LoadData();
            var s = Make(g, new[] { "=." }, null, ("TANK_A", Army.Red, 0, 0), ("INF", Army.Blue, 1, 0));
            var tank = At(s, 0, 0); var inf = At(s, 1, 0);
            Assert.IsTrue(Act(s, tank, 0, 0, UnitAction.Attack, inf.Id).Ok);
            Assert.AreEqual(100 - 76, inf.Hp, "85 x 0.9 = 76");
            Assert.AreEqual(95, tank.Hp, "infantry fires back with its full 10 men: 5 on a road (defence 0)");
        }

        [Test]
        public void RandomStaysInsideForecast()
        {
            var g = LoadData(0, 9);
            for (uint seed = 1; seed < 40; seed++)
            {
                var s = Make(g, new[] { ".." }, null, ("INF", Army.Red, 0, 0), ("INF", Army.Blue, 1, 0));
                s.Rng = new Rng(seed);
                var red = At(s, 0, 0); var blue = At(s, 1, 0);
                var f = Combat.Predict(s, red, 0, 0, blue);
                Assert.IsTrue(Act(s, red, 0, 0, UnitAction.Attack, blue.Id).Ok);
                int dealt = 100 - blue.Hp;
                Assert.That(dealt, Is.InRange(f.DamageToDefenderMin, f.DamageToDefenderMax));
            }
        }

        [Test]
        public void IndirectCannotMoveAndFireAndIsNotCountered()
        {
            var g = LoadData();
            var s = Make(g, new[] { "....." }, null, ("ART_B", Army.Red, 0, 0), ("TANK_B", Army.Blue, 3, 0));
            var art = At(s, 0, 0); var tank = At(s, 3, 0);
            Assert.IsFalse(Act(s, art, 1, 0, UnitAction.Attack, tank.Id).Ok, "cannot move then fire");
            Assert.IsTrue(Act(s, art, 0, 0, UnitAction.Attack, tank.Id).Ok);
            Assert.AreEqual(100, art.Hp);
            Assert.Less(tank.Hp, 100);
        }

        [Test]
        public void AntiAirCannotHitGround()
        {
            var g = LoadData();
            var s = Make(g, new[] { ".." }, null, ("AAG", Army.Red, 0, 0), ("INF", Army.Blue, 1, 0));
            Assert.IsFalse(Act(s, At(s, 0, 0), 0, 0, UnitAction.Attack, At(s, 1, 0).Id).Ok);
        }

        // ---------- movement ----------

        [Test]
        public void TankPaysTwoForForestAndCannotClimb()
        {
            var g = LoadData();
            var s = Make(g, new[] { ".ff^." }, null, ("TANK_B", Army.Red, 0, 0));
            var m = Movement.Reachable(s, At(s, 0, 0));
            Assert.AreEqual(2, m.Cost[s.Index(1, 0)]);
            Assert.AreEqual(4, m.Cost[s.Index(2, 0)]);
            Assert.IsFalse(m.Reaches(s.Index(3, 0)));
        }

        [Test]
        public void EnemyBlocksPath()
        {
            var g = LoadData();
            var s = Make(g, new[] { "....", "^^^^" }, null, ("TANK_B", Army.Red, 0, 0), ("INF", Army.Blue, 1, 0));
            var m = Movement.Reachable(s, At(s, 0, 0));
            Assert.IsFalse(m.Reaches(s.Index(2, 0)));
        }

        [Test]
        public void MoveSpendsFuelByTerrainCost()
        {
            var g = LoadData();
            var s = Make(g, new[] { ".ff" }, null, ("TANK_B", Army.Red, 0, 0));
            var t = At(s, 0, 0);
            Assert.IsTrue(Act(s, t, 2, 0, UnitAction.Wait).Ok);
            Assert.AreEqual(50 - 4, t.Fuel);
        }

        // ---------- capture / victory ----------

        [Test]
        public void TenInfantryCaptureInTwoActionsAndLeavingResets()
        {
            var g = LoadData();
            var s = Make(g, new[] { "C.", ".." }, null, ("INF", Army.Red, 0, 0), ("INF", Army.Blue, 1, 1));
            var inf = At(s, 0, 0);
            Assert.IsTrue(Act(s, inf, 0, 0, UnitAction.Capture).Ok);
            Assert.AreEqual(10, s.CaptureProgress[0]);
            inf.Acted = false;
            Assert.IsTrue(Act(s, inf, 1, 0, UnitAction.Wait).Ok);
            Assert.AreEqual(0, s.CaptureProgress[0], "leaving resets the gauge");
            inf.Acted = false;
            Act(s, inf, 0, 0, UnitAction.Capture); inf.Acted = false;
            Assert.IsTrue(Act(s, inf, 0, 0, UnitAction.Capture).Ok);
            Assert.AreEqual(Army.Red, s.Owner[0]);
        }

        [Test]
        public void OnlyInfantryCanTakeHqAndItWins()
        {
            var g = LoadData();
            var owners = new[] { "b." };
            var s = Make(g, new[] { "H." }, owners, ("MECH", Army.Red, 0, 0), ("INF", Army.Blue, 1, 0));
            Assert.IsFalse(Act(s, At(s, 0, 0), 0, 0, UnitAction.Capture).Ok);

            var s2 = Make(g, new[] { "H.." }, new[] { "b.." }, ("INF", Army.Red, 0, 0), ("INF", Army.Blue, 2, 0));
            var inf = At(s2, 0, 0);
            Act(s2, inf, 0, 0, UnitAction.Capture); inf.Acted = false;
            var r = Act(s2, inf, 0, 0, UnitAction.Capture);
            Assert.IsTrue(s2.GameOver);
            Assert.AreEqual(Army.Red, s2.Winner);
            Assert.IsTrue(r.Events.OfType<GameOverEvent>().Any());
        }

        [Test]
        public void DestroyingLastUnitWins()
        {
            var g = LoadData();
            var s = Make(g, new[] { "=." }, null, ("TANK_A", Army.Red, 0, 0), ("INF", Army.Blue, 1, 0));
            var inf = At(s, 1, 0);
            inf.Hp = 10;
            var r = Act(s, At(s, 0, 0), 0, 0, UnitAction.Attack, inf.Id);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsTrue(s.GameOver);
            Assert.AreEqual(Army.Red, s.Winner);
            Assert.AreEqual(100, At(s, 0, 0).Hp, "simultaneous fire: 1 soldier deals 0 + R(0)");
        }

        // ---------- economy ----------

        [Test]
        public void IncomeArrivesAtPhaseStartFromDayOne()
        {
            var g = LoadData();
            var s = Make(g, new[] { "HCCA" }, new[] { "rrrb" }, ("INF", Army.Red, 0, 0), ("INF", Army.Blue, 3, 0));
            RulesEngine.StartGame(s);
            Assert.AreEqual(6000, s.Funds[0], "two cities (1000 each) + HQ (4000)");
            Assert.IsTrue(RulesEngine.Apply(s, new EndPhaseCommand { Army = Army.Red }).Ok);
            Assert.AreEqual(2000, s.Funds[1], "blue's airport");
            Assert.AreEqual(Army.Blue, s.Active);
        }

        [Test]
        public void ProductionNeedsRangeFundsAndEmptyTile()
        {
            var g = LoadData();
            var s = Make(g, new[] { "HX.X" }, new[] { "rr.r" }, ("INF", Army.Blue, 2, 0));
            s.Funds[0] = 6000;
            Assert.IsFalse(RulesEngine.Apply(s, new ProduceCommand { Army = Army.Red, X = 3, Y = 0, UnitType = "INF" }).Ok, "factory 3 tiles from HQ");
            Assert.IsFalse(RulesEngine.Apply(s, new ProduceCommand { Army = Army.Red, X = 1, Y = 0, UnitType = "TANK_A" }).Ok, "not enough funds");
            var r = RulesEngine.Apply(s, new ProduceCommand { Army = Army.Red, X = 1, Y = 0, UnitType = "TANK_B" });
            Assert.IsTrue(r.Ok, r.Error);
            Assert.AreEqual(0, s.Funds[0]);
            Assert.IsTrue(At(s, 1, 0).Acted);
            Assert.IsFalse(RulesEngine.Apply(s, new ProduceCommand { Army = Army.Red, X = 1, Y = 0, UnitType = "INF" }).Ok, "occupied");
        }

        [Test]
        public void ResupplyRestoresTwoMachinesOrRefusesWhenBroke()
        {
            var g = LoadData();
            var s = Make(g, new[] { "C." }, new[] { "r." }, ("TANK_B", Army.Red, 0, 0), ("INF", Army.Blue, 1, 0));
            var t = At(s, 0, 0);
            t.Hp = 50;
            s.Funds[0] = 100;
            Assert.IsFalse(RulesEngine.Apply(s, new ResupplyAllCommand { Army = Army.Red }).Ok);
            s.Funds[0] = 5000;
            Assert.IsTrue(RulesEngine.Apply(s, new ResupplyAllCommand { Army = Army.Red }).Ok);
            Assert.AreEqual(70, t.Hp);
            Assert.AreEqual(5000 - 2 * 600, s.Funds[0]);
            Assert.IsFalse(RulesEngine.Apply(s, new ResupplyAllCommand { Army = Army.Red }).Ok, "once per phase");
        }

        [Test]
        public void AircraftCrashWhenFuelRunsOut()
        {
            var g = LoadData();
            var s = Make(g, new[] { "....." }, null, ("FTR_B", Army.Red, 0, 0), ("INF", Army.Red, 2, 0), ("INF", Army.Blue, 4, 0));
            At(s, 0, 0).Fuel = 4;
            RulesEngine.Apply(s, new EndPhaseCommand { Army = Army.Red });
            var r = RulesEngine.Apply(s, new EndPhaseCommand { Army = Army.Blue });
            Assert.IsNull(At(s, 0, 0), "fuel 4 - 5 -> crash");
            Assert.IsTrue(r.Events.OfType<UnitLostEvent>().Any(e => e.Reason == "墜落"));
        }

        // ---------- transport / join ----------

        [Test]
        public void JoinAddsHpAndEndsAction()
        {
            var g = LoadData();
            var s = Make(g, new[] { "..." }, null, ("INF", Army.Red, 0, 0), ("INF", Army.Red, 1, 0), ("INF", Army.Blue, 2, 0));
            var a = At(s, 0, 0); var b = At(s, 1, 0);
            a.Hp = 40; b.Hp = 30;
            Assert.IsTrue(Act(s, a, 1, 0, UnitAction.Join).Ok);
            Assert.AreEqual(70, b.Hp);
            Assert.IsTrue(b.Acted);
            Assert.IsNull(s.UnitById(a.Id));
        }

        [Test]
        public void LoadUnloadAndCargoSharesDamage()
        {
            var g = LoadData();
            var s = Make(g, new[] { "....." }, null, ("INF", Army.Red, 0, 0), ("APC", Army.Red, 1, 0), ("TANK_A", Army.Blue, 4, 0));
            var inf = At(s, 0, 0); var apc = At(s, 1, 0);
            Assert.IsTrue(Act(s, inf, 1, 0, UnitAction.Load).Ok);
            Assert.AreEqual(apc.Id, inf.CarriedBy);
            Assert.IsTrue(apc.Acted, "boarding uses the transport's action");
            apc.Acted = false;
            var r = RulesEngine.Apply(s, new UnitCommand { Army = Army.Red, UnitId = apc.Id, ToX = 2, ToY = 0, Action = UnitAction.Unload, CargoId = inf.Id, DropX = 2, DropY = 1 });
            Assert.IsFalse(r.Ok, "drop tile outside map");
            r = RulesEngine.Apply(s, new UnitCommand { Army = Army.Red, UnitId = apc.Id, ToX = 2, ToY = 0, Action = UnitAction.Unload, CargoId = inf.Id, DropX = 1, DropY = 0 });
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsFalse(inf.IsCarried);
            Assert.AreEqual(1, inf.X);
        }
    
        // ---------- join / unit limit / boarding ----------

        [Test]
        public void JoinAddsMachinesUpToTenAndLosesTheRest()
        {
            var g = LoadData();
            var s = Make(g, new[] { "..." }, null, ("INF", Army.Red, 0, 0), ("INF", Army.Red, 2, 0));
            var a = At(s, 0, 0); var b = At(s, 2, 0);
            a.Hp = 50; b.Hp = 70;
            Assert.AreEqual(StopKind.Join, Movement.StopAt(s, a, 2, 0));
            var r = Act(s, a, 2, 0, UnitAction.Join);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsFalse(s.Units.Contains(a), "the moving unit is absorbed");
            Assert.AreEqual(100, b.Hp);
            Assert.IsTrue(b.Acted);
            var ev = r.Events.OfType<JoinedEvent>().Single();
            Assert.AreEqual(10, ev.CountAfter);
            Assert.AreEqual(2, ev.Overflow, "5 + 7 = 12, two machines are lost");
        }

        [Test]
        public void JoinNeedsSameTypeAndAnUnderstrengthUnit()
        {
            var g = LoadData();
            var s = Make(g, new[] { "..." }, null, ("INF", Army.Red, 0, 0), ("INF", Army.Red, 1, 0), ("MECH", Army.Red, 2, 0));
            var a = At(s, 0, 0);
            Assert.AreNotEqual(StopKind.Join, Movement.StopAt(s, a, 1, 0), "both at 10: nothing to join");
            Assert.AreNotEqual(StopKind.Join, Movement.StopAt(s, a, 2, 0), "different type");
            a.Hp = 60;
            Assert.AreEqual(StopKind.Join, Movement.StopAt(s, a, 1, 0));
        }

        [Test]
        public void ProductionStopsAtTheUnitLimit()
        {
            var g = LoadData();
            g.Rules.UnitLimit = 3;
            var s = Make(g, new[] { "HX..." }, new[] { "rr..." }, ("INF", Army.Red, 2, 0), ("INF", Army.Red, 3, 0));
            s.Funds[0] = 10000;
            Assert.IsTrue(RulesEngine.Apply(s, new ProduceCommand { Army = Army.Red, X = 1, Y = 0, UnitType = "INF" }).Ok);
            At(s, 1, 0).X = 4;   // free the factory
            var r = RulesEngine.Apply(s, new ProduceCommand { Army = Army.Red, X = 1, Y = 0, UnitType = "INF" });
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("上限", r.Error);
        }

        [Test]
        public void InfantryCannotWalkOntoTheSeaToBoardAHelicopter()
        {
            var g = LoadData();
            var s = Make(g, new[] { ".~." }, null, ("INF", Army.Red, 0, 0), ("HELI", Army.Red, 1, 0));
            var inf = At(s, 0, 0);
            Assert.IsFalse(Movement.Reachable(s, inf).Reaches(s.Index(1, 0)));
            Assert.AreEqual(StopKind.None, Movement.StopAt(s, inf, 1, 0));
            Assert.IsFalse(Act(s, inf, 1, 0, UnitAction.Load).Ok);
        }

        [Test]
        public void HqProducesGroundUnits()
        {
            var g = LoadData();
            var s = Make(g, new[] { "H." }, new[] { "r." });
            s.Funds[0] = 10000;
            Assert.IsTrue(RulesEngine.ProducibleAt(s, Army.Red, 0, 0).Any(d => d.Id == "TANK_B"));
            Assert.IsTrue(RulesEngine.Apply(s, new ProduceCommand { Army = Army.Red, X = 0, Y = 0, UnitType = "INF" }).Ok);
        }

        [Test]
        public void IncomePerProperty()
        {
            var g = LoadData();
            Assert.AreEqual(4000, g.Terrain("HQ").Income);
            Assert.AreEqual(0, g.Terrain("FACTORY").Income);
            Assert.AreEqual(1000, g.Terrain("CITY").Income);
            Assert.AreEqual(2000, g.Terrain("AIRPORT").Income);
            Assert.AreEqual(2000, g.Terrain("PORT").Income);
        }

        [Test]
        public void NoUnloadingFromABridgeOrIntoAForest()
        {
            var g = LoadData();
            var s = Make(g, new[] { ".#f", "..." }, null, ("APC", Army.Red, 0, 1), ("INF", Army.Red, 1, 1));
            var apc = At(s, 0, 1); var inf = At(s, 1, 1);
            Assert.IsTrue(Act(s, inf, 0, 1, UnitAction.Load).Ok);
            s.Units.ForEach(u => u.Acted = false);
            UnitCommand Drop(int tx, int ty, int dx, int dy) => new UnitCommand { Army = Army.Red, UnitId = apc.Id, ToX = tx, ToY = ty, Action = UnitAction.Unload, CargoId = inf.Id, DropX = dx, DropY = dy };
            Assert.IsNotNull(RulesEngine.Check(s, Drop(1, 0, 0, 0)), "the APC stands on a bridge");
            Assert.IsNotNull(RulesEngine.Check(s, Drop(2, 1, 2, 0)), "forest is not an unloading tile");
            Assert.IsNull(RulesEngine.Check(s, Drop(1, 1, 0, 1)), "plains to plains is fine");
        }
}
}
