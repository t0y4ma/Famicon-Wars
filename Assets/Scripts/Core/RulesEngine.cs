using System;
using System.Collections.Generic;

namespace FamiconWars.Core
{
    /// <summary>The only place that changes GameState. Every command is fully checked before anything is modified.</summary>
    public static class RulesEngine
    {
        static readonly int[] DX = { 1, -1, 0, 0 };
        static readonly int[] DY = { 0, 0, 1, -1 };

        /// <summary>Call once after GameState.Create: starts Red's first phase (income from day 1).</summary>
        public static ApplyResult StartGame(GameState s)
        {
            var r = new ApplyResult();
            StartPhase(s, Army.Red, r);
            return r;
        }

        public static string Check(GameState s, Command c)
        {
            if (s.GameOver) return "対戦は終了しています";
            if (c.Army != s.Active) return "手番ではありません";
            switch (c)
            {
                case UnitCommand uc: return CheckUnit(s, uc, out _, out _);
                case ProduceCommand pc: return CheckProduce(s, pc, out _);
                case ResupplyAllCommand _: return CheckResupply(s, out _, out _);
                case EndPhaseCommand _: return null;
                case SurrenderCommand _: return null;
            }
            return "不明なコマンド";
        }

        public static ApplyResult Apply(GameState s, Command c)
        {
            var err = Check(s, c);
            if (err != null) return ApplyResult.Fail(err);
            var r = new ApplyResult();
            switch (c)
            {
                case UnitCommand uc: ExecUnit(s, uc, r); break;
                case ProduceCommand pc: ExecProduce(s, pc, r); break;
                case ResupplyAllCommand _: ExecResupply(s, r); break;
                case EndPhaseCommand _: EndPhase(s, r); break;
                case SurrenderCommand _: Finish(s, Other(s.Active), "降伏", r); break;
            }
            return r;
        }

        public static Army Other(Army a) => a == Army.Red ? Army.Blue : Army.Red;

        // ---------------- unit command ----------------

        static string CheckUnit(GameState s, UnitCommand c, out MoveResult move, out int supplyCost)
        {
            move = null; supplyCost = 0;
            var u = s.UnitById(c.UnitId);
            if (u == null) return "部隊がいません";
            if (u.Army != c.Army) return "自軍の部隊ではありません";
            if (u.Acted) return "行動済みです";
            if (u.IsCarried) return "搭載中の部隊は動かせません";
            if (!s.InBounds(c.ToX, c.ToY)) return "マップの外です";
            move = Movement.Reachable(s, u);
            int dest = s.Index(c.ToX, c.ToY);
            if (!move.Reaches(dest)) return "そこへは移動できません";
            var stop = Movement.StopAt(s, u, c.ToX, c.ToY);
            if (stop == StopKind.None) return "そのマスには止まれません";
            if (move.EndOnly.Contains(dest) && stop != StopKind.Load) return "そのマスには止まれません";
            var d = s.Def(u);
            bool moved = c.ToX != u.X || c.ToY != u.Y;

            switch (c.Action)
            {
                case UnitAction.Wait:
                    if (stop != StopKind.Empty) return "そのマスには止まれません";
                    return null;
                case UnitAction.Load:
                    return stop == StopKind.Load ? null : "搭載できません";
                case UnitAction.Join:
                    return stop == StopKind.Join ? null : "合流できません";
                case UnitAction.Attack:
                {
                    if (stop != StopKind.Empty) return "そのマスからは攻撃できません";
                    var t = s.UnitById(c.TargetId);
                    if (t == null) return "攻撃対象がいません";
                    if (!Combat.CanAttackFrom(s, u, t, c.ToX, c.ToY, moved)) return "攻撃できません";
                    return null;
                }
                case UnitAction.Capture:
                {
                    if (stop != StopKind.Empty) return "占領できません";
                    var ter = s.TerrainAt(c.ToX, c.ToY);
                    if (d.Capture == 0 || !ter.IsProperty) return "占領できません";
                    if (s.Owner[dest] == u.Army) return "自軍の施設です";
                    if (ter.Id == "HQ" && d.Capture < 2) return "基地は歩兵しか占領できません";
                    return null;
                }
                case UnitAction.Unload:
                {
                    if (stop != StopKind.Empty) return "降車できません";
                    if (!u.Cargo.Contains(c.CargoId)) return "その部隊は搭載していません";
                    if (d.Domain == Domain.Sea && !Movement.IsShoreForShip(s, c.ToX, c.ToY)) return "揚陸艦は港か砂浜でしか降車できません";
                    if (Movement.Distance(c.ToX, c.ToY, c.DropX, c.DropY) != 1 || !s.InBounds(c.DropX, c.DropY)) return "降車先が隣ではありません";
                    var occ = s.UnitAt(c.DropX, c.DropY);
                    if (occ != null && occ.Id != u.Id) return "降車先に部隊がいます";
                    if (c.DropX == u.X && c.DropY == u.Y && moved) { } // the transport left this tile: allowed
                    var cargo = s.UnitById(c.CargoId);
                    if (s.TerrainAt(c.DropX, c.DropY).Cost[(int)s.Def(cargo).MoveClass] < 0) return "その地形には降ろせません";
                    return null;
                }
                case UnitAction.Supply:
                {
                    if (stop != StopKind.Empty || !d.CanSupply) return "補給できません";
                    var targets = SupplyTargets(s, u, c.ToX, c.ToY);
                    if (targets.Count == 0) return "補給できる部隊がいません";
                    foreach (var t in targets) supplyCost += FuelAmmoCost(s, t, t.Count);
                    if (supplyCost > s.Funds[(int)u.Army]) return "資金が足りません";
                    return null;
                }
            }
            return "不明な行動";
        }

        static void ExecUnit(GameState s, UnitCommand c, ApplyResult r)
        {
            CheckUnit(s, c, out var move, out var supplyCost);
            var u = s.UnitById(c.UnitId);
            int dest = s.Index(c.ToX, c.ToY);
            var path = move.PathTo(dest);
            u.Fuel -= move.Cost[dest];
            u.X = c.ToX; u.Y = c.ToY;
            ResetCaptureIfLeft(s, u);
            r.Events.Add(new MovedEvent { UnitId = u.Id, Path = path });

            switch (c.Action)
            {
                case UnitAction.Load:
                {
                    var t = FindOther(s, u, c.ToX, c.ToY);
                    u.CarriedBy = t.Id;
                    t.Cargo.Add(u.Id);
                    t.Acted = true;
                    break;
                }
                case UnitAction.Join:
                {
                    var t = FindOther(s, u, c.ToX, c.ToY);
                    int ca = u.Count, cb = t.Count;
                    int sum = t.Hp + u.Hp;
                    t.Hp = Math.Min(100, sum);
                    r.Events.Add(new JoinedEvent { UnitId = u.Id, IntoId = t.Id, CountA = ca, CountB = cb, CountAfter = t.Count, Overflow = Math.Max(0, ca + cb - t.Count) });
                    t.Fuel = Math.Max(t.Fuel, u.Fuel);
                    t.Ammo = Math.Max(t.Ammo, u.Ammo);
                    t.Acted = true;
                    RemoveUnit(s, u, null, null);
                    return;
                }
                case UnitAction.Attack:
                {
                    var t = s.UnitById(c.TargetId);
                    Battle(s, u, t, r);
                    break;
                }
                case UnitAction.Capture:
                {
                    if (s.CapturingUnit[dest] != u.Id) { s.CapturingUnit[dest] = u.Id; s.CaptureProgress[dest] = 0; }
                    s.CaptureProgress[dest] += u.Count;
                    var ev = new CaptureEvent { UnitId = u.Id, X = c.ToX, Y = c.ToY, Progress = Math.Min(s.CaptureProgress[dest], s.Rules.CaptureGoal), Goal = s.Rules.CaptureGoal };
                    if (s.CaptureProgress[dest] >= s.Rules.CaptureGoal)
                    {
                        var previous = s.Owner[dest];
                        s.Owner[dest] = u.Army;
                        s.CaptureProgress[dest] = 0;
                        s.CapturingUnit[dest] = -1;
                        ev.Completed = true;
                        r.Events.Add(ev);
                        if (s.TerrainAt(c.ToX, c.ToY).Id == "HQ" && previous != Army.None)
                            Finish(s, u.Army, "基地占領", r);
                    }
                    else r.Events.Add(ev);
                    break;
                }
                case UnitAction.Unload:
                {
                    var cargo = s.UnitById(c.CargoId);
                    u.Cargo.Remove(cargo.Id);
                    cargo.CarriedBy = -1;
                    cargo.X = c.DropX; cargo.Y = c.DropY;
                    cargo.Acted = true;
                    break;
                }
                case UnitAction.Supply:
                {
                    var ev = new ResuppliedEvent { Cost = supplyCost };
                    foreach (var t in SupplyTargets(s, u, c.ToX, c.ToY))
                    {
                        t.Fuel = s.Def(t).Fuel; t.Ammo = s.Def(t).Ammo;
                        ev.UnitIds.Add(t.Id);
                    }
                    s.Funds[(int)u.Army] -= supplyCost;
                    r.Events.Add(ev);
                    break;
                }
            }
            if (s.Units.Contains(u)) u.Acted = true;
            CheckRout(s, r);
        }

        static UnitState FindOther(GameState s, UnitState u, int x, int y)
        {
            foreach (var o in s.Units) if (!o.IsCarried && o.Id != u.Id && o.X == x && o.Y == y) return o;
            return null;
        }

        static List<UnitState> SupplyTargets(GameState s, UnitState truck, int x, int y)
        {
            var list = new List<UnitState>();
            for (int k = 0; k < 4; k++)
            {
                var o = s.UnitAt(x + DX[k], y + DY[k]);
                if (o == null || o.Army != truck.Army || o.Acted || o.Id == truck.Id) continue;
                if (s.Def(o).Domain != Domain.Ground) continue;
                if (o.Fuel == s.Def(o).Fuel && o.Ammo == s.Def(o).Ammo) continue;
                list.Add(o);
            }
            return list;
        }

        static int FuelAmmoCost(GameState s, UnitState u, int count)
        {
            var d = s.Def(u);
            return (d.Fuel - u.Fuel) * count + (d.Ammo - u.Ammo) * d.AmmoPrice * count;
        }

        static void ResetCaptureIfLeft(GameState s, UnitState u)
        {
            for (int i = 0; i < s.CapturingUnit.Length; i++)
                if (s.CapturingUnit[i] == u.Id && (u.IsCarried || i != s.Index(u.X, u.Y)))
                { s.CapturingUnit[i] = -1; s.CaptureProgress[i] = 0; }
        }

        // ---------------- battle ----------------

        static void Battle(GameState s, UnitState att, UnitState def, ApplyResult r)
        {
            var ev = new BattleEvent { AttackerId = att.Id, DefenderId = def.Id, AttackerCountBefore = att.Count, DefenderCountBefore = def.Count };
            Combat.Roll(s, att, att.X, att.Y, def, out int toDef, out int toAtt, out bool counter);
            def.Hp -= toDef;
            att.Ammo--;
            if (counter) { att.Hp -= toAtt; def.Ammo--; }
            DamageCargo(s, def, toDef);
            if (counter) DamageCargo(s, att, toAtt);
            ev.ToDefender = toDef; ev.ToAttacker = toAtt; ev.Counter = counter;
            ev.AttackerCountAfter = Math.Max(0, att.Count); ev.DefenderCountAfter = Math.Max(0, def.Count);
            ev.DefenderDestroyed = def.Hp <= 0; ev.AttackerDestroyed = att.Hp <= 0;
            r.Events.Add(ev);
            if (def.Hp <= 0) RemoveUnit(s, def, r, "撃破");
            if (att.Hp <= 0) RemoveUnit(s, att, r, "撃破");
        }

        static void DamageCargo(GameState s, UnitState transport, int dmg)
        {
            if (dmg <= 0) return;
            foreach (var id in transport.Cargo) { var c = s.UnitById(id); if (c != null) c.Hp = Math.Max(0, c.Hp - dmg); }
            var dead = new List<UnitState>();
            foreach (var id in transport.Cargo) { var c = s.UnitById(id); if (c != null && c.Hp <= 0) dead.Add(c); }
            foreach (var c in dead) { transport.Cargo.Remove(c.Id); RemoveUnit(s, c, null, null); }
        }

        static void RemoveUnit(GameState s, UnitState u, ApplyResult r, string reason)
        {
            foreach (var id in new List<int>(u.Cargo)) { var c = s.UnitById(id); if (c != null) RemoveUnit(s, c, r, reason); }
            if (u.IsCarried) { var t = s.UnitById(u.CarriedBy); if (t != null) t.Cargo.Remove(u.Id); }
            for (int i = 0; i < s.CapturingUnit.Length; i++)
                if (s.CapturingUnit[i] == u.Id) { s.CapturingUnit[i] = -1; s.CaptureProgress[i] = 0; }
            s.Units.Remove(u);
            if (r != null && reason != null) r.Events.Add(new UnitLostEvent { UnitId = u.Id, Reason = reason });
        }

        static void CheckRout(GameState s, ApplyResult r)
        {
            if (s.GameOver) return;
            for (int a = 0; a < 2; a++)
            {
                if (!s.HadUnits[a]) continue;
                bool any = false;
                foreach (var u in s.Units) if ((int)u.Army == a) { any = true; break; }
                if (!any) { Finish(s, Other((Army)a), "全滅", r); return; }
            }
        }

        static void Finish(GameState s, Army winner, string reason, ApplyResult r)
        {
            if (s.GameOver) return;
            s.GameOver = true;
            s.Winner = winner;
            r.Events.Add(new GameOverEvent { Winner = winner, Reason = reason });
        }

        // ---------------- production ----------------

        public static bool InProductionRange(GameState s, Army army, int x, int y)
        {
            int rad = s.Rules.ProductionRadius;
            for (int yy = y - rad; yy <= y + rad; yy++)
                for (int xx = x - rad; xx <= x + rad; xx++)
                    if (s.InBounds(xx, yy) && s.TerrainAt(xx, yy).Id == "HQ" && s.Owner[s.Index(xx, yy)] == army) return true;
            return false;
        }

        public static List<UnitDef> ProducibleAt(GameState s, Army army, int x, int y)
        {
            var list = new List<UnitDef>();
            if (!s.InBounds(x, y)) return list;
            var t = s.TerrainAt(x, y);
            if (!t.IsProperty || t.Produces == null || s.Owner[s.Index(x, y)] != army) return list;
            if (!InProductionRange(s, army, x, y) || s.UnitAt(x, y) != null) return list;
            foreach (var d in s.Data.Units) if (d.Domain == t.Produces.Value) list.Add(d);
            return list;
        }

        static string CheckProduce(GameState s, ProduceCommand c, out UnitDef def)
        {
            def = null;
            if (!s.InBounds(c.X, c.Y)) return "マップの外です";
            var t = s.TerrainAt(c.X, c.Y);
            if (!t.IsProperty || t.Produces == null) return "ここでは生産できません";
            if (s.Owner[s.Index(c.X, c.Y)] != c.Army) return "自軍の施設ではありません";
            if (!InProductionRange(s, c.Army, c.X, c.Y)) return "基地から遠すぎます";
            if (s.UnitAt(c.X, c.Y) != null) return "施設の上に部隊がいます";
            try { def = s.Data.Unit(c.UnitType); } catch (KeyNotFoundException) { return "不明な部隊です"; }
            if (def.Domain != t.Produces.Value) return "この施設では生産できない部隊です";
            if (s.Funds[(int)c.Army] < def.Price) return "資金が足りません";
            if (UnitCount(s, c.Army) >= s.Rules.UnitLimit) return "部隊数が上限(" + s.Rules.UnitLimit + ")に達しています";
            return null;
        }

        /// <summary>All units of the army, carried ones included (they count toward the limit).</summary>
        public static int UnitCount(GameState s, Army army)
        {
            int n = 0;
            foreach (var u in s.Units) if (u.Army == army) n++;
            return n;
        }

        static void ExecProduce(GameState s, ProduceCommand c, ApplyResult r)
        {
            CheckProduce(s, c, out var def);
            s.Funds[(int)c.Army] -= def.Price;
            var u = s.SpawnUnit(def.Index, c.Army, c.X, c.Y, 100, true);
            r.Events.Add(new ProducedEvent { UnitId = u.Id });
        }

        // ---------------- resupply all ----------------

        public static string CheckResupply(GameState s, out List<UnitState> targets, out int cost)
        {
            targets = new List<UnitState>(); cost = 0;
            var a = s.Active;
            if (s.ResupplyUsed[(int)a]) return "全補はこのフェーズで使用済みです";
            foreach (var u in s.Units)
            {
                if (u.Army != a || u.Acted || u.IsCarried) continue;
                var d = s.Def(u);
                int idx = s.Index(u.X, u.Y);
                var t = s.TerrainAt(u.X, u.Y);
                bool atFacility = t.IsProperty && s.Owner[idx] == a && t.Supplies == d.Domain;
                bool byTruck = !atFacility && d.Domain == Domain.Ground && NextToTruck(s, u);
                if (!atFacility && !byTruck) continue;
                int addHp = atFacility ? Math.Min(s.Rules.ResupplyHp, 100 - u.Hp) : 0;
                int newCount = (u.Hp + addHp + 9) / 10;
                int addedMachines = newCount - u.Count;
                int c = FuelAmmoCost(s, u, newCount) + addedMachines * d.Price / 10;
                if (c == 0 && addHp == 0) continue;
                targets.Add(u);
                cost += c;
            }
            if (targets.Count == 0) return "全補できる部隊がいません";
            if (cost > s.Funds[(int)a]) return "資金が足りないため全補できません(必要 " + cost + ")";
            return null;
        }

        static bool NextToTruck(GameState s, UnitState u)
        {
            for (int k = 0; k < 4; k++)
            {
                var o = s.UnitAt(u.X + DX[k], u.Y + DY[k]);
                if (o != null && o.Army == u.Army && s.Def(o).CanSupply) return true;
            }
            return false;
        }

        static void ExecResupply(GameState s, ApplyResult r)
        {
            CheckResupply(s, out var targets, out var cost);
            var a = s.Active;
            var ev = new ResuppliedEvent { Cost = cost };
            foreach (var u in targets)
            {
                var d = s.Def(u);
                var t = s.TerrainAt(u.X, u.Y);
                bool atFacility = t.IsProperty && s.Owner[s.Index(u.X, u.Y)] == a && t.Supplies == d.Domain;
                if (atFacility) u.Hp = Math.Min(100, u.Hp + s.Rules.ResupplyHp);
                u.Fuel = d.Fuel; u.Ammo = d.Ammo;
                u.Acted = true;
                ev.UnitIds.Add(u.Id);
            }
            s.Funds[(int)a] -= cost;
            s.ResupplyUsed[(int)a] = true;
            r.Events.Add(ev);
        }

        // ---------------- phases ----------------

        static void EndPhase(GameState s, ApplyResult r)
        {
            var next = Other(s.Active);
            if (s.Active == Army.Blue)
            {
                s.Day++;
                if (s.Day > s.Rules.TimeLimit) { Finish(s, Army.None, "時間切れ", r); return; }
            }
            StartPhase(s, next, r);
        }

        static void StartPhase(GameState s, Army a, ApplyResult r)
        {
            s.Active = a;
            s.ResupplyUsed[(int)a] = false;
            int income = 0;
            for (int i = 0; i < s.Owner.Length; i++)
                if (s.Owner[i] == a) income += s.Data.Terrains[s.Terrain[i]].Income;
            s.Funds[(int)a] += income;
            r.Events.Add(new PhaseStartEvent { Army = a, Day = s.Day, Income = income });

            var lost = new List<UnitState>();
            foreach (var u in s.Units)
            {
                if (u.Army != a) continue;
                u.Acted = false;
                if (u.IsCarried) continue;
                var d = s.Def(u);
                var t = s.TerrainAt(u.X, u.Y);
                bool home = t.IsProperty && s.Owner[s.Index(u.X, u.Y)] == a && t.Supplies == d.Domain;
                if (d.FuelPerPhase > 0 && !home) u.Fuel = Math.Max(0, u.Fuel - d.FuelPerPhase);
                if ((d.Domain == Domain.Air || d.Domain == Domain.Sea) && u.Fuel <= 0 && !home) lost.Add(u);
            }
            foreach (var u in lost) RemoveUnit(s, u, r, s.Def(u).Domain == Domain.Air ? "墜落" : "沈没");
            CheckRout(s, r);
        }
    }
}
