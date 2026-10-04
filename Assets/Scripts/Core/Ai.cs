using System;
using System.Collections.Generic;

namespace FamiconWars.Core
{
    /// <summary>Evaluation terms the COM may use. Difficulty = which terms are on + how far it searches.</summary>
    [Flags]
    public enum AiFeature
    {
        None = 0,
        Damage = 1,           // value of damage dealt / taken (always on)
        Capture = 2,          // value of capture progress
        Advance = 4,          // moving toward objectives
        Threat = 8,           // damage enemies can deal to the tile next phase (1-ply reply)
        Terrain = 16,         // defensive terrain
        Fuel = 32,            // aircraft return before running dry
        Guard = 64,           // protect own HQ, punish enemy capturers
        Resupply = 128,       // use 全補
        SmartProduction = 256,// production by matchup against the enemy army
        GlobalOrder = 512,    // pick which unit moves next (search over move order)
        Lookahead = 1024,     // 2-step search: best action + best follow-up in a simulated state
        Economy = 2048,       // infantry only while there is something to capture; save up instead of filling slots with cheap units; respect a crowded staging area
        Formation = 4096,     // act before moving, move the front first, keep bridges and lanes clear
    }

    public sealed class AiProfile
    {
        public int Level;
        public string Name;
        public AiFeature Features;
        public int Noise;            // random score noise (gold units)
        public int LookaheadWidth = 3;
        public float LookaheadFollow = 0.8f;   // weight of the best follow-up in the 2-step search
        public bool FocusFire = false;
        public int GuardRange = 2;             // enemy infantry this far beyond their move from our HQ puts a guard on it         // reward the first hit on a unit the rest of the army can then finish
        public float FocusBonus = 0.3f;
        public float WDamage = 1f, WLoss = 1f, WCapture = 1f, WAdvance = 1f, WThreat = 0.6f, WTerrain = 1f;
        // Economy knobs (tuned with Tools/Sim: "eco" experiment, 60 games per variant)
        public bool EcoSave = false;          // save for a better unit instead of buying a cheap one
        public int EcoCrowd = 99;             // skip a facility with this many own units within 2 tiles
        public float EcoInfantry = 0.02f;    // infantry weight once capturing is covered
        public float EcoQuality = 0.5f;      // exponent on price/6000 for combat units
        public int EcoWantCapLate = 2;       // capturers wanted after day 10
        public float EcoInfShare = 0.5f;     // past capturing, keep infantry under this share of the army
        public int EcoSharpen = 1;           // 2 = square the weights (more decisive picks)
        public float EcoSlotStart = 0.7f;
        public bool EcoSlotModel = true;     // tilt production toward strength per unit by how scarce slots / front tiles are
        public float EcoSlotGain = 1.5f;     // how hard the tilt is at full pressure
        public bool EcoSlotSave = false;
        public float EcoJam = 0.6f;          // front congestion at which foot soldiers stop being built (unless needed to capture)
        public int EcoJamSlots = 8;          // ...or when this few unit slots remain     // under pressure, save for the strong unit instead of buying a cheap one
        public float LastPress;              // diagnostics: slot pressure at the last production decision
        public float EcoSlotFrom = 0.4f;     // fraction of the unit limit where slot pressure starts (full at +0.5)
        public bool RotateWeak = true;
        public int EcoLateCapRange = 8;      // any day: build a capturer for a property this close that no own infantry is nearer to (0 = off)
        public bool JoinAfterAct = true;       // only join units that have already acted this phase
        public bool JoinCapture = true;        // join a capturing squad: the capture gauge is kept, so the capture finishes sooner
        public bool TransportTactics = true;   // early: a truck carries infantry to far properties; afterwards it stands in front of them as a wall       // a battered front unit steps back so a fresh one can take its tile      // infantry share / quality rules apply only once the army reaches this fraction of the unit limit (0 = always)

        public bool Has(AiFeature f) => (Features & f) != 0;
        public int Depth => Has(AiFeature.Lookahead) ? 3 : Has(AiFeature.GlobalOrder) ? 2 : Has(AiFeature.Threat) ? 1 : 0;

        public static readonly string[] Names = { "弱い", "普通", "強い", "最強" };

        public static AiProfile ForLevel(int level)
        {
            level = Math.Max(1, Math.Min(4, level));
            var basic = AiFeature.Damage | AiFeature.Capture | AiFeature.Advance;
            var normal = basic | AiFeature.Threat | AiFeature.Terrain | AiFeature.Fuel | AiFeature.Resupply | AiFeature.SmartProduction;
            var strong = normal | AiFeature.Guard | AiFeature.GlobalOrder | AiFeature.Economy | AiFeature.Formation;
            switch (level)
            {
                case 1: return new AiProfile { Level = 1, Name = Names[0], Features = basic, Noise = 2500 };
                case 2: return new AiProfile { Level = 2, Name = Names[1], Features = normal, Noise = 400, WAdvance = 1.6f };
                case 3: return new AiProfile { Level = 3, Name = Names[2], Features = strong, Noise = 0, WAdvance = 1.6f };
                // 最強: decisive production (no gambling on low-weight units) and a full-weight follow-up in the 2-step search
                default: return new AiProfile { Level = 4, Name = Names[3], Features = strong | AiFeature.Lookahead, Noise = 0, WAdvance = 1.6f, EcoSharpen = 2, LookaheadFollow = 1.0f };
            }
        }
    }

    /// <summary>
    /// Computer player. Call Next() repeatedly during its phase; each call returns one command
    /// (the controller applies it, shows it, and calls again). Never modifies the real GameState.
    /// </summary>
    public sealed class AiPlayer
    {
        public readonly Army Army;
        public readonly AiProfile Profile;
        Rng rng;
        GameData simData;
        int phaseKey = -1;
        int commandsThisPhase;
        bool resupplyConsidered;
        readonly HashSet<int> forceWait = new HashSet<int>();
        readonly HashSet<int> done = new HashSet<int>();
        readonly HashSet<int> productionTried = new HashSet<int>();
        const int Inf = 1 << 20;

        public AiPlayer(Army army, AiProfile profile, uint seed)
        {
            Army = army; Profile = profile; rng = new Rng(seed);
        }

        Army Foe => Army == Army.Red ? Army.Blue : Army.Red;

        public Command Next(GameState s)
        {
            if (s.GameOver || s.Active != Army) return null;
            int key = s.Day * 2 + (int)s.Active;
            if (key != phaseKey)
            {
                phaseKey = key; commandsThisPhase = 0; resupplyConsidered = false; savingThisPhase = false; lateCapThisPhase = false;
                forceWait.Clear(); done.Clear(); productionTried.Clear();
            }
            if (++commandsThisPhase > 300) return new EndPhaseCommand { Army = Army };
            if (simData == null)
            {
                var r = s.Data.Rules.Clone();
                r.RandMin = r.RandMax = (s.Data.Rules.RandMin + s.Data.Rules.RandMax) / 2;
                simData = s.Data.WithRules(r);
            }

            if (!resupplyConsidered)
            {
                resupplyConsidered = true;
                if (Profile.Has(AiFeature.Resupply) && ShouldResupply(s)) return new ResupplyAllCommand { Army = Army };
            }

            foreach (var u in s.Units)
                if (u.Army == Army && !u.Acted && !u.IsCarried && forceWait.Contains(u.Id) && done.Add(u.Id))
                    return new UnitCommand { Army = Army, UnitId = u.Id, ToX = u.X, ToY = u.Y, Action = UnitAction.Wait };

            var cmd = ChooseUnitCommand(s);
            if (cmd != null) return cmd;
            var p = ChooseProduction(s);
            if (p != null) return p;
            return new EndPhaseCommand { Army = Army };
        }

        /// <summary>Tell the COM a command was refused so it never loops on it.</summary>
        public void Rejected(Command c)
        {
            if (c is UnitCommand uc) { if (!forceWait.Add(uc.UnitId)) done.Add(uc.UnitId); }
            else if (c is ProduceCommand pc) productionTried.Add(pc.Y * 4096 + pc.X);
        }

        // ================= unit actions =================

        struct Cand { public UnitCommand Cmd; public float Score; }

        Command ChooseUnitCommand(GameState s)
        {
            var units = new List<UnitState>();
            foreach (var u in s.Units)
                if (u.Army == Army && !u.Acted && !u.IsCarried && !forceWait.Contains(u.Id) && !done.Contains(u.Id)) units.Add(u);
            if (units.Count == 0) return null;
            var ctx = new Ctx(s, this);
            if (Profile.Has(AiFeature.Formation) && Profile.RotateWeak)
            {
                if (reliefUnit >= 0)
                {
                    var f = units.Find(u => u.Id == reliefUnit);
                    int tile = reliefTile;
                    reliefUnit = -1;
                    if (f != null)
                    {
                        Cand rb = default; bool found = false;
                        foreach (var c in ctx.Top(f, 64))
                            if (c.Cmd.Action == UnitAction.Attack && s.Index(c.Cmd.ToX, c.Cmd.ToY) == tile && (!found || c.Score > rb.Score)) { rb = c; found = true; }
                        if (found) return rb.Cmd;
                    }
                }
                var rot = ctx.Rotation(units, out reliefUnit, out reliefTile);
                if (rot != null) return rot;
            }

            if (!Profile.Has(AiFeature.GlobalOrder))
            {
                units.Sort((a, b) => OrderKey(s, a).CompareTo(OrderKey(s, b)));
                return ctx.Best(units[0]).Cmd;
            }

            int k = Profile.Has(AiFeature.Lookahead) ? Profile.LookaheadWidth : 1;
            var all = new List<Cand>();
            foreach (var u in units) all.AddRange(ctx.Top(u, k));
            all.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (Profile.Has(AiFeature.Formation))
            {
                // 1) everything that does something (attack, capture, unload, join...) goes before plain moves,
                //    so a unit walking up from the rear never takes the tile a front unit needed to fire from.
                var acts = all.FindAll(c => c.Cmd.Action != UnitAction.Wait && c.Score > 0);
                if (acts.Count > 0) all = acts;
                else
                {
                    // 2) plain moves: the unit closest to the enemy moves first and makes room for the others.
                    UnitState front = null; int fd = int.MaxValue;
                    foreach (var u in units)
                    {
                        int dd = ctx.FrontDistance(u);
                        if (dd < fd) { fd = dd; front = u; }
                    }
                    if (front != null)
                    {
                        var mine = all.FindAll(c => c.Cmd.UnitId == front.Id);
                        if (mine.Count > 0) all = mine;
                    }
                }
            }
            if (!Profile.Has(AiFeature.Lookahead) || all.Count == 1) return all[0].Cmd;

            // 2-step search: apply each top candidate on a copy (fixed mid roll) and add the best follow-up.
            Cand best = all[0];
            float bestTotal = float.MinValue;
            int width = Math.Min(all.Count, Profile.LookaheadWidth * 2);
            for (int i = 0; i < width; i++)
            {
                var c = all[i];
                var sim = s.Clone();
                sim.Data = simData;
                var r = RulesEngine.Apply(sim, c.Cmd);
                if (!r.Ok) continue;
                float follow = 0;
                if (sim.GameOver) follow = sim.Winner == Army ? 1e7f : -1e7f;
                else
                {
                    var ctx2 = new Ctx(sim, this);
                    foreach (var u2 in sim.Units)
                        if (u2.Army == Army && !u2.Acted && !u2.IsCarried && !forceWait.Contains(u2.Id) && !done.Contains(u2.Id))
                            follow = Math.Max(follow, ctx2.Best(u2).Score);
                }
                float total = c.Score + Profile.LookaheadFollow * follow;
                if (total > bestTotal) { bestTotal = total; best = c; }
            }
            return best.Cmd;
        }

        static int OrderKey(GameState s, UnitState u)
        {
            var d = s.Def(u);
            int k = d.IsIndirect ? 0 : s.CapturingUnit[s.Index(u.X, u.Y)] == u.Id ? 1 : d.RangeMax > 0 ? 2 : 3;
            return k * 100000 - d.Price;
        }

        bool ShouldResupply(GameState s)
        {
            int urgent = 0;
            foreach (var u in s.Units)
            {
                if (u.Army != Army || u.Acted || u.IsCarried) continue;
                var d = s.Def(u);
                var t = s.TerrainAt(u.X, u.Y);
                if (!(t.IsProperty && s.Owner[s.Index(u.X, u.Y)] == Army && t.Supplies == d.Domain)) continue;
                if (u.Hp <= 60 || (d.Ammo > 0 && u.Ammo == 0) || (d.FuelPerPhase > 0 && u.Fuel < d.Fuel / 2)) urgent++;
            }
            if (urgent == 0) return false;
            if (RulesEngine.CheckResupply(s, out var targets, out var cost) != null) return false;
            return urgent * 2 >= targets.Count && cost <= s.Funds[(int)Army];
        }

        // ================= production =================

        Command ChooseProduction(GameState s)
        {
            // blue scans from the far corner so that both armies try their facilities in the same order
            // relative to the front on a point-symmetric map
            bool rev = Army == Army.Blue;
            for (int yi = 0; yi < s.Height; yi++)
                for (int xi = 0; xi < s.Width; xi++)
                {
                    int y = rev ? s.Height - 1 - yi : yi, x = rev ? s.Width - 1 - xi : xi;
                    int key = y * 4096 + x;
                    if (productionTried.Contains(key)) continue;
                    var list = RulesEngine.ProducibleAt(s, Army, x, y);
                    if (list.Count == 0) continue;
                    productionTried.Add(key);
                    if (RulesEngine.UnitCount(s, Army) >= s.Rules.UnitLimit) return null;
                    var pick = PickUnit(s, list, x, y);
                    if (pick != null) return new ProduceCommand { Army = Army, X = x, Y = y, UnitType = pick.Id };
                }
            return null;
        }

        int FreeFacilities(GameState s)
        {
            int n = 0;
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                    if (!productionTried.Contains(y * 4096 + x) && RulesEngine.ProducibleAt(s, Army, x, y).Count > 0) n++;
            return n;
        }

        bool savingThisPhase, lateCapThisPhase;
        int reliefUnit = -1, reliefTile = -1;

        UnitDef PickUnit(GameState s, List<UnitDef> list, int fx, int fy)
        {
            int funds = s.Funds[(int)Army];
            if (savingThisPhase) return null;
            var aff = list.FindAll(d => d.Price <= funds);
            if (aff.Count == 0) return null;
            if (Profile.Has(AiFeature.Economy)) return PickEconomic(s, list, aff, fx, fy);

            if (!Profile.Has(AiFeature.SmartProduction))
            {
                var inf = aff.Find(d => d.Capture >= 2);
                if (inf != null && rng.Range(0, 99) < 50) return inf;
                return aff[rng.Range(0, aff.Count - 1)];
            }

            // Smart production = the same infantry-heavy mix, but weighted toward units that counter the
            // enemy's current army and away from units with nothing to shoot at. (Picking the single
            // "best" unit every time measured far weaker: armies became one-note and easy to counter.)
            int capTargets = 0, capturers = 0;
            for (int i = 0; i < s.Owner.Length; i++)
            {
                var t = s.Data.Terrains[s.Terrain[i]];
                if (t.IsProperty && t.Id != "HQ" && s.Owner[i] != Army) capTargets++;
            }
            var enemies = new List<(UnitDef d, int hp)>();
            foreach (var u in s.Units)
            {
                if (u.Army == Army) { if (s.Def(u).Capture > 0) capturers++; }
                else if (!u.IsCarried) enemies.Add((s.Def(u), u.Hp));
            }

            var weights = new float[aff.Count];
            float sum = 0;
            for (int k = 0; k < aff.Count; k++)
            {
                var t = aff[k];
                float wgt = 1f;
                if (t.Capture >= 2) wgt = aff.Count * (capturers < Math.Min(capTargets, 3 + capTargets / 3) ? 1.5f : 0.6f);
                if (enemies.Count > 0 && t.RangeMax > 0)
                {
                    float give = 0, take = 0, hits = 0;
                    foreach (var e in enemies)
                    {
                        int g = s.Data.BaseDamage(t, e.d);
                        if (g >= 0) { give += g / 100f * e.d.Price * e.hp / 100f; hits++; }
                        int tk = s.Data.BaseDamage(e.d, t);
                        if (tk >= 0) take += tk / 100f * t.Price * e.hp / 100f;
                    }
                    if (hits == 0) wgt *= 0.1f;                       // nothing to shoot at (e.g. AA vs no air)
                    else wgt *= Math.Max(0.3f, Math.Min(3f, (give + 1) / (take + 1)));
                }
                if (t.CargoCapacity > 0 && t.Domain != Domain.Air && !NeedTransport(s, t)) wgt *= 0.3f;
                if (t.CanSupply && !NeedSupply(s)) wgt *= 0.2f;
                weights[k] = wgt;
                sum += wgt;
            }
            int roll = rng.Range(0, 9999);
            float pick = roll / 10000f * sum;
            for (int k = 0; k < aff.Count; k++)
            {
                pick -= weights[k];
                if (pick <= 0) return aff[k];
            }
            return aff[aff.Count - 1];
        }


        /// <summary>
        /// Production with discipline. Infantry fight only adjacent tiles, so past the capturing phase a
        /// tile spent on infantry is a tile not spent on a gun: infantry are bought only while there are
        /// properties to take. Money is saved for the unit the situation calls for instead of being spent
        /// on whatever is affordable, and nothing is built while the staging area is already jammed.
        /// </summary>
        UnitDef PickEconomic(GameState s, List<UnitDef> list, List<UnitDef> aff, int fx, int fy)
        {
            int myCount = 0, foeCount = 0, capturers = 0, capTargets = 0, infantry = 0;
            var enemies = new List<(UnitDef d, int hp)>();
            foreach (var u in s.Units)
            {
                if (u.Army == Army) { myCount++; if (s.Def(u).Capture >= 2) { capturers++; infantry++; } }
                else { foeCount++; if (!u.IsCarried) enemies.Add((s.Def(u), u.Hp)); }
            }
            for (int i = 0; i < s.Owner.Length; i++)
            {
                var t = s.Data.Terrains[s.Terrain[i]];
                if (t.IsProperty && t.Id != "HQ" && s.Owner[i] != Army) capTargets++;
            }
            float infShare = myCount > 0 ? infantry / (float)myCount : 0;
            // slots are only scarce once the army nears the unit limit: before that, cheap and many is fine
            bool slotPressure = myCount >= s.Rules.UnitLimit * Profile.EcoSlotStart;
            if (!slotPressure) infShare = 0;
            int wantCap = Math.Min(capTargets, 2 + capTargets / 3);
            if (s.Day > 10) wantCap = Math.Min(wantCap, Profile.EcoWantCapLate);
            bool needInf = capturers < wantCap;
            // Late game: infantry are still the only way to take a base. A property close to this facility
            // that no foot soldier of ours is nearer to, and that the enemy is not standing on or right next
            // to, gets one capturer, at any stage of the game and regardless of how full the front is.
            if (!needInf && !lateCapThisPhase && Profile.EcoLateCapRange > 0 && LateCaptureTarget(s, fx, fy)) { needInf = true; lateCapThisPhase = true; }

            // ---- how scarce is a unit slot? (unit limit headroom, and how jammed the front is) ----
            float pSlots = Clamp01((myCount - s.Rules.UnitLimit * Profile.EcoSlotFrom) / (s.Rules.UnitLimit * 0.5f));
            float pFront = FrontCongestion(s);
            float press = Math.Max(pSlots, pFront);
            Profile.LastPress = press;

            // jammed staging area: units we already have cannot get out of the way
            int crowd = 0;
            foreach (var u in s.Units)
                if (u.Army == Army && !u.IsCarried && Movement.Distance(u.X, u.Y, fx, fy) <= 2 && (u.X != fx || u.Y != fy)) crowd++;
            if (crowd >= Profile.EcoCrowd && myCount >= foeCount && !needInf) return null;

            // The proven weighting, then tilted toward strength per unit as slots get scarce:
            // with room to spare nothing changes; near the limit or with a jammed front, a unit that deals
            // more HP and takes less (tanks, guns, aircraft) outweighs a cheap one.
            float Weight(UnitDef t)
            {
                float bw = BaseWeight(t);
                if (!Profile.EcoSlotModel || press <= 0 || (t.Capture >= 2 && needInf)) return bw;
                bw *= (float)Math.Pow(SlotEff(t), press * Profile.EcoSlotGain);
                // truly jammed (front packed, or only a few slots left): another rifle squad cannot even reach a fight
                if (t.RangeMax == 1 && t.MoveClass == MoveClass.Foot && (pFront >= Profile.EcoJam || s.Rules.UnitLimit - myCount <= Profile.EcoJamSlots)) bw *= 0.1f;
                return bw;
            }

            float BaseWeight(UnitDef t)
            {
                float wgt = 1f;
                // early on the race for properties decides the game: infantry dominate while there is land to take
                if (t.Capture >= 2) return needInf ? aff.Count * 1.5f : infShare < Profile.EcoInfShare ? 1.5f : Profile.EcoInfantry;
                if (t.Capture == 1) wgt = needInf ? 1.2f : 0.5f;
                if (t.RangeMax > 0)
                {
                    if (enemies.Count == 0) wgt *= 1f;
                    else
                    {
                        float give = 0, take = 0, hits = 0;
                        foreach (var e in enemies)
                        {
                            int g = s.Data.BaseDamage(t, e.d);
                            if (g >= 0) { give += g / 100f * e.d.Price * e.hp / 100f; hits++; }
                            int tk = s.Data.BaseDamage(e.d, t);
                            if (tk >= 0) take += tk / 100f * t.Price * e.hp / 100f;
                        }
                        if (hits == 0) return 0.02f;
                        wgt *= Math.Max(0.3f, Math.Min(3f, (give + 1) / (take + 1)));
                    }
                    // a tile on the front is the scarce resource: favour strength per unit
                    if (slotPressure) wgt *= (float)Math.Pow(Math.Max(1000, t.Price) / 6000.0, Profile.EcoQuality);
                }
                else wgt *= 0.3f;
                if (t.CargoCapacity > 0 && t.Domain == Domain.Ground && Profile.TransportTactics && EarlyTruck(s, t, fx, fy)) return aff.Count * 1.2f;
                if (t.CargoCapacity > 0 && t.Domain != Domain.Air && !NeedTransport(s, t)) wgt *= 0.2f;
                if (t.CargoCapacity > 0 && t.Domain == Domain.Air && !needInf) wgt *= 0.3f;
                if (t.CanSupply && !NeedSupply(s)) wgt *= 0.1f;
                return wgt;
            }

            // HP dealt / HP taken per unit against the current enemy army, price ignored. Finishing off
            // weakened enemies counts extra; guns and aircraft fight over a jammed front.
            float SlotEff(UnitDef t)
            {
                if (t.RangeMax <= 0 || enemies.Count == 0) return 1f;
                float hits = 0, dealtHp = 0, takenHp = 0, kills = 0, shooters = 0;
                foreach (var e in enemies)
                {
                    int g = s.Data.BaseDamage(t, e.d);
                    if (g >= 0) { hits++; dealtHp += Math.Min(g, e.hp); if (g >= e.hp) kills++; }
                    int tk = s.Data.BaseDamage(e.d, t);
                    if (tk >= 0) { shooters++; takenHp += tk * e.hp / 100f; }
                }
                if (hits == 0) return 0.1f;
                float slot = (dealtHp / hits * (hits / enemies.Count) + 40f * kills / enemies.Count + 5f) / ((shooters > 0 ? takenHp / shooters : 0) + 10f);
                if (t.IsIndirect) slot *= 1f + 0.8f * pFront;
                if (t.Domain == Domain.Air) slot *= 1f + 0.5f * pFront;
                return Math.Max(0.1f, Math.Min(5f, slot));
            }

            // what would we build with unlimited money? if it is within two days' income, save for it
            UnitDef ideal = null; float idealW = 0;
            foreach (var t in list) { float wv = Weight(t); if (wv > idealW) { idealW = wv; ideal = t; } }
            UnitDef bestAff = null; float bestAffW = 0;
            foreach (var t in aff) { float wv = Weight(t); if (wv > bestAffW) { bestAffW = wv; bestAff = t; } }
            if (bestAff == null || bestAffW < 0.05f) return null;
            int income = 0;
            for (int i = 0; i < s.Owner.Length; i++) if (s.Owner[i] == Army) income += s.Data.Terrains[s.Terrain[i]].Income;
            if ((Profile.EcoSave || (Profile.EcoSlotSave && press >= 0.5f)) && ideal != null && ideal.Price > s.Funds[(int)Army] && ideal.Price <= s.Funds[(int)Army] + income * 2
                && bestAffW < idealW * 0.75f && myCount >= foeCount - 1 && !needInf)
            { savingThisPhase = true; return null; }

            // weighted pick among affordable units (keeps the army mixed and hard to counter)
            var weights = new float[aff.Count];
            float sum = 0;
            for (int k = 0; k < aff.Count; k++) { weights[k] = Weight(aff[k]); if (Profile.EcoSharpen > 1) weights[k] = (float)Math.Pow(weights[k], Profile.EcoSharpen); sum += weights[k]; }
            float pick = rng.Range(0, 9999) / 10000f * sum;
            for (int k = 0; k < aff.Count; k++) { pick -= weights[k]; if (pick <= 0) return weights[k] > 0 ? aff[k] : bestAff; }
            return bestAff;
        }

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>
        /// 0 = room to spare, 1 = jammed: own ground units near the enemy compared with the tiles from
        /// which the enemy can actually be attacked. Infantry only fight from those tiles.
        /// </summary>
        float FrontCongestion(GameState s)
        {
            var attackTiles = new HashSet<int>();
            int crowd = 0;
            foreach (var e in s.Units)
            {
                if (e.Army == Army || e.IsCarried) continue;
                for (int k = 0; k < 4; k++)
                {
                    int x = e.X + (k == 0 ? 1 : k == 1 ? -1 : 0), y = e.Y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (!s.InBounds(x, y) || s.TerrainAt(x, y).Cost[(int)MoveClass.Foot] < 0) continue;
                    var o = s.UnitAt(x, y);
                    if (o != null && o.Army != Army) continue;
                    attackTiles.Add(s.Index(x, y));
                }
            }
            if (attackTiles.Count == 0) return 0;
            foreach (var u in s.Units)
            {
                if (u.Army != Army || u.IsCarried || s.Def(u).Domain != Domain.Ground || s.Def(u).RangeMax <= 0) continue;
                foreach (var e in s.Units)
                    if (e.Army != Army && !e.IsCarried && Movement.Distance(u.X, u.Y, e.X, e.Y) <= 3) { crowd++; break; }
            }
            float cap = attackTiles.Count;
            return Clamp01((crowd - cap * 0.6f) / (cap * 0.6f + 1));
        }

        /// <summary>
        /// Is there a property within EcoLateCapRange of this facility that no own foot unit is
        /// at least as close to, and that is not held or screened by an enemy unit?
        /// </summary>
        bool LateCaptureTarget(GameState s, int fx, int fy)
        {
            for (int i = 0; i < s.Owner.Length; i++)
            {
                var t = s.Data.Terrains[s.Terrain[i]];
                if (!t.IsProperty || s.Owner[i] == Army) continue;
                int x = i % s.Width, y = i / s.Width;
                int dist = Movement.Distance(x, y, fx, fy);
                if (dist > Profile.EcoLateCapRange) continue;
                bool covered = false, hot = false;
                foreach (var u in s.Units)
                {
                    if (u.IsCarried) continue;
                    int du = Movement.Distance(u.X, u.Y, x, y);
                    if (u.Army == Army) { if (s.Def(u).Capture >= 2 && du <= dist) { covered = true; break; } }
                    else if (du <= 3 && !s.Def(u).IsIndirect || du <= 1) hot = true;
                }
                if (!covered && !hot) return true;
            }
            return false;
        }

        /// <summary>
        /// Capture race: one truck early on when the properties still to take are two or more infantry
        /// moves away from this factory. It carries a squad out and then shields the capture.
        /// </summary>
        bool EarlyTruck(GameState s, UnitDef t, int fx, int fy)
        {
            if (s.Day > 6) return false;
            foreach (var u in s.Units) if (u.Army == Army && s.Def(u).CargoCapacity > 0 && s.Def(u).Domain == Domain.Ground) return false;
            int foot = 0;
            foreach (var u in s.Units) if (u.Army == Army && s.Def(u).Capture > 0) foot++;
            if (foot == 0) return false;   // nothing to carry yet: infantry first
            int far = 0;
            for (int i = 0; i < s.Owner.Length; i++)
            {
                var ter = s.Data.Terrains[s.Terrain[i]];
                if (!ter.IsProperty || s.Owner[i] == Army || ter.Id == "HQ") continue;
                if (Movement.Distance(i % s.Width, i / s.Width, fx, fy) >= 7) far++;
            }
            return far >= 3;
        }

        bool NeedTransport(GameState s, UnitDef t)
        {
            foreach (var u in s.Units) if (u.Army == Army && s.Def(u).CargoCapacity > 0 && s.Def(u).Domain == t.Domain) return false;
            var ctx = new Ctx(s, this);
            if (t.Domain == Domain.Sea)
            {
                int hq = ctx.OwnHq;
                if (hq < 0) return false;
                return ctx.Dist(MoveClass.Vehicle, "foehq")[hq] >= Inf;
            }
            var cap = ctx.Dist(MoveClass.Foot, "cap2");
            foreach (var u in s.Units)
                if (u.Army == Army && !u.IsCarried && s.Def(u).MoveClass == MoveClass.Foot && cap[s.Index(u.X, u.Y)] > 9 && cap[s.Index(u.X, u.Y)] < Inf) return true;
            return false;
        }

        bool NeedSupply(GameState s)
        {
            int ground = 0; bool low = false;
            foreach (var u in s.Units)
            {
                if (u.Army != Army) continue;
                var d = s.Def(u);
                if (d.CanSupply) return false;
                if (d.Domain != Domain.Ground) continue;
                ground++;
                if ((d.Ammo > 0 && u.Ammo <= 1) || u.Fuel < d.Fuel / 4) low = true;
            }
            return ground >= 6 && low;
        }

        // ================= evaluation context =================

        sealed class Ctx
        {
            readonly GameState s;
            readonly AiPlayer ai;
            readonly AiProfile p;
            readonly Army me, foe;
            readonly int w;
            List<(UnitState e, HashSet<int> tiles)> enemyReach;
            readonly Dictionary<string, int[]> dist = new Dictionary<string, int[]>();
            readonly HashSet<string> emptyDist = new HashSet<string>();
            public readonly int OwnHq = -1;
            readonly bool hqDanger;

            public Ctx(GameState s, AiPlayer ai)
            {
                this.s = s; this.ai = ai; p = ai.Profile; me = ai.Army; foe = ai.Foe; w = s.Width;
                for (int i = 0; i < s.Owner.Length; i++)
                    if (s.Owner[i] == me && s.Data.Terrains[s.Terrain[i]].Id == "HQ") OwnHq = i;
                if (OwnHq >= 0 && p.Has(AiFeature.Guard))
                    foreach (var e in s.Units)
                        if (e.Army == foe && !e.IsCarried && s.Def(e).Capture >= 2 &&
                            Movement.Distance(e.X, e.Y, OwnHq % w, OwnHq / w) <= s.Def(e).Move + p.GuardRange) hqDanger = true;
            }

            float Value(UnitState u) => s.Def(u).Price;

            /// <summary>
            /// Relief in place: a battered unit locked in a fight steps back when a fresh unit can move into
            /// its tile and keep fighting (weak vs weak trades drag on and feed the enemy cheap kills).
            /// Not when it can finish its opponent, or when it is capturing.
            /// </summary>
            public UnitCommand Rotation(List<UnitState> units, out int reliefId, out int reliefAt)
            {
                reliefId = -1; reliefAt = -1;
                foreach (var weak in units)
                {
                    var d = s.Def(weak);
                    if (weak.Hp > 40 || d.Domain != Domain.Ground || d.IsIndirect) continue;
                    int wi = s.Index(weak.X, weak.Y);
                    if (s.CapturingUnit[wi] == weak.Id) continue;
                    bool engaged = false, canKill = false;
                    foreach (var e in s.Units)
                    {
                        if (e.Army != foe || e.IsCarried || Movement.Distance(e.X, e.Y, weak.X, weak.Y) != 1) continue;
                        engaged = true;
                        if (weak.Ammo > 0 && Combat.CanAttackFrom(s, weak, e, weak.X, weak.Y, false) &&
                            Combat.Predict(s, weak, weak.X, weak.Y, e).DamageToDefenderMin >= e.Hp) canKill = true;
                    }
                    if (!engaged || canKill) continue;

                    UnitState fresh = null;
                    foreach (var f in units)
                    {
                        if (f == weak || f.Hp < 80 || f.Ammo <= 0) continue;
                        var fd = s.Def(f);
                        if (fd.Domain != Domain.Ground || fd.IsIndirect || fd.RangeMax <= 0) continue;
                        bool busy = false;
                        foreach (var e in s.Units) if (e.Army == foe && !e.IsCarried && Movement.Distance(e.X, e.Y, f.X, f.Y) == 1) { busy = true; break; }
                        if (busy || !Movement.Reachable(s, f).Reaches(wi)) continue;
                        if (s.TerrainAt(weak.X, weak.Y).Cost[(int)fd.MoveClass] < 0) continue;
                        bool target = false;
                        foreach (var e in s.Units)
                            if (e.Army == foe && !e.IsCarried && Movement.Distance(e.X, e.Y, weak.X, weak.Y) == 1 && s.Data.BaseDamage(fd, s.Def(e)) > 0) { target = true; break; }
                        if (!target) continue;
                        // the fresh unit must trade better from that tile than the battered one would (it usually does:
                        // a full-strength unit hits harder and the battered one would just be a cheap kill next phase)
                        if (ReliefValue(f, weak.X, weak.Y, true) > ReliefValue(weak, weak.X, weak.Y, false) + 100) { fresh = f; break; }
                    }
                    if (fresh == null) continue;

                    var reach = Movement.Reachable(s, weak);
                    int best = -1; float bestSc = float.MinValue;
                    foreach (var kv in Ordered(reach.Cost))
                    {
                        int i = kv.Key, x = i % w, y = i / w;
                        if (i == wi || reach.EndOnly.Contains(i) || Movement.StopAt(s, weak, x, y) != StopKind.Empty) continue;
                        bool adj = false;
                        foreach (var e in s.Units) if (e.Army == foe && !e.IsCarried && Movement.Distance(e.X, e.Y, x, y) == 1) { adj = true; break; }
                        if (adj) continue;
                        var t = s.TerrainAt(x, y);
                        float sc = -Threat(weak, x, y, weak.Hp) + t.DefenseFor(d) * 5;
                        if (t.IsProperty && s.Owner[i] == me && t.Supplies == d.Domain) sc += d.Price * 0.4f;
                        if (sc > bestSc) { bestSc = sc; best = i; }
                    }
                    if (best >= 0 && bestSc > -0.5f * d.Price * weak.Hp / 100f)
                    {
                        reliefId = fresh.Id; reliefAt = wi;
                        return Cmd(weak, best % w, best / w, UnitAction.Wait);
                    }
                }
                return null;
            }

            /// <summary>Best net value (damage dealt minus counter-fire taken, in gold) of attacking from (x, y).</summary>
            float ReliefValue(UnitState u, int x, int y, bool moved)
            {
                if (u.Ammo <= 0) return 0;
                var ghost = u.Clone(); ghost.X = x; ghost.Y = y;
                float best = float.MinValue;
                foreach (var e in s.Units)
                {
                    if (e.Army != foe || e.IsCarried || !Combat.CanAttackFrom(s, ghost, e, x, y, moved)) continue;
                    var f = Combat.Predict(s, ghost, x, y, e);
                    float dmg = (f.DamageToDefenderMin + f.DamageToDefenderMax) / 2f, cd = f.Counter ? (f.DamageToAttackerMin + f.DamageToAttackerMax) / 2f : 0;
                    best = Math.Max(best, dmg / 100f * Value(e) - cd / 100f * Value(u));
                }
                return best == float.MinValue ? 0 : best;
            }

            /// <summary>Distance (in moves) from the unit to the nearest enemy it could fight.</summary>
            public int FrontDistance(UnitState u)
            {
                var d = s.Def(u);
                var dm = Dist(d.MoveClass, "enemy:" + d.Index);
                int v = dm[s.Index(u.X, u.Y)];
                return v >= Inf ? Inf : v / Math.Max(1, d.Move);
            }

            /// <summary>Passable neighbours for this move class: 2 or fewer means a lane or a bridge.</summary>
            int Openness(MoveClass mc, int x, int y)
            {
                int n = 0;
                if (x + 1 < s.Width && s.TerrainAt(x + 1, y).Cost[(int)mc] >= 0) n++;
                if (x > 0 && s.TerrainAt(x - 1, y).Cost[(int)mc] >= 0) n++;
                if (y + 1 < s.Height && s.TerrainAt(x, y + 1).Cost[(int)mc] >= 0) n++;
                if (y > 0 && s.TerrainAt(x, y - 1).Cost[(int)mc] >= 0) n++;
                return n;
            }

            // ---------- candidates ----------

            public Cand Best(UnitState u)
            {
                var top = Top(u, 1);
                return top.Count > 0 ? top[0] : new Cand { Cmd = Cmd(u, u.X, u.Y, UnitAction.Wait), Score = 0 };
            }

            public List<Cand> Top(UnitState u, int k)
            {
                var list = new List<Cand>();
                Enumerate(u, (c, sc) => list.Add(new Cand { Cmd = c, Score = sc }));
                list.Sort((a, b) => b.Score.CompareTo(a.Score));
                // keep at most k, but never two candidates for the same tile+action
                if (list.Count > k) list.RemoveRange(k, list.Count - k);
                return list;
            }

            UnitCommand Cmd(UnitState u, int x, int y, UnitAction a) =>
                new UnitCommand { Army = me, UnitId = u.Id, ToX = x, ToY = y, Action = a };

            void Enumerate(UnitState u, Action<UnitCommand, float> emit)
            {
                var d = s.Def(u);
                var reach = Movement.Reachable(s, u);
                int start = s.Index(u.X, u.Y);
                foreach (var kv in Ordered(reach.Cost))
                {
                    int i = kv.Key, x = i % w, y = i / w;
                    var stop = Movement.StopAt(s, u, x, y);
                    if (stop == StopKind.None) continue;
                    if (reach.EndOnly.Contains(i) && stop != StopKind.Load) continue;
                    bool moved = i != start;

                    if (stop == StopKind.Load) { emit(Cmd(u, x, y, UnitAction.Load), ScoreLoad(u, start)); continue; }
                    if (stop == StopKind.Join)
                    {
                        var o = FindOther(u, x, y);
                        if (o == null) continue;
                        // walking off a half-taken property throws its progress away
                        float leave = moved && s.CapturingUnit[start] == u.Id ? PropValue(start) * s.CaptureProgress[start] / (float)s.Rules.CaptureGoal + 200 : 0;
                        bool capturer = s.CapturingUnit[i] == o.Id;
                        // a join leaves the merged unit done for the phase: never take away an action the
                        // other unit still has (a capturer captures first, then gets joined)
                        if (!o.Acted && (capturer || p.JoinAfterAct)) continue;
                        if (p.Has(AiFeature.Capture) && p.JoinCapture && d.Capture > 0 && capturer && o.Hp < 100)
                        {
                            // the capture gauge survives a join: shield the squad that just captured so the
                            // enemy cannot wipe out its progress, and capture with more men next turn
                            float jc = JoinCaptureScore(u, d, o, i);
                            if (jc > 0) emit(Cmd(u, x, y, UnitAction.Join), jc - leave + Positional(u, d, start, i, kv.Value, Math.Min(100, u.Hp + o.Hp)) * 0.5f);
                            continue;
                        }
                        else if (u.Hp <= 40 && u.Hp + o.Hp <= 110) emit(Cmd(u, x, y, UnitAction.Join), 300 - leave + Positional(u, d, start, i, kv.Value, o.Hp) * 0.5f);
                        else if (p.Has(AiFeature.Formation) && u.Hp <= 70 && o.Hp < 100 && u.Hp + o.Hp <= 110)
                        {
                            // two half-strength units hold one tile at full strength and stop being easy kills
                            float gain = 0.25f * d.Price * Math.Min(u.Hp, 100 - o.Hp) / 100f;
                            emit(Cmd(u, x, y, UnitAction.Join), 150 + gain - leave + Positional(u, d, start, i, kv.Value, Math.Min(100, u.Hp + o.Hp)) * 0.5f);
                        }
                        continue;
                    }

                    float pos = Positional(u, d, start, i, kv.Value, u.Hp);
                    emit(Cmd(u, x, y, UnitAction.Wait), pos);

                    // capture
                    var t = s.TerrainAt(x, y);
                    if (d.Capture > 0 && t.IsProperty && s.Owner[i] != me && (t.Id != "HQ" || d.Capture >= 2))
                        emit(Cmd(u, x, y, UnitAction.Capture), pos + CaptureScore(u, i));

                    // attack
                    if (d.RangeMax > 0 && u.Ammo > 0 && !(d.IsIndirect && moved))
                    {
                        var ghost = u.Clone(); ghost.X = x; ghost.Y = y;
                        foreach (var e in s.Units)
                        {
                            if (e.Army != foe || e.IsCarried) continue;
                            if (!Combat.CanAttackFrom(s, ghost, e, x, y, moved)) continue;
                            var f = Combat.Predict(s, ghost, x, y, e);
                            int dmg = (f.DamageToDefenderMin + f.DamageToDefenderMax) / 2;
                            int cdmg = f.Counter ? (f.DamageToAttackerMin + f.DamageToAttackerMax) / 2 : 0;
                            int hpAfter = Math.Max(0, u.Hp - cdmg);
                            float sc = AttackScore(u, e, dmg, cdmg) + (hpAfter > 0 ? Positional(u, d, start, i, kv.Value, hpAfter) : 0);
                            var c = Cmd(u, x, y, UnitAction.Attack); c.TargetId = e.Id;
                            emit(c, sc);
                        }
                    }

                    // supply
                    if (d.CanSupply)
                    {
                        float sv = SupplyScore(u, x, y);
                        if (sv > 0) emit(Cmd(u, x, y, UnitAction.Supply), pos + sv);
                    }

                    // unload
                    if (u.Cargo.Count > 0 && t.Unload && !(d.Domain == Domain.Sea && !Movement.IsShoreForShip(s, x, y)))
                    {
                        foreach (var cid in u.Cargo)
                        {
                            var cargo = s.UnitById(cid);
                            if (cargo == null) continue;
                            var cd = s.Def(cargo);
                            for (int k = 0; k < 4; k++)
                            {
                                int dx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), dy = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                                if (!s.InBounds(dx, dy)) continue;
                                var occ = s.UnitAt(dx, dy);
                                if (occ != null && occ.Id != u.Id) continue;
                                if (s.TerrainAt(dx, dy).Cost[(int)cd.MoveClass] < 0 || !s.TerrainAt(dx, dy).Unload) continue;
                                var c = Cmd(u, x, y, UnitAction.Unload); c.CargoId = cid; c.DropX = dx; c.DropY = dy;
                                emit(c, pos + UnloadScore(cargo, i, s.Index(dx, dy)));
                            }
                        }
                    }
                }
            }

            UnitState FindOther(UnitState u, int x, int y)
            {
                foreach (var o in s.Units) if (!o.IsCarried && o.Id != u.Id && o.X == x && o.Y == y) return o;
                return null;
            }

            // ---------- scoring terms ----------

            float Positional(UnitState u, UnitDef d, int start, int dest, int moveCost, int hp)
            {
                float sc = 0;
                int x = dest % w, y = dest / w;
                if (p.Has(AiFeature.Advance))
                {
                    var dm = DistFor(u);
                    int a = dm[start], b = dm[dest];
                    if (a < Inf && b < Inf) sc += p.WAdvance * 450f * (a - b) / Math.Max(1, d.Move);
                    if (d.IsIndirect && b < d.RangeMin && dest != start) sc -= 300;
                }
                var t = s.TerrainAt(x, y);
                if (p.Has(AiFeature.Terrain) && d.Domain != Domain.Air)
                    sc += p.WTerrain * t.DefenseFor(d) / 100f * d.Price * 0.2f * hp / 100f;
                if (p.Has(AiFeature.Threat)) sc -= p.WThreat * Threat(u, x, y, hp);
                if (p.Has(AiFeature.Fuel) && d.FuelPerPhase > 0)
                {
                    int left = u.Fuel - moveCost;
                    int home = Dist(MoveClass.Air, "airport")[dest];
                    if (home >= Inf) home = 99;
                    if (left - d.FuelPerPhase * 2 < home) sc -= d.Price * 0.6f;
                }
                if (t.Produces != null && s.Owner[dest] == me && RulesEngine.InProductionRange(s, me, x, y)) sc -= 3000;
                if (p.TransportTactics && d.CargoCapacity > 0 && d.Domain == Domain.Ground && u.Cargo.Count == 0)
                {
                    // a cheap truck standing between the enemy and a capturing squad buys the capture a day
                    int[] bx = { 1, -1, 0, 0 }, by = { 0, 0, 1, -1 };
                    var fd = Dist(MoveClass.Vehicle, "enemy:" + d.Index);
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + bx[k], ny = y + by[k];
                        if (!s.InBounds(nx, ny)) continue;
                        var o = s.UnitAt(nx, ny);
                        if (o == null || o.Army != me || s.CapturingUnit[s.Index(nx, ny)] != o.Id) continue;
                        sc += 500;
                        if (fd[dest] < fd[s.Index(nx, ny)]) sc += 400;   // on the enemy's side of it
                    }
                }
                if (p.Has(AiFeature.Formation) && hp <= 60 && t.IsProperty && s.Owner[dest] == me && t.Supplies == d.Domain)
                    sc += d.Price * 0.25f * (100 - hp) / 100f;   // battered units head for a city to be repaired
                if (p.Has(AiFeature.Formation) && d.Domain == Domain.Ground && dest != start)
                {
                    // don't park on a bridge or in a one-tile lane: everyone behind gets stuck
                    if (Openness(MoveClass.Vehicle, x, y) <= 2) sc -= 700;
                    // don't stand shoulder to shoulder with units that still have to move
                    int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
                    for (int k = 0; k < 4; k++)
                    {
                        var o = s.UnitAt(x + ox[k], y + oy[k]);
                        if (o != null && o.Army == me && !o.Acted && o.Id != u.Id) sc -= 120;
                    }
                }
                if (hqDanger && dest == OwnHq) sc += 4000;
                if (p.Noise > 0) sc += ai.rng.Range(0, p.Noise);
                return sc;
            }

            /// <summary>
            /// Net value lost if enemies attack this tile next phase. Combat is simultaneous, so an attacker
            /// also eats our counter-fire: the threat is (our loss) - (their loss), strongest attacker in full,
            /// the second at half (each enemy attacks only once and has other targets).
            /// </summary>
            float Threat(UnitState u, int x, int y, int hp)
            {
                int idx = s.Index(x, y);
                var d = s.Def(u);
                float n1 = 0, n2 = 0;
                foreach (var er in EnemyReach())
                {
                    if (!er.tiles.Contains(idx)) continue;
                    var ed = s.Def(er.e);
                    if (s.Data.BaseDamage(ed, d) < 0) continue;
                    int dmg = Math.Min(hp, Combat.BaseRoll(s, er.e, er.e.Hp, u, x, y, false) + 4);
                    float net = dmg / 100f * d.Price;
                    if (!ed.IsIndirect && d.RangeMin == 1 && u.Ammo > 0 && s.Data.BaseDamage(d, ed) >= 0)
                        net -= Math.Min(er.e.Hp, Combat.BaseRoll(s, u, hp, er.e, er.e.X, er.e.Y, false) + 4) / 100f * ed.Price;
                    if (net > n1) { n2 = n1; n1 = net; } else if (net > n2) n2 = net;
                }
                return Math.Max(0f, n1 + n2 * 0.5f);
            }

            List<(UnitState e, HashSet<int> tiles)> EnemyReach()
            {
                if (enemyReach != null) return enemyReach;
                enemyReach = new List<(UnitState, HashSet<int>)>();
                foreach (var e in s.Units)
                {
                    if (e.Army != foe || e.IsCarried || e.Ammo <= 0) continue;
                    var d = s.Def(e);
                    if (d.RangeMax <= 0) continue;
                    var set = new HashSet<int>();
                    if (d.IsIndirect)
                    {
                        for (int yy = e.Y - d.RangeMax; yy <= e.Y + d.RangeMax; yy++)
                            for (int xx = e.X - d.RangeMax; xx <= e.X + d.RangeMax; xx++)
                            {
                                if (!s.InBounds(xx, yy)) continue;
                                int dist = Movement.Distance(xx, yy, e.X, e.Y);
                                if (dist >= d.RangeMin && dist <= d.RangeMax) set.Add(s.Index(xx, yy));
                            }
                    }
                    else
                    {
                        var r = Movement.Reachable(s, e);
                        foreach (var i in r.Cost.Keys)
                        {
                            int x = i % w, y = i / w;
                            if (Movement.StopAt(s, e, x, y) != StopKind.Empty) continue;
                            if (x + 1 < s.Width) set.Add(i + 1);
                            if (x > 0) set.Add(i - 1);
                            if (y + 1 < s.Height) set.Add(i + w);
                            if (y > 0) set.Add(i - w);
                        }
                    }
                    enemyReach.Add((e, set));
                }
                return enemyReach;
            }

            float AttackScore(UnitState u, UnitState t, int dmg, int cdmg)
            {
                float sc = p.WDamage * dmg / 100f * Value(t);
                if (dmg >= t.Hp) sc += 0.35f * Value(t);
                int ti = s.Index(t.X, t.Y);
                if (s.CapturingUnit[ti] == t.Id)
                {
                    float pv = PropValue(ti);
                    int newCount = Math.Max(0, (t.Hp - dmg + 9) / 10);
                    sc += pv * 0.8f * (t.Count - newCount) / Math.Max(1, t.Count);
                    if (newCount == 0) sc += pv * s.CaptureProgress[ti] / (float)s.Rules.CaptureGoal;
                }
                if (hqDanger && s.Def(t).Capture >= 2 && OwnHq >= 0 && Movement.Distance(t.X, t.Y, OwnHq % w, OwnHq / w) <= s.Def(t).Move + 1)
                    sc += 3000f * dmg / Math.Max(1, t.Hp);
                sc -= p.WLoss * cdmg / 100f * Value(u);
                if (cdmg >= u.Hp) sc -= 0.35f * Value(u);
                if (p.FocusFire && dmg < t.Hp)
                {
                    // the others can finish what this hit starts: a dead unit fires back at no one next phase
                    float others = AllyPotential(t) - OwnShare(u, t);
                    if (dmg + others >= t.Hp) sc += p.FocusBonus * Value(t) * Math.Min(1f, dmg / (float)t.Hp * 2f);
                }
                return sc;
            }

            Dictionary<int, float> allyPot;
            Dictionary<long, float> ownShare;

            /// <summary>Damage our not-yet-acted units could still deal to this enemy this phase (rough, ignores order).</summary>
            float AllyPotential(UnitState t)
            {
                if (allyPot == null)
                {
                    allyPot = new Dictionary<int, float>(); ownShare = new Dictionary<long, float>();
                    foreach (var u in s.Units)
                    {
                        if (u.Army != me || u.Acted || u.IsCarried || u.Ammo <= 0) continue;
                        var d = s.Def(u);
                        if (d.RangeMax <= 0) continue;
                        MoveResult reach = d.IsIndirect ? null : Movement.Reachable(s, u);
                        foreach (var e in s.Units)
                        {
                            if (e.Army != foe || e.IsCarried || s.Data.BaseDamage(d, s.Def(e)) < 0) continue;
                            bool can = false;
                            if (d.IsIndirect) can = Combat.CanAttackFrom(s, u, e, u.X, u.Y, false);
                            else
                                foreach (var i in reach.Cost.Keys)
                                {
                                    int x = i % w, y = i / w;
                                    if (Movement.Distance(x, y, e.X, e.Y) == 1 && Movement.StopAt(s, u, x, y) == StopKind.Empty) { can = true; break; }
                                }
                            if (!can) continue;
                            float dm = Combat.BaseRoll(s, u, u.Hp, e, e.X, e.Y, false) + 4;
                            allyPot.TryGetValue(e.Id, out var v); allyPot[e.Id] = v + dm;
                            ownShare[((long)u.Id << 32) | (uint)e.Id] = dm;
                        }
                    }
                }
                return allyPot.TryGetValue(t.Id, out var p0) ? p0 : 0;
            }

            float OwnShare(UnitState u, UnitState t) => ownShare != null && ownShare.TryGetValue(((long)u.Id << 32) | (uint)t.Id, out var v) ? v : 0;

            float PropValue(int i)
            {
                var t = s.Data.Terrains[s.Terrain[i]];
                if (t.Id == "HQ") return s.Owner[i] == foe ? 30000 : 0;
                float v = 1500 + t.Income * 4;
                if (t.Produces != null) v += 800;
                if (s.Owner[i] == foe) v *= 1.5f;
                return v;
            }

            /// <summary>
            /// Joining a squad that has already captured this phase. The gauge is kept, so the join is worth
            /// what it saves: the progress itself when the enemy could otherwise wipe the capturer out, and
            /// the turns saved because more men survive to capture next turn. Nothing when the capturer is
            /// safe and finishes as fast alone.
            /// </summary>
            float JoinCaptureScore(UnitState u, UnitDef d, UnitState o, int i)
            {
                int goal = s.Rules.CaptureGoal, prog = s.CaptureProgress[i];
                int need = Math.Max(1, goal - prog);
                int after = Math.Min(100, u.Hp + o.Hp);
                float hit = IncomingHp(o, i);
                int cB = Math.Max(0, (int)(o.Hp - hit) + 9) / 10, cA = Math.Max(0, (int)(after - hit) + 9) / 10;
                if (o.Hp - hit <= 0) cB = 0;
                if (after - hit <= 0) cA = 0;
                Func<int, int> turns = c => c <= 0 ? 6 : Math.Min(6, (need + c - 1) / c);
                float pv = PropValue(i);
                float sc = 100 + pv * 0.15f * (turns(cB) - turns(cA));
                if (cB == 0 && cA > 0) sc += pv * prog / (float)goal + pv * 0.3f;   // the progress would be lost
                if (turns(cB) == turns(cA)) sc -= 400;
                sc -= Math.Max(0, u.Hp + o.Hp - 100) / 100f * d.Price;          // men beyond a full squad are lost
                return sc;
            }

            /// <summary>HP the enemy could take off a unit on this tile next phase (strongest hit + half the second).</summary>
            float IncomingHp(UnitState u, int idx)
            {
                var d = s.Def(u);
                int x = idx % w, y = idx / w;
                float h1 = 0, h2 = 0;
                foreach (var er in EnemyReach())
                {
                    if (!er.tiles.Contains(idx) || s.Data.BaseDamage(s.Def(er.e), d) < 0) continue;
                    float dmg = Combat.BaseRoll(s, er.e, er.e.Hp, u, x, y, false) + 4;
                    if (dmg > h1) { h2 = h1; h1 = dmg; } else if (dmg > h2) h2 = dmg;
                }
                return h1 + h2 * 0.5f;
            }

            /// <summary>
            /// Reachable tiles in an order that is the same for both armies under a 180-degree turn of the
            /// map, so equal scores are broken the same way for each side (index order would favour moving
            /// up and left, which is forward for one army and backward for the other).
            /// </summary>
            List<KeyValuePair<int, int>> Ordered(Dictionary<int, int> cost)
            {
                var l = new List<KeyValuePair<int, int>>(cost);
                if (me == Army.Blue) l.Sort((a, b) => b.Key.CompareTo(a.Key));
                else l.Sort((a, b) => a.Key.CompareTo(b.Key));
                return l;
            }

            float CaptureScore(UnitState u, int i)
            {
                int prog = s.CapturingUnit[i] == u.Id ? s.CaptureProgress[i] : 0;
                int np = prog + u.Count, goal = s.Rules.CaptureGoal;
                float pv = PropValue(i);
                float sc = p.WCapture * pv * Math.Min(1f, np / (float)goal) + 200;
                if (np >= goal)
                {
                    sc += pv * 0.6f;
                    if (s.Data.Terrains[s.Terrain[i]].Id == "HQ" && s.Owner[i] == foe) sc += 1e7f;
                }
                return sc;
            }

            float ScoreLoad(UnitState u, int start)
            {
                var dm = DistFor(u);
                int a = dm[start];
                return a < Inf && a > 2 * s.Def(u).Move + 1 ? 700 : -500;
            }

            float UnloadScore(UnitState cargo, int at, int drop)
            {
                var dm = DistFor(cargo);
                int b = dm[drop];
                float sc = b < Inf && b <= 2 * s.Def(cargo).Move ? 1200 : -800;
                var t = s.Data.Terrains[s.Terrain[drop]];
                if (t.IsProperty && s.Owner[drop] != me) sc += 1500;
                if (p.Has(AiFeature.Threat)) sc -= p.WThreat * Threat(cargo, drop % w, drop / w, cargo.Hp);
                return sc;
            }

            float SupplyScore(UnitState truck, int x, int y)
            {
                float sc = 0; int cost = 0;
                int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
                for (int k = 0; k < 4; k++)
                {
                    var o = s.UnitAt(x + dx[k], y + dy[k]);
                    if (o == null || o.Army != me || o.Acted || o.Id == truck.Id) continue;
                    var d = s.Def(o);
                    if (d.Domain != Domain.Ground) continue;
                    float need = 0.3f * (1 - o.Fuel / (float)Math.Max(1, d.Fuel)) + (d.Ammo > 0 ? 0.7f * (1 - o.Ammo / (float)d.Ammo) : 0);
                    sc += need * d.Price * 0.3f;
                    cost += (d.Fuel - o.Fuel) * o.Count + (d.Ammo - o.Ammo) * d.AmmoPrice * o.Count;
                }
                if (cost > s.Funds[(int)me]) return 0;
                return sc < 100 ? 0 : sc;
            }

            // ---------- distance maps ----------

            int[] DistFor(UnitState u)
            {
                var d = s.Def(u);
                if (d.CargoCapacity > 0)
                {
                    if (u.Cargo.Count > 0) return Dist(d.MoveClass, "cap2");
                    var pick = Dist(d.MoveClass, "pickup");
                    if (!emptyDist.Contains(d.MoveClass + "pickup")) return pick;
                    if (p.TransportTactics && d.Domain == Domain.Ground)
                    {
                        var scr = Dist(d.MoveClass, "screen");
                        if (!emptyDist.Contains(d.MoveClass + "screen")) return scr;
                    }
                    return Dist(d.MoveClass, "own");
                }
                if (d.Capture > 0) return Dist(MoveClass.Foot, d.Capture >= 2 ? "cap2" : "cap1");
                if (p.Has(AiFeature.Fuel) && d.FuelPerPhase > 0 && u.Fuel <= d.Fuel * 0.4f) return Dist(d.MoveClass, "airport");
                if (d.CanSupply)
                {
                    var n = Dist(d.MoveClass, "needy");
                    return emptyDist.Contains(d.MoveClass + "needy") ? Dist(d.MoveClass, "own") : n;
                }
                return Dist(d.MoveClass, "enemy:" + d.Index);
            }

            List<int> Sources(string key)
            {
                var src = new List<int>();
                int n = s.Width * s.Height;
                if (key == "cap1" || key == "cap2")
                {
                    for (int i = 0; i < n; i++)
                    {
                        var t = s.Data.Terrains[s.Terrain[i]];
                        if (t.IsProperty && s.Owner[i] != me && (key == "cap2" || t.Id != "HQ")) src.Add(i);
                    }
                }
                else if (key == "airport")
                {
                    for (int i = 0; i < n; i++)
                        if (s.Owner[i] == me && s.Data.Terrains[s.Terrain[i]].Supplies == Domain.Air) src.Add(i);
                }
                else if (key == "foehq")
                {
                    for (int i = 0; i < n; i++)
                        if (s.Owner[i] == foe && s.Data.Terrains[s.Terrain[i]].Id == "HQ") src.Add(i);
                }
                else if (key == "pickup")
                {
                    var cap = Dist(MoveClass.Foot, "cap2");
                    foreach (var u in s.Units)
                        if (u.Army == me && !u.IsCarried && s.Def(u).MoveClass == MoveClass.Foot)
                        {
                            int i = s.Index(u.X, u.Y);
                            if (cap[i] > 6 && cap[i] < Inf) src.Add(i);
                        }
                }
                else if (key == "needy")
                {
                    foreach (var u in s.Units)
                    {
                        var d = s.Def(u);
                        if (u.Army != me || u.IsCarried || d.Domain != Domain.Ground || d.CanSupply) continue;
                        if (u.Fuel < d.Fuel * 0.4f || (d.Ammo > 0 && u.Ammo < d.Ammo * 0.4f)) src.Add(s.Index(u.X, u.Y));
                    }
                }
                else if (key == "screen")
                {
                    // tiles next to our units that are capturing: where a truck makes a wall
                    foreach (var u in s.Units)
                    {
                        if (u.Army != me || u.IsCarried || s.CapturingUnit[s.Index(u.X, u.Y)] != u.Id) continue;
                        for (int k = 0; k < 4; k++)
                        {
                            int x = u.X + (k == 0 ? 1 : k == 1 ? -1 : 0), y = u.Y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (s.InBounds(x, y) && s.UnitAt(x, y) == null) src.Add(s.Index(x, y));
                        }
                    }
                }
                else if (key == "own")
                {
                    foreach (var u in s.Units) if (u.Army == me && !u.IsCarried) src.Add(s.Index(u.X, u.Y));
                }
                else if (key.StartsWith("enemy:"))
                {
                    var ad = s.Data.Units[int.Parse(key.Substring(6))];
                    foreach (var e in s.Units)
                        if (e.Army == foe && !e.IsCarried && s.Data.BaseDamage(ad, s.Def(e)) >= 0) src.Add(s.Index(e.X, e.Y));
                    if (ad.Domain == Domain.Ground)
                        for (int i = 0; i < n; i++)
                            if (s.Owner[i] == foe && s.Data.Terrains[s.Terrain[i]].Id == "HQ") src.Add(i);
                }
                return src;
            }

            /// <summary>Move cost from every tile to the nearest source (reverse Dijkstra over terrain only).</summary>
            public int[] Dist(MoveClass mc, string key)
            {
                string ck = mc + key;
                if (dist.TryGetValue(ck, out var cached)) return cached;
                int n = s.Width * s.Height;
                var dm = new int[n];
                for (int i = 0; i < n; i++) dm[i] = Inf;
                var src = Sources(key);
                if (src.Count == 0) emptyDist.Add(ck);
                var isSrc = new bool[n];
                foreach (var i in src) { dm[i] = 0; isSrc[i] = true; }
                var done = new bool[n];
                for (int iter = 0; iter < n; iter++)
                {
                    int cur = -1, best = Inf;
                    for (int i = 0; i < n; i++) if (!done[i] && dm[i] < best) { best = dm[i]; cur = i; }
                    if (cur < 0) break;
                    done[cur] = true;
                    int enter = s.Data.Terrains[s.Terrain[cur]].Cost[(int)mc];
                    if (isSrc[cur]) enter = 1;
                    else if (enter < 0) continue;
                    int cx = cur % w, cy = cur / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (!s.InBounds(nx, ny)) continue;
                        int ni = ny * w + nx;
                        if (s.Data.Terrains[s.Terrain[ni]].Cost[(int)mc] < 0) continue;
                        int nd = dm[cur] + enter;
                        if (nd < dm[ni]) dm[ni] = nd;
                    }
                }
                dist[ck] = dm;
                return dm;
            }
        }
    }
}
