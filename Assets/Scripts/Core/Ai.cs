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

    /// <summary>
    /// The COM's habit of thought (戦法). Each one leans the evaluation and the production one way, so
    /// two COMs with different habits do not mirror each other and one of them gets the upper hand
    /// (which one depends on the matchup and on the map; measured with Tools/Sim "styles"):
    /// 速攻 (Blitz): a vehicle army (tanks, few foot soldiers and guns) that trades freely and keeps
    ///   moving forward.
    /// 物量 (Swarm): foot soldiers from the first day; pushes forward early, then holds a line in the
    ///   middle of the map: mech in front, light guns placed to cover the tiles in front of it.
    /// 精鋭 (Elite): heavier units, refilled early and pulled back for repairs early so that they last.
    /// Which one wins depends on the map as much as on the pairing (Tools/Sim "styles2").
    /// </summary>
    public enum AiStyle { Auto = -1, Standard = 0, Blitz = 1, Swarm = 2, Elite = 3 }

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
        public bool KeepBarricade = true;      // a unit keeping an enemy off a capturing squad does not leave for less than that is worth
        public bool StrategicBuy = true;       // buy (or save up for) an aircraft or warship when the situation calls for one
        public bool JoinAfterAct = true;       // only join units that have already acted this phase
        public bool JoinCapture = true;        // join a capturing squad: the capture gauge is kept, so the capture finishes sooner
        public bool TransportTactics = true;   // early: a truck carries infantry to far properties; afterwards it stands in front of them as a wall       // a battered front unit steps back so a fresh one can take its tile      // infantry share / quality rules apply only once the army reaches this fraction of the unit limit (0 = always)

        // ---- 戦法 (style) ----
        public AiStyle Style = AiStyle.Auto;   // Auto: picked from the COM's seed (Lv2 and up)
        public float ProdDirect = 1f, ProdIndirect = 1f, ProdFoot = 1f;   // production weight per kind of unit
        public float PricePref = 0f;           // production weight x (price / 6000)^PricePref for combat vehicles: <0 cheap, >0 heavy
        public float EarlyPush = 0f;           // "go for the base" from day 8 on, whatever the balance (0..1)
        public bool IndirectFirst = true;      // guns fire before the direct-fire units move in
        public bool Gunships = false;          // 精鋭: against a foot army with little anti-air, armed helicopters as fighting units
        public bool SlotMerge = false;         // 精鋭: near the unit limit, merge battered units to free slots for heavy ones
        public bool Focus = false;             // middle game: the fighting units mass on one enemy property at a time
        public bool Phases = true;             // the end game: from a day set by the map size, everything goes for the enemy base
        public int LateBase = 12, LateSpan = 10; // end game from day LateBase + (distance between the bases) / 2, in full LateSpan days later
        public bool KillZone = false;          // 物量: guns stand where they cover the tiles in front of the line
        public int RepairAt = 60;              // battered units (this HP or less) go to a city to be repaired
        public bool CarefulSupply = false;     // refill early (ammo at half) and pull back valuable units for repairs           // 精鋭: after the capture race, defend and save, then buy heavy units all at once and strike
        public int HoldFromDay = 0;            // 物量: push forward until this day, then hold the line (0 = off)
        public float MechPref = 1f;            // weight of mech (anti-tank foot soldiers) once the capture race is over
        public int AirPerAirport = 2;          // aircraft kept per airport we own (they have to come back to refuel)
        public bool BreakDeadlock = true;      // at the unit limit with money piling up, trade more readily

        public static readonly string[] StyleNames = { "標準", "速攻", "物量", "精鋭" };

        public bool Has(AiFeature f) => (Features & f) != 0;

        public AiProfile Clone() => (AiProfile)MemberwiseClone();

        /// <summary>Applies a 戦法 on top of the level's weights.</summary>
        public bool StyleApplied { get; private set; }

        public void ApplyStyle(AiStyle st)
        {
            if (StyleApplied) return;
            StyleApplied = true;
            Style = st;
            switch (st)
            {
                case AiStyle.Blitz:
                    // vehicles: tanks and trucks, hardly any foot soldiers or guns; fast, trades freely and
                    // keeps moving forward
                    WLoss *= 0.7f; WThreat *= 0.5f; WAdvance *= 1.5f; FocusFire = true;
                    ProdDirect = 1.8f; ProdIndirect = 0.4f; ProdFoot = 0.9f; PricePref = 0.3f;
                    break;
                case AiStyle.Swarm:
                    // foot soldiers from the first day: pushes forward early, then digs in on a line of
                    // mech (anti-tank) and light guns before the enemy's tanks arrive
                    WCapture *= 1.2f;
                    ProdFoot = 1.6f; ProdIndirect = 1.2f; ProdDirect = 0.6f; PricePref = -0.4f;
                    HoldFromDay = 9; MechPref = 2.5f; KillZone = true;
                    break;
                case AiStyle.Elite:
                    // heavier units, kept alive: refilled at half ammo and pulled back for repairs early, so
                    // that they are still there late, when every unit slot counts
                    FocusFire = true;
                    ProdDirect = 1.4f; ProdIndirect = 1.2f; PricePref = 0.8f;
                    RepairAt = 70; CarefulSupply = true;
                    Gunships = true; SlotMerge = true;
                    break;
            }
        }
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
        readonly HashSet<int> refill = new HashSet<int>();     // stay put this phase: 全補 at the end of it
        readonly HashSet<int> productionTried = new HashSet<int>();
        const int Inf = 1 << 20;

        public AiPlayer(Army army, AiProfile profile, uint seed)
        {
            Army = army; rng = new Rng(seed);
            Profile = profile.Clone();
            if (Profile.Style == AiStyle.Auto)
                Profile.ApplyStyle(Profile.Level >= 2 ? StyleForSeed(seed) : AiStyle.Standard);
            else Profile.ApplyStyle(Profile.Style);       // no-op when the caller already applied (and tuned) it
        }

        /// <summary>The 戦法 a COM (Lv2 and up) with this seed plays.</summary>
        public static AiStyle StyleForSeed(uint seed) => (AiStyle)(1 + (int)(PhaseSeed(seed, 7777) % 3u));

        /// <summary>A seed near `seed` whose random 戦法 is not `other`: two COMs facing each other play
        /// different 戦法 so that one of them has the upper hand.</summary>
        public static uint SeedForStyleOtherThan(uint seed, AiStyle other)
        {
            for (uint k = 0; k < 64; k++)
                if (StyleForSeed(seed + k * 7919u) != other) return seed + k * 7919u;
            return seed;
        }

        public static uint SeedWithOtherStyle(uint seed, uint other) => SeedForStyleOtherThan(seed, StyleForSeed(other));

        /// <summary>The 戦法 a BOT / COM plays: the chosen one (1-3), or for 0 (random) the one its seed picks.</summary>
        public static AiStyle BotStyle(int chosen, uint seed) => chosen > 0 ? (AiStyle)chosen : StyleForSeed(seed);

        /// <summary>An online BOT: level, the 戦法 setting (0 random, 1-3) and the seed shared by every client.</summary>
        public static AiPlayer ForBot(Army army, int level, int style, uint seed)
        {
            var p = AiProfile.ForLevel(level);
            if (style > 0) p.Style = (AiStyle)style;
            return ForOnline(army, p, seed);
        }

        /// <summary>The 戦法 this COM plays (for display).</summary>
        public AiStyle Style => Profile.Style;

        // Online BOTs: every phase starts from a state that depends only on (seed, day, army), so any
        // client can compute the BOT's moves (the room host plays them, the others check them), and a
        // client that joins mid-game only has to replay the current phase.
        bool phaseSeeded;
        uint phaseSeedBase;

        public static AiPlayer ForOnline(Army army, AiProfile profile, uint seed) =>
            new AiPlayer(army, profile, seed) { phaseSeeded = true, phaseSeedBase = seed };

        static uint PhaseSeed(uint seed, int key)
        {
            unchecked
            {
                uint x = seed ^ ((uint)key * 0x9E3779B9u);
                x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
                return x | 1u;
            }
        }

        Army Foe => Army == Army.Red ? Army.Blue : Army.Red;

        public Command Next(GameState s)
        {
            if (s.GameOver || s.Active != Army) return null;
            int key = s.Day * 2 + (int)s.Active;
            if (key != phaseKey)
            {
                phaseKey = key; commandsThisPhase = 0; resupplyConsidered = false; savingThisPhase = false; lateCapThisPhase = false; strategicPlanned = false; strategic = null;
                forceWait.Clear(); done.Clear(); productionTried.Clear(); refill.Clear();
                if (phaseSeeded) { rng = new Rng(PhaseSeed(phaseSeedBase, key)); reliefUnit = reliefTile = -1; }
            }
            if (++commandsThisPhase > 300) return new EndPhaseCommand { Army = Army };
            if (simData == null)
            {
                var r = s.Data.Rules.Clone();
                r.RandMin = r.RandMax = (s.Data.Rules.RandMin + s.Data.Rules.RandMax) / 2;
                simData = s.Data.WithRules(r);
            }

            if (commandsThisPhase == 1 && Profile.Has(AiFeature.Resupply)) PickRefill(s);

            foreach (var u in s.Units)
                if (u.Army == Army && !u.Acted && !u.IsCarried && forceWait.Contains(u.Id) && done.Add(u.Id))
                    return new UnitCommand { Army = Army, UnitId = u.Id, ToX = u.X, ToY = u.Y, Action = UnitAction.Wait };

            var cmd = ChooseUnitCommand(s);
            if (cmd != null) return cmd;
            // everyone else has acted, so 全補 now refills only the units that stayed for it
            if (!resupplyConsidered && refill.Count > 0)
            {
                resupplyConsidered = true;
                if (RulesEngine.CheckResupply(s, out _, out _) == null) return new ResupplyAllCommand { Army = Army };
            }
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
                if (u.Army == Army && !u.Acted && !u.IsCarried && !forceWait.Contains(u.Id) && !done.Contains(u.Id) && !refill.Contains(u.Id)) units.Add(u);
            if (units.Count == 0) return null;
            var ctx = new Ctx(s, this);
            // (end game: no stepping back to rest; everything stays in the fight)
            if (Profile.Has(AiFeature.Formation) && Profile.RotateWeak && pushNow0(s) < 0.5f)
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
                if (acts.Count > 0)
                {
                    all = acts;
                    // guns fire first (they take no counter-fire), the direct-fire units then finish off what is left
                    if (Profile.IndirectFirst)
                    {
                        var guns = acts.FindAll(c => c.Cmd.Action == UnitAction.Attack && s.Def(s.UnitById(c.Cmd.UnitId)).IsIndirect);
                        if (guns.Count > 0) all = guns;
                    }
                }
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
                        if (u2.Army == Army && !u2.Acted && !u2.IsCarried && !forceWait.Contains(u2.Id) && !done.Contains(u2.Id) && !refill.Contains(u2.Id))
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

        /// <summary>
        /// How badly a unit needs fuel or ammo: 0 fine, 1 should refill soon, 2 stranded (cannot move or
        /// cannot fight). Ground vehicles keep two moves' worth of fuel in hand; aircraft turn back at 40%.
        /// </summary>
        internal static int Need(GameState s, UnitState u)
        {
            var d = s.Def(u);
            if (d.CanSupply) return u.Fuel <= 0 ? 2 : u.Fuel <= d.Move * 2 ? 1 : 0;
            int n = 0;
            if (d.Ammo > 0) { if (u.Ammo <= 0) n = 2; else if (u.Ammo * 4 <= d.Ammo) n = 1; }
            if (d.Domain == Domain.Ground)
            {
                if (u.Fuel <= 0) n = 2;
                else if (u.Fuel <= Math.Max(d.Move * 2, d.Fuel * 3 / 10)) n = Math.Max(n, 1);
            }
            else if (d.Domain == Domain.Sea) { if (u.Fuel <= d.Fuel * 3 / 10) n = Math.Max(n, 1); }
            else if (u.Fuel <= d.Fuel * 4 / 10) n = Math.Max(n, 1);
            return n;
        }

        /// <summary>
        /// 全補 refills every unit that has not acted yet, and uses up their action. So the units that need
        /// it (low on fuel or ammo, or battered, standing on one of our facilities) are held back, the rest
        /// of the army acts first, and 全補 comes last, when it touches only those. Units next to a supply
        /// truck are left to the truck, which refills them without using them up.
        /// </summary>
        void PickRefill(GameState s)
        {
            refill.Clear();
            resupplyConsidered = false;
            if (s.ResupplyUsed[(int)Army]) return;
            int cost = 0;
            foreach (var u in s.Units)
            {
                if (u.Army != Army || u.Acted || u.IsCarried) continue;
                var d = s.Def(u);
                var t = s.TerrainAt(u.X, u.Y);
                if (!(t.IsProperty && s.Owner[s.Index(u.X, u.Y)] == Army && t.Supplies == d.Domain)) continue;
                int need = Need(s, u);
                if (d.FuelPerPhase > 0 && u.Fuel < d.Fuel / 2) need = Math.Max(need, 1);
                if (need == 0 && u.Hp > Profile.RepairAt) continue;
                if (d.Capture > 0 && s.CapturingUnit[s.Index(u.X, u.Y)] == u.Id) continue;   // finishing a capture comes first
                int c = (d.Fuel - u.Fuel) * u.Count + (d.Ammo - u.Ammo) * d.AmmoPrice * u.Count
                        + ((Math.Min(100, u.Hp + s.Rules.ResupplyHp) + 9) / 10 - u.Count) * d.Price / 10;
                if (cost + c > s.Funds[(int)Army]) continue;
                cost += c;
                refill.Add(u.Id);
            }
        }

        // ================= production =================

        Command ChooseProduction(GameState s)
        {
            if (Profile.Has(AiFeature.Economy) && Profile.StrategicBuy && !strategicPlanned)
            {
                strategicPlanned = true;
                strategic = PlanStrategic(s);
            }
            if (strategic != null)
            {
                var st = strategic.Value;
                int skey = st.y * 4096 + st.x;
                if (KeepForRefill(s, st.x, st.y) || (st.def.Domain == Domain.Air && AirFull(s))) strategic = null;
                else if (!productionTried.Contains(skey) && s.Funds[(int)Army] >= st.def.Price && RulesEngine.UnitCount(s, Army) < s.Rules.UnitLimit)
                {
                    productionTried.Add(skey);
                    strategic = null;
                    return new ProduceCommand { Army = Army, X = st.x, Y = st.y, UnitType = st.def.Id };
                }
            }
            // blue scans from the far corner so that both armies try their facilities in the same order
            // relative to the front on a point-symmetric map
            bool rev = Army == Army.Blue;
            // gunships: the airports choose before the factories spend the money
            for (int pass = GunshipMode(s) ? 0 : 1; pass < 2; pass++)
            for (int yi = 0; yi < s.Height; yi++)
                for (int xi = 0; xi < s.Width; xi++)
                {
                    int y = rev ? s.Height - 1 - yi : yi, x = rev ? s.Width - 1 - xi : xi;
                    int key = y * 4096 + x;
                    if (productionTried.Contains(key)) continue;
                    if (pass == 0 && s.TerrainAt(x, y).Produces != Domain.Air) continue;
                    var list = RulesEngine.ProducibleAt(s, Army, x, y);
                    if (list.Count == 0) continue;
                    productionTried.Add(key);
                    if (AirFull(s)) { list = list.FindAll(d => d.Domain != Domain.Air); if (list.Count == 0) continue; }
                    if (KeepForRefill(s, x, y)) continue;
                    if (RulesEngine.UnitCount(s, Army) >= s.Rules.UnitLimit) return null;
                    var pick = PickUnit(s, list, x, y);
                    if (pick != null) return new ProduceCommand { Army = Army, X = x, Y = y, UnitType = pick.Id };
                }
            return null;
        }

        /// <summary>An airport or port is left empty while one of our aircraft or ships that is low on
        /// fuel is coming home to it (a unit built there would block it for another day).</summary>
        bool KeepForRefill(GameState s, int x, int y)
        {
            if (!Profile.Has(AiFeature.Fuel)) return false;
            var t = s.TerrainAt(x, y);
            if (t.Supplies == null || t.Supplies.Value == Domain.Ground) return false;
            foreach (var u in s.Units)
            {
                if (u.Army != Army || u.IsCarried) continue;
                var d = s.Def(u);
                if (d.Domain != t.Supplies.Value || d.CanSupply) continue;
                if (Need(s, u) == 0 && u.Hp > Profile.RepairAt) continue;
                if (u.X == x && u.Y == y) continue;
                if (Movement.Distance(u.X, u.Y, x, y) <= d.Move + 1) return true;
            }
            return false;
        }

        /// <summary>Aircraft have to come back to an airport every few days; more than two per airport and
        /// they queue up in the air until they crash.</summary>
        bool AirFull(GameState s)
        {
            if (!Profile.Has(AiFeature.Fuel)) return false;
            int air = 0, airports = 0;
            foreach (var u in s.Units) if (u.Army == Army && s.Def(u).Domain == Domain.Air && s.Def(u).CargoCapacity == 0) air++;
            for (int i = 0; i < s.Owner.Length; i++)
                if (s.Owner[i] == Army && s.Data.Terrains[s.Terrain[i]].Supplies == Domain.Air) airports++;
            return air >= Profile.AirPerAirport * airports;
        }

        int FreeFacilities(GameState s)
        {
            int n = 0;
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                    if (!productionTried.Contains(y * 4096 + x) && RulesEngine.ProducibleAt(s, Army, x, y).Count > 0) n++;
            return n;
        }

        bool savingThisPhase, lateCapThisPhase, strategicPlanned;
        (UnitDef def, int x, int y)? strategic;
        int reliefUnit = -1, reliefTile = -1;

        UnitDef PickUnit(GameState s, List<UnitDef> list, int fx, int fy)
        {
            int funds = s.Funds[(int)Army];
            if (savingThisPhase) return null;
            // an aircraft or warship planned this phase is bought first: keep its price aside
            if (strategic != null) funds -= strategic.Value.def.Price;
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
                if (t.CanSupply) { if (!NeedSupply(s)) wgt *= 0.2f; else wgt = Math.Max(wgt, 1.5f); }
                wgt *= StyleFactor(s, t);
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


        /// <summary>How much this COM's 戦法 likes a unit type (production weight factor).</summary>
        float StyleFactor(GameState s, UnitDef t)
        {
            if (t.RangeMax <= 0) return 1f;                                  // trucks: by need only
            if (t.Domain != Domain.Ground) return 1f;
            // near the unit limit: each slot is worth more, so heavier units
            if (Profile.SlotMerge && RulesEngine.UnitCount(s, Army) >= 40 && t.RangeMax > 0 && t.Capture == 0)
                return (t.IsIndirect ? Profile.ProdIndirect : Profile.ProdDirect) * (float)Math.Pow(t.Price / 6000.0, Profile.PricePref + 1.5f);
            if (t.Capture > 0) return Profile.ProdFoot * (t.Capture == 1 && s.Day >= 6 ? Profile.MechPref : 1f);
            float price = (float)Math.Pow(t.Price / 6000.0, Profile.PricePref);
            if (t.IsIndirect) return Profile.ProdIndirect * price;
            return t.CargoCapacity > 0 ? 1f : Profile.ProdDirect * price;
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
            // finishing off: only infantry can take the base, so always have a few
            if (!needInf && pushNow0(s) >= 0.5f && capturers < 3) needInf = true;
            // ...and keep the last unit slots for them: a full army without infantry cannot take the base
            if (pushNow0(s) >= 0.5f && capturers < 3 && myCount >= s.Rules.UnitLimit - 3)
            {
                aff = aff.FindAll(d => d.Capture >= 2);
                if (aff.Count == 0) return null;
                needInf = true;
            }

            // ---- how scarce is a unit slot? (unit limit headroom, and how jammed the front is) ----
            float pSlots = Clamp01((myCount - s.Rules.UnitLimit * Profile.EcoSlotFrom) / (s.Rules.UnitLimit * 0.5f));
            float pFront = FrontCongestion(s);
            float pushNow = pushNow0(s);
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
                // a landing ship is bought for a crossing only it can make, never as a fighting ship
                if (t.Domain == Domain.Sea && t.CargoCapacity > 0 && !SeaLiftUseful(s, t)) return 0f;
                float bw = BaseWeight(t) * StyleFactor(s, t);
                if (t.Domain == Domain.Air && t.CargoCapacity > 0 && t.RangeMax > 0 && GunshipMode(s)) bw = Math.Max(bw, 3f);   // gunships
                // finishing off needs units that move and shoot: guns only hold ground
                if (pushNow > 0 && t.RangeMax > 0 && t.Domain == Domain.Ground && t.Capture == 0)
                    bw *= t.IsIndirect ? 1f - 0.7f * pushNow : 1f + pushNow;
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
                if (t.CanSupply) { if (!NeedSupply(s)) wgt *= 0.1f; else wgt = Math.Max(wgt, 1.5f); }
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

        /// <summary>
        /// Aircraft and warships come from their own facilities, which get only what the factories leave
        /// over, so left to the per-facility picks they are almost never bought. Once per phase, decide
        /// whether one is worth buying first, judged by what it is good at on the current battlefield:
        /// an aircraft by its speed (enemies it reaches days before our ground army could), a warship by
        /// long-range fire from the water (enemies it can shell from a tile nothing of theirs can hit).
        /// Only what is affordable now; returns the unit and the facility to build it at, or null.
        /// </summary>
        (UnitDef def, int x, int y)? PlanStrategic(GameState s)
        {
            int funds = s.Funds[(int)Army];
            if (RulesEngine.UnitCount(s, Army) >= s.Rules.UnitLimit - 1) return null;
            if (s.Day < 6) return null;            // the capture race comes first
            int n = s.Width * s.Height;
            // how many days our ground army needs to reach each tile (from its units and factories)
            var groundDays = new float[n];
            for (int i = 0; i < n; i++) groundDays[i] = 99;
            var src = new List<int>();
            foreach (var u in s.Units)
            {
                if (u.Army != Army || u.IsCarried) continue;
                var d = s.Def(u);
                if (d.Domain == Domain.Ground && d.RangeMax > 0 && !d.IsIndirect) src.Add(s.Index(u.X, u.Y));
            }
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    var t = s.TerrainAt(x, y);
                    if (t.Produces == Domain.Ground && s.Owner[s.Index(x, y)] == Army && RulesEngine.InProductionRange(s, Army, x, y)) src.Add(s.Index(x, y));
                }
            foreach (var mc in new[] { MoveClass.Vehicle, MoveClass.Foot })
            {
                float perDay = mc == MoveClass.Vehicle ? 5f : 3f;
                var c = TerrainCost(s, mc, src);
                for (int i = 0; i < n; i++) if (c[i] < Inf) groundDays[i] = Math.Min(groundDays[i], 1 + c[i] / perDay);   // +1: it has to be built first
            }

            var enemies = new List<UnitState>();
            int ownAirSea = 0;
            foreach (var u in s.Units)
            {
                var d = s.Def(u);
                if (u.Army == Army) { if (d.Domain != Domain.Ground && d.RangeMax > 0 && d.CargoCapacity == 0) ownAirSea++; continue; }
                if (!u.IsCarried) enemies.Add(u);
            }
            if (enemies.Count == 0) return null;

            (UnitDef def, int x, int y)? best = null; float bestSc = 0; int bestTargets = 0;
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    var t = s.TerrainAt(x, y);
                    if (t.Produces == null || t.Produces.Value == Domain.Ground) continue;
                    var list = RulesEngine.ProducibleAt(s, Army, x, y);
                    if (list.Count == 0) continue;
                    int[] seaCost = t.Produces.Value == Domain.Sea ? TerrainCost(s, MoveClass.Ship, new List<int> { s.Index(x, y) }) : null;
                    foreach (var d in list)
                    {
                        if (d.RangeMax <= 0 || d.CargoCapacity > 0 || d.Price > funds) continue;
                        float value = 0; int targets = 0;
                        foreach (var e in enemies)
                        {
                            var ed = s.Def(e);
                            int g = s.Data.BaseDamage(d, ed);
                            if (g < 0) continue;
                            float worth = Math.Min(g, e.Hp) / 100f * ed.Price;
                            float wgt = 0;
                            if (d.Domain == Domain.Air)
                            {
                                // speed: days the aircraft saves over the ground army (built now, flying from here)
                                float airDays = 1 + Math.Max(0, Movement.Distance(x, y, e.X, e.Y) - 1) / (float)d.Move;
                                float gain = groundDays[s.Index(e.X, e.Y)] - airDays;
                                wgt = Clamp01((gain - 1) / 2f);
                            }
                            else if (seaCost != null)
                                wgt = ShellingWeight(s, d, e, seaCost);
                            if (wgt <= 0) continue;
                            value += wgt * worth;
                            if (wgt >= 0.5f) targets++;
                        }
                        float sc = value / d.Price;
                        if (sc > bestSc) { bestSc = sc; best = (d, x, y); bestTargets = targets; }
                    }
                }
            if (best == null) return null;
            if (bestSc < 0.8f) return null;
            // a few of them, as many as the good targets warrant
            if (ownAirSea >= Math.Min(3, 1 + bestTargets / 4)) return null;
            return best;
        }

        /// <summary>
        /// 1 when the warship can reach (within two days) a sea tile from which it can shell e and that no
        /// enemy can hit back at; less when every such tile is covered by something of theirs.
        /// </summary>
        float ShellingWeight(GameState s, UnitDef d, UnitState e, int[] seaCost)
        {
            float bestW = 0;
            int r = d.RangeMax;
            for (int y = e.Y - r; y <= e.Y + r; y++)
                for (int x = e.X - r; x <= e.X + r; x++)
                {
                    if (!s.InBounds(x, y)) continue;
                    int dist = Movement.Distance(x, y, e.X, e.Y);
                    if (dist < d.RangeMin || dist > d.RangeMax) continue;
                    int i = s.Index(x, y);
                    if (seaCost[i] >= Inf || seaCost[i] > d.Move * 2) continue;
                    int threats = 0;
                    foreach (var o in s.Units)
                    {
                        if (o.Army == Army || o.IsCarried) continue;
                        var od = s.Def(o);
                        if (od.RangeMax <= 0 || s.Data.BaseDamage(od, d) < 0) continue;
                        int od2 = Movement.Distance(o.X, o.Y, x, y);
                        if (od.IsIndirect ? od2 >= od.RangeMin && od2 <= od.RangeMax : od2 <= od.Move + od.RangeMax && CanCloseIn(s, o, od, x, y)) threats++;
                    }
                    float w = threats == 0 ? 1f : 0.3f / threats;
                    if (w > bestW) bestW = w;
                }
            return bestW;
        }

        /// <summary>Can a direct-fire unit get next to tile (x, y)? Land units only from a neighbouring tile
        /// they can stand on (a ship out at sea is safe from tanks on the far shore).</summary>
        static bool CanCloseIn(GameState s, UnitState o, UnitDef od, int x, int y)
        {
            if (od.Domain != Domain.Ground) return true;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (!s.InBounds(nx, ny) || s.TerrainAt(nx, ny).Cost[(int)od.MoveClass] < 0) continue;
                if (Movement.Distance(o.X, o.Y, nx, ny) <= od.Move) return true;
            }
            return false;
        }

        /// <summary>Movement cost from the given tiles over terrain only (units ignored).</summary>
        static int[] TerrainCost(GameState s, MoveClass mc, List<int> sources)
        {
            int n = s.Width * s.Height;
            var cost = new int[n];
            for (int i = 0; i < n; i++) cost[i] = Inf;
            var q = new SortedSet<(int c, int i)>();
            foreach (var i in sources) if (cost[i] != 0) { cost[i] = 0; q.Add((0, i)); }
            while (q.Count > 0)
            {
                var cur = q.Min; q.Remove(cur);
                int x = cur.i % s.Width, y = cur.i / s.Width;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (!s.InBounds(nx, ny)) continue;
                    int tc = s.TerrainAt(nx, ny).Cost[(int)mc];
                    if (tc <= 0) continue;
                    int ni = s.Index(nx, ny), nc = cur.c + tc;
                    if (nc >= cost[ni]) continue;
                    if (cost[ni] < Inf) q.Remove((cost[ni], ni));
                    cost[ni] = nc; q.Add((nc, ni));
                }
            }
            return cost;
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

        /// <summary>A landing ship has a job: the enemy base is only reachable by sea, or a property we do not
        /// own is out of reach on foot (an island) and we have no landing ship yet.</summary>
        bool SeaLiftUseful(GameState s, UnitDef t)
        {
            if (NeedTransport(s, t)) return true;
            int hq = -1;
            foreach (var u in s.Units) if (u.Army == Army && s.Def(u).CargoCapacity > 0 && s.Def(u).Domain == Domain.Sea) return false;
            for (int i = 0; i < s.Owner.Length; i++) if (s.Owner[i] == Army && s.Data.Terrains[s.Terrain[i]].Id == "HQ") hq = i;
            if (hq < 0) return false;
            var foot = TerrainCost(s, MoveClass.Foot, new List<int> { hq });
            for (int i = 0; i < s.Owner.Length; i++)
                if (s.Data.Terrains[s.Terrain[i]].IsProperty && s.Owner[i] != Army && foot[i] >= Inf) return true;
            return false;
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

        /// <summary>A supply truck is worth buying when several ground units run low and the trucks we
        /// have cannot keep up (one truck looks after about four needy units).</summary>
        /// <summary>0..1: how stuck this army's money is (near the unit limit, funds piling up past what
        /// a couple of replacements cost).</summary>
        internal float Surplus(GameState s)
        {
            int count = RulesEngine.UnitCount(s, Army);
            int room = s.Rules.UnitLimit - count;
            if (room > 6) return 0f;
            float money = Clamp01((s.Funds[(int)Army] - 12000f) / 24000f);
            return money * Clamp01((7 - room) / 4f);
        }

        float pushNow0(GameState s)
        {
            float p = Math.Max(s.Day >= 8 ? Profile.EarlyPush : 0f, Profile.BreakDeadlock ? Math.Max(Surplus(s), Lead(s)) : 0f);
            if (Profile.Phases) p = Math.Max(p, Late(s));
            return p;
        }

        // ---- phases of the game: opening (the capture race), middle game (each 戦法's own plan),
        //      end game (everything goes for the enemy base) ----
        int gunKey = -1; bool gunMode;
        internal int UnitCountNow;                 // our units at the start of the current command (set by Ctx)

        /// <summary>
        /// 精鋭 against a foot army: when at least half of the enemy are infantry / mech and under one in ten of
        /// its units can really hurt a helicopter, armed helicopters (cheap, fast, out of reach of the guns
        /// and barely scratched by foot soldiers) become fighting units.
        /// </summary>
        internal bool GunshipMode(GameState s)
        {
            if (!Profile.Gunships) return false;
            int key = s.Day * 2 + (int)s.Active;
            if (gunKey == key) return gunMode;
            gunKey = key; gunMode = false;
            var heli = s.Data.Units.Find(d => d.Domain == Domain.Air && d.CargoCapacity > 0 && d.RangeMax > 0);
            if (heli == null) return false;
            int all = 0, foot = 0, aa = 0;
            foreach (var u in s.Units)
            {
                if (u.Army == Army || u.IsCarried) continue;
                var d = s.Def(u);
                all++;
                if (d.Capture > 0) foot++;
                if (s.Data.BaseDamage(d, heli) >= 40) aa++;
            }
            gunMode = all >= 6 && foot * 5 >= all * 2 && aa * 100 < all * 15;
            return gunMode;
        }

        int hqDistance = -1;
        int focusKey = -1, focusTile = -1;

        /// <summary>
        /// Middle game: one enemy property to take this phase, where the army masses instead of spreading
        /// along the whole front. The nearest one to our fighting units, avoiding the most heavily held.
        /// </summary>
        internal int FocusTile(GameState s)
        {
            int key = s.Day * 2 + (int)s.Active;
            if (focusKey == key) return focusTile;
            focusKey = key; focusTile = -1;
            if (!Profile.Focus || s.Day < 8) return -1;
            var src = new List<int>();
            foreach (var u in s.Units)
            {
                if (u.Army != Army || u.IsCarried) continue;
                var d = s.Def(u);
                if (d.Domain == Domain.Ground && d.RangeMax > 0 && !d.IsIndirect && d.Capture == 0) src.Add(s.Index(u.X, u.Y));
            }
            if (src.Count == 0) return -1;
            var dist = TerrainCost(s, MoveClass.Vehicle, src);
            float best = float.MaxValue;
            for (int i = 0; i < s.Owner.Length; i++)
            {
                var t = s.Data.Terrains[s.Terrain[i]];
                if (!t.IsProperty || s.Owner[i] == Army || s.Owner[i] == Army.None || dist[i] >= Inf) continue;
                int x = i % s.Width, y = i / s.Width;
                float held = 0;
                foreach (var e in s.Units)
                    if (e.Army != Army && !e.IsCarried && Movement.Distance(e.X, e.Y, x, y) <= 3) held += s.Def(e).Price * e.Hp / 100f;
                float sc = dist[i] * 2000f + held * 0.5f - (t.Produces != null ? 3000f : 0f);
                if (sc < best) { best = sc; focusTile = i; }
            }
            return focusTile;
        }

        /// <summary>Foot distance between the two bases (the size of the battlefield).</summary>
        int HqDistance(GameState s)
        {
            if (hqDistance >= 0) return hqDistance;
            int mine = -1, theirs = -1;
            for (int i = 0; i < s.Owner.Length; i++)
                if (s.Data.Terrains[s.Terrain[i]].Id == "HQ") { if (s.Owner[i] == Army) mine = i; else if (s.Owner[i] != Army.None) theirs = i; }
            hqDistance = s.Width + s.Height;
            if (mine >= 0 && theirs >= 0)
            {
                var c = TerrainCost(s, MoveClass.Foot, new List<int> { mine });
                if (c[theirs] < Inf) hqDistance = c[theirs];
            }
            return hqDistance;
        }

        /// <summary>0..1: how far into the end game we are. It starts when the armies have had time to meet
        /// and fight it out (later on a bigger map) and is complete ten days later.</summary>
        internal float Late(GameState s)
        {
            int start = Profile.LateBase + HqDistance(s) / 2;
            return Clamp01((s.Day - start) / (float)Math.Max(1, Profile.LateSpan));
        }

        /// <summary>0..1: how far ahead in army value we are (0 below 1.2x, 1 from 1.6x).</summary>
        internal float Lead(GameState s)
        {
            long mine = 0, theirs = 0;
            foreach (var u in s.Units)
            {
                long v = (long)s.Def(u).Price * u.Hp / 100;
                if (u.Army == Army) mine += v; else theirs += v;
            }
            // money in the bank is an army about to appear (a COM saving up is not weak)
            mine += s.Funds[(int)Army]; theirs += s.Funds[1 - (int)Army];
            if (mine <= 0) return 0f;
            float ratio = theirs <= 0 ? 9f : mine / (float)theirs;
            return Clamp01((ratio - 1.2f) / 0.4f);
        }

        bool NeedSupply(GameState s)
        {
            int ground = 0, needy = 0, trucks = 0;
            foreach (var u in s.Units)
            {
                if (u.Army != Army) continue;
                var d = s.Def(u);
                if (d.CanSupply) { trucks++; continue; }
                if (d.Domain != Domain.Ground) continue;
                ground++;
                if (Need(s, u) > 0) needy++;
            }
            return ground >= 5 && needy >= 2 && trucks * 4 < needy && trucks < 3;
        }

        // ================= evaluation context =================

        sealed class Ctx
        {
            readonly GameState s;
            readonly AiPlayer ai;
            readonly AiProfile p;
            readonly float wLoss, wThreat, wAdvance, wTerrain;
            readonly float hold;                       // 0..1: fighting units keep to our half of the map (holding a line in the middle)   // the profile's weights, eased toward trading in a deadlock
            readonly float push;                       // 0..1: time to finish the enemy off (deadlock or a clear lead)
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
                // Deadlock: at the unit limit with money nobody can spend, a lost unit is replaced the same
                // day, so a trade costs far less than its price. Fight instead of holding still.
                // A clear lead (well ahead in army value) is the other reason: stop trading blows at the front
                // and go for the base, standing on the factories next to it so that nothing new comes out.
                push = ai.pushNow0(s);
                ai.UnitCountNow = RulesEngine.UnitCount(s, me);
                wLoss = p.WLoss * (1f - 0.5f * push);
                wThreat = p.WThreat * (1f - 0.6f * push);
                wAdvance = p.WAdvance * (1f + 0.8f * push);
                wTerrain = p.WTerrain;
                hold = 0f;
                if (p.HoldFromDay > 0 && push < 0.5f)
                {
                    // 物量: forward early, then hold the line
                    if (s.Day < p.HoldFromDay) wAdvance *= 1.3f;
                    else if (EnemyPushing()) { hold = 1f; wThreat *= 0.8f; wTerrain *= 2f; }
                    // (with the enemy not coming, it advances like anyone else)
                }
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
                        else if (p.SlotMerge && ai.UnitCountNow >= 40 && u.Hp < 100 && o.Hp < 100 && u.Hp + o.Hp <= 120)
                        {
                            // near the unit limit: two battered units in one slot, the other slot for a heavy unit
                            emit(Cmd(u, x, y, UnitAction.Join), 450 - leave + Positional(u, d, start, i, kv.Value, Math.Min(100, u.Hp + o.Hp)) * 0.5f);
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

            readonly Dictionary<int, List<(UnitState c, UnitState e, int with, float g)>> keepPairs = new Dictionary<int, List<(UnitState, UnitState, int, float)>>();
            readonly Dictionary<long, float> leaveCosts = new Dictionary<long, float>();

            /// <summary>
            /// Enemies cannot pass through our units, so a unit can be what keeps an enemy away from one of
            /// our capturing squads. Cost of u moving to dest: for each enemy direct-fire ground unit that
            /// could then reach a tile next to a capturer (and cannot now), what its attack would cost the
            /// capture; a little if the move only opens one more attack spot. Moving along the barricade
            /// costs nothing; leaving it for a kill worth more than the capture still happens.
            /// </summary>
            float LeaveCost(UnitState u, int dest)
            {
                long key = (long)u.Id * 100000 + dest;
                if (leaveCosts.TryGetValue(key, out var cached)) return cached;
                if (!keepPairs.TryGetValue(u.Id, out var pairs))
                {
                    pairs = new List<(UnitState, UnitState, int, float)>();
                    int goal = s.Rules.CaptureGoal;
                    foreach (var c in s.Units)
                    {
                        if (c.Army != me || c.IsCarried || c.Id == u.Id) continue;
                        int ci = s.Index(c.X, c.Y);
                        if (s.CapturingUnit[ci] != c.Id || Movement.Distance(u.X, u.Y, c.X, c.Y) > 6) continue;
                        var cd = s.Def(c);
                        float pv = PropValue(ci);
                        foreach (var e in s.Units)
                        {
                            if (e.Army != foe || e.IsCarried || e.Ammo <= 0) continue;
                            var ed = s.Def(e);
                            if (ed.Domain != Domain.Ground || ed.IsIndirect || ed.RangeMax < 1 || s.Data.BaseDamage(ed, cd) < 0) continue;
                            if (Movement.Distance(e.X, e.Y, c.X, c.Y) > ed.Move + 1) continue;
                            int with = AttackSpots(EnemyStops(e, ed, -1, -1), c);
                            // does u matter to this enemy at all?
                            if (AttackSpots(EnemyStops(e, ed, u.Id, -1), c) <= with) continue;
                            int dmg = Combat.BaseRoll(s, e, e.Hp, c, c.X, c.Y, false) + 4;
                            float g = 0.6f * pv * Math.Min(1f, dmg / (float)Math.Max(1, c.Hp)) * Math.Min(1f, (s.CaptureProgress[ci] + c.Count) / (float)goal + 0.3f)
                                      + Math.Min(dmg, c.Hp) / 100f * cd.Price;
                            pairs.Add((c, e, with, g));
                        }
                    }
                    keepPairs[u.Id] = pairs;
                }
                float v = 0;
                foreach (var pr in pairs)
                {
                    int after = AttackSpots(EnemyStops(pr.e, s.Def(pr.e), u.Id, dest), pr.c);
                    if (after <= pr.with) continue;
                    v += pr.with == 0 ? pr.g : 0.15f * pr.g;
                }
                leaveCosts[key] = v;
                return v;
            }

            /// <summary>Tiles an enemy could end its move on (cost within its move); one of our units can be
            /// left out (ignoreId) or stand somewhere else (blockAt).</summary>
            Dictionary<int, int> EnemyStops(UnitState e, UnitDef ed, int ignoreId, int blockAt)
            {
                var cost = new Dictionary<int, int>();
                int start = s.Index(e.X, e.Y);
                cost[start] = 0;
                var open = new List<int> { start };
                int budget = Math.Min(ed.Move, e.Fuel);
                while (open.Count > 0)
                {
                    int bi = 0;
                    for (int k = 1; k < open.Count; k++) if (cost[open[k]] < cost[open[bi]]) bi = k;
                    int cur = open[bi]; open.RemoveAt(bi);
                    int x = cur % w, y = cur / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (!s.InBounds(nx, ny)) continue;
                        int ni = s.Index(nx, ny);
                        if (ni == blockAt) continue;
                        int tc = s.TerrainAt(nx, ny).Cost[(int)ed.MoveClass];
                        if (tc <= 0) continue;
                        var o = s.UnitAt(nx, ny);
                        if (o != null && o.Id == ignoreId) o = null;
                        if (o != null && o.Army != e.Army) continue;     // cannot pass through us
                        int nc = cost[cur] + tc;
                        if (nc > budget) continue;
                        if (cost.TryGetValue(ni, out var old) && old <= nc) continue;
                        cost[ni] = nc; open.Add(ni);
                    }
                }
                // only empty tiles (or where it already stands) can be stopped on
                var stops = new Dictionary<int, int>();
                foreach (var kv in cost)
                {
                    var o = s.UnitAt(kv.Key % w, kv.Key / w);
                    if (kv.Key == blockAt) continue;
                    if (kv.Key == start || o == null || o.Id == ignoreId) stops[kv.Key] = kv.Value;
                }
                return stops;
            }

            int AttackSpots(Dictionary<int, int> stops, UnitState c)
            {
                int n = 0;
                foreach (var kv in stops) if (IsAttackSpot(kv.Key, c)) n++;
                return n;
            }

            bool IsAttackSpot(int i, UnitState c) => Movement.Distance(i % w, i / w, c.X, c.Y) == 1;

            float Positional(UnitState u, UnitDef d, int start, int dest, int moveCost, int hp)
            {
                float sc = 0;
                int x = dest % w, y = dest / w;
                if (p.Has(AiFeature.Advance))
                {
                    var dm = DistFor(u);
                    int a = dm[start], b = dm[dest];
                    if (a < Inf && b < Inf) sc += wAdvance * 450f * (a - b) / Math.Max(1, d.Move);
                    if (d.IsIndirect && b < d.RangeMin && dest != start) sc -= 300;
                }
                var t = s.TerrainAt(x, y);
                if (p.Has(AiFeature.Terrain) && d.Domain != Domain.Air)
                    sc += wTerrain * t.DefenseFor(d) / 100f * d.Price * 0.2f * hp / 100f;
                if (p.Has(AiFeature.Threat)) sc -= wThreat * Threat(u, x, y, hp);
                if (p.Has(AiFeature.Fuel))
                {
                    int left = u.Fuel - moveCost;
                    bool atSupply = SupplyTile(u, d, dest);
                    if (d.FuelPerPhase > 0 || d.Domain == Domain.Sea)
                    {
                        // aircraft and ships are lost at 0 fuel: never fly or sail beyond the way home
                        int home = Dist(d.MoveClass, "supply:" + (int)d.Domain)[dest];
                        if (home >= Inf) home = 99;
                        // fuel it takes to get home from there: the way itself plus the upkeep of every phase on it
                        int trip = home + d.FuelPerPhase * (home / Math.Max(1, d.Move) + 1);
                        if (!atSupply && left < home) sc -= d.Price * 1.0f;
                        else if (!atSupply && left < trip) sc -= d.Price * 0.6f;
                    }
                    else if (d.Domain == Domain.Ground && dest != start && !atSupply)
                    {
                        // a vehicle that runs dry becomes a sitting duck: keep enough fuel to reach supply
                        if (left <= 0) sc -= d.Price * (d.CanSupply ? 2f : 0.5f);   // nobody refills a truck but a city
                        else if (left < d.Move * 2)
                        {
                            int sd = Dist(d.MoveClass, (d.CanSupply ? "supplyfac:" : "supply:") + (int)d.Domain)[dest];
                            if (sd > left) sc -= d.Price * 0.2f;
                        }
                    }
                    int need = NeedAt(u);
                    if (need > 0 && atSupply)
                        sc += d.Price * (need >= 2 ? 0.4f : 0.2f);         // wait here for 全補 or the truck
                    if (d.CanSupply)
                    {
                        // a truck parks next to units that run low (it refills them now or next phase)
                        int[] qx = { 1, -1, 0, 0 }, qy = { 0, 0, 1, -1 };
                        for (int k = 0; k < 4; k++)
                        {
                            var o = s.UnitAt(x + qx[k], y + qy[k]);
                            if (o == null || o.Army != me || o.Id == u.Id || s.Def(o).Domain != Domain.Ground) continue;
                            int on = Need(s, o);
                            if (on > 0) sc += s.Def(o).Price * (on >= 2 ? 0.25f : 0.12f);
                        }
                    }
                }
                if (t.Produces != null && s.Owner[dest] == me && RulesEngine.InProductionRange(s, me, x, y))
                {
                    // keep production sites free, except for a unit that has to refill there (an aircraft's
                    // only airport is usually next to the HQ)
                    bool refill = t.Supplies == d.Domain && (Need(s, u) > 0 || (hp <= p.RepairAt && (d.Domain == Domain.Ground || !OthersNeedRefill(u, d, dest))));
                    if (!refill) sc -= 3000;
                }
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
                if (p.Has(AiFeature.Formation) && push < 0.5f && hp <= p.RepairAt && t.IsProperty && s.Owner[dest] == me && t.Supplies == d.Domain)
                    sc += d.Price * 0.25f * (100 - hp) / 100f;   // battered units head for a city to be repaired
                // don't walk out of a barricade that is keeping an enemy off one of our capturing squads
                if (p.Has(AiFeature.Formation) && p.KeepBarricade && d.Domain == Domain.Ground && dest != start)
                    sc -= LeaveCost(u, dest);
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
                if (hold > 0 && d.Capture == 0 && d.Domain == Domain.Ground && dest != start)
                {
                    // holding the line: the middle of the map, not beyond it
                    int dOwn = Dist(MoveClass.Foot, "ownhq")[dest], dFoe = Dist(MoveClass.Foot, "foehq")[dest];
                    if (dOwn < Inf && dFoe < Inf && dFoe < dOwn - 1) sc -= hold * d.Price * 0.4f;
                }
                if (hold > 0 && p.KillZone && d.Domain == Domain.Ground)
                {
                    if (d.IsIndirect)
                    {
                        // a gun covers the empty tiles next to our front units: whoever steps up to the line
                        // gets shelled next phase
                        int cover = 0;
                        foreach (var f in s.Units)
                        {
                            if (f.Army != me || f.IsCarried || f.Id == u.Id) continue;
                            var fd = s.Def(f);
                            if (fd.IsIndirect || fd.Domain != Domain.Ground || fd.CanSupply) continue;
                            if (Movement.Distance(f.X, f.Y, x, y) > d.RangeMax + 1) continue;
                            for (int k = 0; k < 4; k++)
                            {
                                int nx = f.X + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = f.Y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                                if (!s.InBounds(nx, ny) || s.UnitAt(nx, ny) != null) continue;
                                int r = Movement.Distance(nx, ny, x, y);
                                if (r >= d.RangeMin && r <= d.RangeMax) cover++;
                            }
                        }
                        sc += hold * Math.Min(cover, 8) * 150f;
                    }
                    else if (d.RangeMax > 0)
                    {
                        // a front unit stands where our guns can shell whoever attacks it
                        foreach (var g in s.Units)
                        {
                            if (g.Army != me || g.IsCarried || g.Id == u.Id || !s.Def(g).IsIndirect) continue;
                            int r = Movement.Distance(g.X, g.Y, x, y);
                            if (r >= s.Def(g).RangeMin - 1 && r <= s.Def(g).RangeMax - 1) { sc += hold * d.Price * 0.15f; break; }
                        }
                    }
                }
                if (hold > 0 && d.Domain == Domain.Ground && d.Capture == 0)
                {
                    // a line has no gaps: stand next to each other (and guns stand behind someone)
                    int nb = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        var o = s.UnitAt(x + (k == 0 ? 1 : k == 1 ? -1 : 0), y + (k == 2 ? 1 : k == 3 ? -1 : 0));
                        if (o != null && o.Army == me && o.Id != u.Id) nb++;
                    }
                    sc += hold * Math.Min(nb, 2) * d.Price * 0.05f;
                }
                if (push > 0 && d.Domain == Domain.Ground && s.Owner[dest] == foe && t.Produces != null && RulesEngine.InProductionRange(s, foe, x, y))
                {
                    if (t.Id != "HQ") sc += 2500f * push;              // nothing can be built on a factory we stand on
                    else if (d.Capture < 2) sc -= 2500f * push;        // and the base itself is for the infantry to take
                }
                if (p.Noise > 0) sc += ai.rng.Range(0, p.Noise);
                return sc;
            }

            /// <summary>
            /// Net value lost if enemies attack this tile next phase. Combat is simultaneous, so an attacker
            /// also eats our counter-fire: the threat is (our loss) - (their loss), strongest attacker in full,
            /// the second at half (each enemy attacks only once and has other targets).
            /// </summary>
            /// <summary>Is the enemy coming at us: three or more of its fighting units in our half of the map?</summary>
            bool EnemyPushing()
            {
                var own = Dist(MoveClass.Foot, "ownhq"); var theirs = Dist(MoveClass.Foot, "foehq");
                int n = 0;
                foreach (var e in s.Units)
                {
                    if (e.Army != foe || e.IsCarried || s.Def(e).RangeMax <= 0) continue;
                    int i = s.Index(e.X, e.Y);
                    if (own[i] < Inf && theirs[i] < Inf && own[i] <= theirs[i] && ++n >= 3) return true;
                }
                return false;
            }

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
                if (hold > 0)
                {
                    // holding a line: whatever has come into our half is caught and finished off there
                    int dOwn = Dist(MoveClass.Foot, "ownhq")[ti], dFoe = Dist(MoveClass.Foot, "foehq")[ti];
                    if (dOwn < Inf && dFoe < Inf && dOwn <= dFoe + 1)
                        sc += hold * Value(t) * (0.4f * Math.Min(1f, dmg / (float)Math.Max(1, t.Hp)) + (dmg >= t.Hp ? 0.3f : 0f));
                }
                if (push > 0)
                {
                    // finishing off: clear the enemy base and the factories next to it
                    var tt = s.Data.Terrains[s.Terrain[ti]];
                    if (s.Owner[ti] == foe && tt.Produces != null && RulesEngine.InProductionRange(s, foe, t.X, t.Y))
                        sc += 2000f * push * Math.Min(1f, dmg / (float)Math.Max(1, t.Hp)) + (dmg >= t.Hp ? 1000f * push : 0);
                }
                sc -= wLoss * cdmg / 100f * Value(u);
                if (cdmg >= u.Hp) sc -= 0.35f * Value(u);
                if ((p.FocusFire || push >= 0.5f) && dmg < t.Hp)
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
                if (p.Has(AiFeature.Threat)) sc -= wThreat * Threat(cargo, drop % w, drop / w, cargo.Hp);
                return sc;
            }

            /// <summary>Would a unit standing on tile i be refilled next phase (own facility of its domain, or
            /// a land tile next to one of our trucks)?</summary>
            bool SupplyTile(UnitState u, UnitDef d, int i)
            {
                var t = s.Data.Terrains[s.Terrain[i]];
                if (t.IsProperty && s.Owner[i] == me && t.Supplies == d.Domain) return true;
                if (d.Domain != Domain.Ground) return false;
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    var o = s.UnitAt(x + (k == 0 ? 1 : k == 1 ? -1 : 0), y + (k == 2 ? 1 : k == 3 ? -1 : 0));
                    if (o != null && o.Army == me && o.Id != u.Id && !o.IsCarried && s.Def(o).CanSupply) return true;
                }
                return false;
            }

            /// <summary>Need(), and for aircraft and ships also "the way home is getting long": turn back
            /// while there is still fuel for the trip plus a move of slack.</summary>
            int NeedAt(UnitState u)
            {
                int n = AiPlayer.Need(s, u);
                if (n > 0) return n;
                var d = s.Def(u);
                if (p.CarefulSupply && d.Capture == 0 && !d.CanSupply)
                {
                    // 精鋭: refill at half ammo, and bring valuable units back for repairs before they are lost
                    if (d.Ammo > 0 && u.Ammo * 2 <= d.Ammo) return 1;
                    if (d.Price >= 6000 && u.Hp <= 50) return 1;
                }
                if (d.Domain == Domain.Ground || d.CanSupply) return 0;
                int home = Dist(d.MoveClass, "supply:" + (int)d.Domain)[s.Index(u.X, u.Y)];
                if (home >= Inf) return 0;
                int trip = home + d.FuelPerPhase * (home / Math.Max(1, d.Move) + 2) + d.Move;
                return u.Fuel <= trip ? 1 : 0;
            }

            /// <summary>Another of our units of the same kind (aircraft or ship) is running low near this tile.</summary>
            bool OthersNeedRefill(UnitState u, UnitDef d, int at)
            {
                foreach (var o in s.Units)
                {
                    if (o.Army != me || o.IsCarried || o.Id == u.Id) continue;
                    var od = s.Def(o);
                    if (od.Domain != d.Domain || AiPlayer.Need(s, o) == 0) continue;
                    if (Movement.Distance(o.X, o.Y, at % w, at / w) <= od.Move * 2) return true;
                }
                return false;
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
                // low on fuel or ammo: head for a refill first (aircraft even with passengers: they crash at 0)
                if (p.Has(AiFeature.Fuel) && d.Capture == 0 && (d.CargoCapacity == 0 || u.Cargo.Count == 0 || d.Domain == Domain.Air) && NeedAt(u) > 0)
                {
                    // (a truck that runs low itself goes to a city; it cannot refill itself)
                    string key = (d.CanSupply ? "supplyfac:" : "supply:") + (int)d.Domain;
                    var sd = Dist(d.MoveClass, key);
                    if (!emptyDist.Contains(d.MoveClass + key)) return sd;
                }
                if (d.CargoCapacity > 0 && d.Domain == Domain.Air && d.RangeMax > 0 && u.Cargo.Count == 0 && ai.GunshipMode(s))
                    return Dist(d.MoveClass, "enemy:" + d.Index);          // gunship: goes hunting
                if (d.CargoCapacity > 0)
                {
                    if (u.Cargo.Count > 0)
                    {
                        // end game: the passengers go to the enemy base
                        if (push >= 0.6f) { var hq = Dist(d.MoveClass, "foehq"); if (hq[s.Index(u.X, u.Y)] < Inf) return hq; }
                        return Dist(d.MoveClass, "cap2");
                    }
                    var pick = Dist(d.MoveClass, "pickup");
                    if (!emptyDist.Contains(d.MoveClass + "pickup")) return pick;
                    if (p.TransportTactics && d.Domain == Domain.Ground)
                    {
                        var scr = Dist(d.MoveClass, "screen");
                        if (!emptyDist.Contains(d.MoveClass + "screen")) return scr;
                    }
                    return Dist(d.MoveClass, "own");
                }
                if (d.Capture >= 2 && push >= 0.6f && s.CapturingUnit[s.Index(u.X, u.Y)] != u.Id)
                {
                    // end game: infantry (the only ones who can take a base) head for the enemy base
                    var hq = Dist(MoveClass.Foot, "foehq");
                    if (hq[s.Index(u.X, u.Y)] < Inf) return hq;
                }
                if (d.Capture > 0) return Dist(MoveClass.Foot, d.Capture >= 2 ? "cap2" : "cap1");
                if (d.CanSupply)
                {
                    var n = Dist(d.MoveClass, "needy");
                    return emptyDist.Contains(d.MoveClass + "needy") ? Dist(d.MoveClass, "own") : n;
                }
                if (push >= 0.5f && d.Domain == Domain.Ground && d.RangeMax > 0 && !d.IsIndirect)
                {
                    var fp = Dist(d.MoveClass, "foeprod");
                    if (!emptyDist.Contains(d.MoveClass + "foeprod") && fp[s.Index(u.X, u.Y)] < Inf) return fp;
                }
                // middle game: mass on the property picked for this phase
                if (p.Focus && push < 0.5f && d.Domain == Domain.Ground && d.RangeMax > 0 && !d.IsIndirect && d.Capture == 0 && ai.FocusTile(s) >= 0)
                {
                    var fo = Dist(d.MoveClass, "focus");
                    if (fo[s.Index(u.X, u.Y)] < Inf) return fo;
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
                else if (key.StartsWith("supply:") || key.StartsWith("supplyfac:"))
                {
                    // where a unit of this domain gets refilled: our own supplying facilities (not the
                    // production sites next to the HQ, which must stay free) and, on land, next to a truck
                    var dom = (Domain)int.Parse(key.Substring(key.IndexOf(':') + 1));
                    for (int i = 0; i < n; i++)
                    {
                        var t = s.Data.Terrains[s.Terrain[i]];
                        if (s.Owner[i] != me || !t.IsProperty || t.Supplies != dom) continue;
                        if (dom == Domain.Ground && t.Produces != null && RulesEngine.InProductionRange(s, me, i % w, i / w)) continue;
                        if (dom != Domain.Ground)
                        {
                            // an airport or port taken by a unit that is staying there is no use to the others
                            var o = s.UnitAt(i % w, i / w);
                            if (o != null && (o.Army != me || AiPlayer.Need(s, o) > 0 || o.Hp <= 60)) continue;
                        }
                        src.Add(i);
                    }
                    if (dom == Domain.Ground && key.StartsWith("supply:"))
                        foreach (var u in s.Units)
                        {
                            if (u.Army != me || u.IsCarried || !s.Def(u).CanSupply) continue;
                            for (int k = 0; k < 4; k++)
                            {
                                int x = u.X + (k == 0 ? 1 : k == 1 ? -1 : 0), y = u.Y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                                if (s.InBounds(x, y)) src.Add(s.Index(x, y));
                            }
                        }
                }
                else if (key == "focus")
                {
                    int f = ai.FocusTile(s);
                    if (f >= 0) src.Add(f);
                }
                else if (key == "foeprod")
                {
                    // the enemy's base and the production sites around it
                    for (int i = 0; i < n; i++)
                    {
                        var t = s.Data.Terrains[s.Terrain[i]];
                        if (s.Owner[i] == foe && t.Produces == Domain.Ground && RulesEngine.InProductionRange(s, foe, i % w, i / w)) src.Add(i);
                    }
                }
                else if (key == "ownhq")
                {
                    for (int i = 0; i < n; i++)
                        if (s.Owner[i] == me && s.Data.Terrains[s.Terrain[i]].Id == "HQ") src.Add(i);
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
                        if (AiPlayer.Need(s, u) > 0) src.Add(s.Index(u.X, u.Y));
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
