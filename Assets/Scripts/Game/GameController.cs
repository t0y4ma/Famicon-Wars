using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FamiconWars.Core;
using FamiconWars.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FamiconWars.Game
{
    /// <summary>
    /// Screen flow: Title → Map select → Setup → Match → Game over (again / back to title).
    /// In a match each army is a human (hot seat) or a COM. Every action, human or COM,
    /// becomes a Command passed to RulesEngine — the same path online play will use.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        enum Mode { Screens, Idle, Selected, Menu, Target, Drop, Produce, GameOver }

        GameData data;
        GameState state;
        BoardView board;
        GameHud hud;
        Camera cam;
        GameSettings settings;
        readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();

        readonly AiPlayer[] ai = new AiPlayer[2];
        bool aiRunning, paused;

        Mode mode = Mode.Screens;
        UnitState selected;
        MoveResult reach;
        int destX, destY;
        int cargoForDrop = -1;
        int produceX, produceY;
        readonly List<(string label, System.Action act)> menu = new List<(string, System.Action)>();
        int hoverX = -1, hoverY = -1;

        int Speed => settings.Speed;

        // online play
        bool online, awaiting;
        int myArmy;
        OnlineReplica replica;
        string roomCode, pendingLobby, pendingCode;
        // test bot: "-fwbot <address> <code>" joins a room and lets the COM play this side (also settable from the editor)
        public int onlineBotLevel;
        AiPlayer onlineBot;
        Command botLast;
        float botNext;

        void Start()
        {
            data = GameData.Load(Text("Data/units"), Text("Data/terrains"), Text("Data/damage"), Text("Data/rules"));
            if (NetConfig.IsDedicatedServer)
            {
                // headless server build: no screens, just rooms
                Application.targetFrameRate = 15;   // turn-based: spare the small VM's CPU
                FwNetworkManager.Create(data).StartServer();
                enabled = false;
                return;
            }
            settings = GameSettings.Load();
            if (MapCatalog.Find(settings.MapId) == null || !MapCatalog.Find(settings.MapId).Ready) settings.MapId = "map01";

            hud = gameObject.AddComponent<GameHud>();
            hud.Build();
            hud.OnResupply += () => { if (!HumanTurn) return; if (mode == Mode.Idle) Try(new ResupplyAllCommand { Army = state.Active }); else hud.Toast("部隊の操作中は全補できません", true); };
            hud.OnEndPhase += () => { if (!HumanTurn) return; Cancel(); Try(new EndPhaseCommand { Army = state.Active }); };
            hud.OnSurrender += () => { if (!HumanTurn) return; Cancel(); Try(new SurrenderCommand { Army = state.Active }); };
            hud.OnCloseProduce += Cancel;
            hud.OnSpeed += () => { settings.Speed = (settings.Speed + 1) % GameSettings.SpeedNames.Length; settings.Save(); RefreshHud(); };

            hud.OnTitleStart += ShowMapSelect;
            hud.OnMapBack += ShowTitle;
            hud.OnMapPicked += id => { var m = MapCatalog.Find(id); if (m != null && m.Ready) settings.MapId = id; };
            hud.OnMapNext += ShowSetup;
            hud.OnSetupBack += ShowMapSelect;
            hud.OnSetupStart += () => { settings.Save(); StartMatch(); };
            hud.OnRestart += () => { if (online) { LeaveOnline(); ShowOnlineLobby(); } else ShowSetup(); };
            hud.OnBackToTitle += () => { if (online || FwClient.IsConnected) { LeaveOnline(); FwClient.Disconnect(); } ShowTitle(); };

            hud.OnTitleOnline += ShowOnlineLobby;
            hud.OnOnlineBack += () => { FwClient.Disconnect(); ShowTitle(); };
            hud.OnOnlineCreate += addr => OnlineGo(addr, "create", null);
            hud.OnOnlineJoin += (addr, code) => OnlineGo(addr, "join", code);
            hud.OnOnlineCancel += () => { if (FwClient.IsConnected) FwClient.LeaveRoom(); ShowOnlineLobby(); };
            FwClient.Connected += OnNetConnected;
            FwClient.Disconnected += OnNetDisconnected;
            FwClient.Error += OnNetError;
            FwClient.Status += OnNetStatus;
            FwClient.Log += StartOnlineMatch;
            FwClient.Applied += ApplyRemote;
            FwClient.Rejected += OnNetRejected;
            FwClient.LobbyError += OnNetLobbyError;
            ShowTitle();

            var args = System.Environment.GetCommandLineArgs();
            int bi = System.Array.IndexOf(args, "-fwbot");
            if (bi >= 0 && bi + 2 < args.Length)
            {
                onlineBotLevel = 2;
                settings.BattleAnimation = false;
                OnlineGo(args[bi + 1], "join", args[bi + 2]);
            }
        }

        static string Text(string path)
        {
            var ta = Resources.Load<TextAsset>(path);
            if (ta == null) throw new System.Exception("Missing resource " + path);
            return ta.text;
        }

        // ---------------- screens ----------------

        /// <summary>Stops any running match and clears everything that belongs to it.</summary>
        void LeaveMatch()
        {
            StopAllCoroutines();
            aiRunning = false; paused = false;
            selected = null;
            mode = Mode.Screens;
            hud.SkipBattle();
            hud.ResetScreens();
            hud.ShowMatchChrome(false);
            if (board != null) { board.ClearHighlights(); board.SetCursor(0, 0, false); board.gameObject.SetActive(false); }
        }

        void ShowTitle()
        {
            LeaveMatch();
            hud.ShowTitle();
        }

        void ShowMapSelect()
        {
            LeaveMatch();
            hud.ShowMapSelect(MapCatalog.All, settings.MapId, Preview);
        }

        void ShowSetup()
        {
            LeaveMatch();
            var m = MapCatalog.Find(settings.MapId);
            hud.ShowSetupScreen(settings, m != null ? m.Name : settings.MapId);
        }

        /// <summary>Mini map drawn from the terrain colours (3px per tile, owned properties in army colour).</summary>
        Texture2D Preview(string id)
        {
            if (previews.TryGetValue(id, out var cached)) return cached;
            var ta = Resources.Load<TextAsset>("Maps/" + id);
            if (ta == null) return null;
            var s = GameState.Create(data, MapDef.Parse(ta.text, data), 1);
            const int k = 3;
            var tex = new Texture2D(s.Width * k, s.Height * k, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[tex.width * tex.height];
            for (int y = 0; y < s.Height; y++)
            for (int x = 0; x < s.Width; x++)
            {
                int i = s.Index(x, y);
                var t = data.Terrains[s.Terrain[i]];
                ColorUtility.TryParseHtmlString(t.Color, out var c);
                var o = s.Owner[i];
                var inner = t.IsProperty && o != Army.None ? (o == Army.Red ? BoardView.RedArmy : BoardView.BlueArmy) : c;
                for (int dy = 0; dy < k; dy++)
                for (int dx = 0; dx < k; dx++)
                {
                    bool centre = dx == 1 && dy == 1;
                    int py = (s.Height - 1 - y) * k + dy;     // texture rows run bottom-up
                    px[py * tex.width + x * k + dx] = t.IsProperty ? (centre ? Color.white : inner) : c;
                }
            }
            foreach (var u in s.Units.Where(u => !u.IsCarried))
            {
                int py = (s.Height - 1 - u.Y) * k + 1;
                px[py * tex.width + u.X * k + 1] = u.Army == Army.Red ? BoardView.RedArmy : BoardView.BlueArmy;
            }
            tex.SetPixels(px); tex.Apply();
            previews[id] = tex;
            return tex;
        }

        // ---------------- match ----------------

        void StartMatch()
        {
            LeaveMatch();
            uint seed = (uint)System.Environment.TickCount;
            for (int a = 0; a < 2; a++)
                ai[a] = settings.Players[a] > 0 ? new AiPlayer((Army)a, AiProfile.ForLevel(settings.Players[a]), seed + (uint)a * 977u) : null;
            LoadMap(settings.MapId);
            hud.ShowMatchChrome(true);
            mode = Mode.Idle;
            var r = RulesEngine.StartGame(state);
            Report(r, null);
            RefreshHud();
        }

        void LoadMap(string id)
        {
            var map = MapDef.Parse(Text("Maps/" + id), data);
            state = GameState.Create(data, map, (uint)System.Environment.TickCount);
            BuildBoard();
        }

        void BuildBoard()
        {
            if (board == null) board = new GameObject("Board").AddComponent<BoardView>();
            board.gameObject.SetActive(true);
            board.Build(state);
            SetupCamera();
        }

        void SetupCamera()
        {
            cam = Camera.main;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.07f);
            cam.transform.rotation = Quaternion.identity;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            cam.orthographicSize = Mathf.Max(state.Height / 2f + 1.3f, (state.Width / 2f + 0.5f) / aspect);
            // leave room for the 76px top bar (about 7% of the height)
            cam.transform.position = new Vector3((state.Width - 1) / 2f, -(state.Height - 1) / 2f + cam.orthographicSize * 0.07f, -10);
        }

        bool InMatch => state != null && mode != Mode.Screens;
        bool ComTurn => InMatch && !online && !state.GameOver && ai[(int)state.Active] != null;
        bool HumanTurn => InMatch && !state.GameOver && (online ? (int)state.Active == myArmy && !awaiting : ai[(int)state.Active] == null);

        string ControllerName(Army a) => online ? ((int)a == myArmy ? "あなた" : "相手") : ai[(int)a] == null ? "人間" : "COM " + ai[(int)a].Profile.Name;

        int Income(Army a)
        {
            int sum = 0;
            for (int i = 0; i < state.Owner.Length; i++) if (state.Owner[i] == a) sum += data.Terrains[state.Terrain[i]].Income;
            return sum;
        }

        void RefreshHud()
        {
            if (!InMatch) return;
            hud.SetTopBar(state, Income(state.Active), ControllerName(state.Active), HumanTurn, GameSettings.SpeedNames[Speed]);
        }

        // ---------------- input ----------------

        void Update()
        {
            if (!InMatch || cam == null) return;
            if (online && onlineBot != null && HumanTurn && !hud.BattlePlaying && !hud.BannerShowing && Time.unscaledTime >= botNext)
            {
                botNext = Time.unscaledTime + 0.15f;
                var bc = onlineBot.Next(state);
                if (bc != null) { botLast = bc; if (!Try(bc)) onlineBot.Rejected(bc); }
            }
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            // a click or Space skips the battle cut-in
            if (hud.BattlePlaying)
            {
                if ((mouse != null && mouse.leftButton.wasPressedThisFrame) || (kb != null && kb.spaceKey.wasPressedThisFrame)) hud.SkipBattle();
                return;
            }

            if (kb != null && kb.spaceKey.wasPressedThisFrame && ComTurn)
            {
                paused = !paused;
                hud.Hint(paused ? "一時停止中(Spaceで再開)" : null);
            }
            if (ComTurn && !aiRunning) StartCoroutine(RunCom());

            if (mouse == null) return;
            Vector2 mp = mouse.position.ReadValue();
            var world = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, 10));
            hoverX = Mathf.RoundToInt(world.x); hoverY = Mathf.RoundToInt(-world.y);
            bool overUi = hud.PointerOverUi;
            if (!overUi) hud.ShowInfo(state, hoverX, hoverY, ForecastText());
            if (!HumanTurn) return;   // the COM drives the cursor during its phase

            board.SetCursor(hoverX, hoverY, !overUi && mode != Mode.GameOver && mode != Mode.Produce);
            bool cancel = mouse.rightButton.wasPressedThisFrame || (kb != null && kb.escapeKey.wasPressedThisFrame);
            if (cancel) { Cancel(); return; }
            if (!mouse.leftButton.wasPressedThisFrame || overUi) return;
            if (!state.InBounds(hoverX, hoverY)) { Cancel(); return; }
            Click(hoverX, hoverY);
        }

        // ---------------- COM ----------------

        IEnumerator RunCom()
        {
            aiRunning = true;
            int sinceYield = 0;
            while (ComTurn)
            {
                while (paused) yield return null;
                var com = ai[(int)state.Active];
                var cmd = com.Next(state);
                if (cmd == null) break;
                float d = GameSettings.SpeedDelay[Speed];

                if (d > 0) yield return ShowIntent(cmd, d);

                var cast = BattleCast(cmd);
                var r = RulesEngine.Apply(state, cmd);
                board.ClearHighlights();
                if (!r.Ok) { com.Rejected(cmd); board.Refresh(); continue; }
                bool battle = Report(r, cast);
                board.Refresh();
                RefreshHud();

                if (battle) { while (hud.BattlePlaying) yield return null; }
                if (cmd is EndPhaseCommand)
                {
                    if (d > 0) { while (hud.BannerShowing) yield return null; }
                    else yield return null;
                }
                else if (d > 0) yield return new WaitForSeconds(d * 0.5f);
                else if (++sinceYield % 4 == 0) yield return null;
            }
            board.SetCursor(0, 0, false);
            board.ClearHighlights();
            aiRunning = false;
            if (!paused) hud.Hint(null);
            RefreshHud();
        }

        /// <summary>Shows what the COM is about to do: the unit, its path, and its target.</summary>
        IEnumerator ShowIntent(Command cmd, float d)
        {
            if (cmd is UnitCommand uc)
            {
                var u = state.UnitById(uc.UnitId);
                if (u == null) yield break;
                board.SetCursor(u.X, u.Y, true);
                var path = Movement.Reachable(state, u).PathTo(state.Index(uc.ToX, uc.ToY));
                board.ClearHighlights();
                board.Highlight(path, new Color(0.3f, 0.6f, 1f, 0.5f));
                yield return new WaitForSeconds(d * 0.5f);
                board.Refresh(u.Id, uc.ToX, uc.ToY);
                board.SetCursor(uc.ToX, uc.ToY, true);
                if (uc.Action == UnitAction.Attack)
                {
                    var t = state.UnitById(uc.TargetId);
                    if (t != null)
                    {
                        board.ClearHighlights();
                        board.Highlight(new[] { state.Index(t.X, t.Y) }, new Color(1f, 0.25f, 0.2f, 0.6f));
                        board.SetCursor(t.X, t.Y, true);
                        yield return new WaitForSeconds(d * 0.6f);
                    }
                }
            }
            else if (cmd is ProduceCommand pc)
            {
                board.SetCursor(pc.X, pc.Y, true);
                yield return new WaitForSeconds(d * 0.4f);
            }
        }

        // ---------------- battle cut-in ----------------

        /// <summary>Who fights where must be read before Apply: a destroyed unit is gone afterwards.</summary>
        BattleSetup BattleCast(Command c)
        {
            if (!(c is UnitCommand uc) || uc.Action != UnitAction.Attack) return null;
            var a = state.UnitById(uc.UnitId);
            var d = state.UnitById(uc.TargetId);
            if (a == null || d == null) return null;
            return new BattleSetup
            {
                AttName = Labels.Get(state.Def(a).NameKey), AttArmy = a.Army, AttDef = state.Def(a), AttTerrain = state.TerrainAt(uc.ToX, uc.ToY),
                DefName = Labels.Get(state.Def(d).NameKey), DefArmy = d.Army, DefDef = state.Def(d), DefTerrain = state.TerrainAt(d.X, d.Y),
            };
        }

        bool BattleAnimationOn => settings.BattleAnimation && GameSettings.SpeedDelay[Speed] > 0;

        // ---------------- human input ----------------

        string ForecastText()
        {
            if (mode != Mode.Target || selected == null || !state.InBounds(hoverX, hoverY)) return null;
            var u = state.UnitAt(hoverX, hoverY);
            if (u == null || u.Army == selected.Army) return null;
            var ghost = selected.Clone(); ghost.X = destX; ghost.Y = destY;
            if (!Combat.CanAttackFrom(state, ghost, u, destX, destY, destX != selected.X || destY != selected.Y)) return null;
            var f = Combat.Predict(state, ghost, destX, destY, u);
            string them = $"相手  {u.Count} → <b>{Range(Left(u.Hp, f.DamageToDefenderMax), Left(u.Hp, f.DamageToDefenderMin))}</b>";
            string us = f.Counter
                ? $"自軍  {selected.Count} → <b>{Range(Left(selected.Hp, f.DamageToAttackerMax), Left(selected.Hp, f.DamageToAttackerMin))}</b>"
                : "自軍  反撃なし";
            return them + "\n" + us;
        }

        static int Left(int hp, int dmg) => Mathf.Max(0, (hp - dmg + 9) / 10);

        void Click(int x, int y)
        {
            switch (mode)
            {
                case Mode.Idle:
                {
                    var u = state.UnitAt(x, y);
                    if (u != null && u.Army == state.Active && !u.Acted) { Select(u); return; }
                    if (u == null && RulesEngine.ProducibleAt(state, state.Active, x, y).Count > 0) { OpenProduce(x, y); return; }
                    break;
                }
                case Mode.Selected:
                {
                    int i = state.Index(x, y);
                    if (!reach.Reaches(i)) { Cancel(); return; }
                    var stop = Movement.StopAt(state, selected, x, y);
                    if (stop == StopKind.None || (reach.EndOnly.Contains(i) && stop != StopKind.Load)) { hud.Toast("そのマスには止まれません", true); return; }
                    destX = x; destY = y;
                    BuildMenu();
                    mode = Mode.Menu;
                    board.ClearHighlights();
                    board.Refresh(selected.Id, x, y);
                    hud.Hint(null);
                    hud.ShowActionMenu(menu, cam.WorldToScreenPoint(new Vector3(x, -y, 0)));
                    return;
                }
                case Mode.Menu:
                    Cancel();
                    return;
                case Mode.Target:
                {
                    var t = state.UnitAt(x, y);
                    if (t != null && Try(new UnitCommand { Army = state.Active, UnitId = selected.Id, ToX = destX, ToY = destY, Action = UnitAction.Attack, TargetId = t.Id }))
                        Done();
                    return;
                }
                case Mode.Drop:
                {
                    if (Try(new UnitCommand { Army = state.Active, UnitId = selected.Id, ToX = destX, ToY = destY, Action = UnitAction.Unload, CargoId = cargoForDrop, DropX = x, DropY = y }))
                        Done();
                    return;
                }
            }
        }

        void Select(UnitState u)
        {
            selected = u;
            reach = Movement.Reachable(state, u);
            mode = Mode.Selected;
            board.ClearHighlights();
            board.Highlight(reach.Cost.Keys.Where(i => Movement.StopAt(state, u, i % state.Width, i / state.Width) != StopKind.None), new Color(0.3f, 0.6f, 1f, 0.45f));
            board.Highlight(AttackPreview(u), new Color(1f, 0.25f, 0.2f, 0.45f));
            var joins = reach.Cost.Keys.Where(i => Movement.StopAt(state, u, i % state.Width, i / state.Width) == StopKind.Join).ToList();
            board.Highlight(joins, new Color(0.2f, 1f, 0.35f, 0.85f));
            hud.Hint(joins.Count > 0 ? "移動先をクリック(緑=合流 / 右クリックで取り消し)" : "移動先をクリック(右クリックで取り消し)");
        }

        /// <summary>Enemy tiles this unit could hit this phase (from any reachable tile; indirect only from where it stands).</summary>
        IEnumerable<int> AttackPreview(UnitState u)
        {
            var d = state.Def(u);
            var set = new HashSet<int>();
            if (d.RangeMax <= 0) return set;
            foreach (var e in state.Units.Where(e => e.Army != u.Army && !e.IsCarried && Combat.CanTarget(state, u, e)))
            {
                if (d.IsIndirect) { if (Combat.CanAttackFrom(state, u, e, u.X, u.Y, false)) set.Add(state.Index(e.X, e.Y)); continue; }
                foreach (var i in reach.Cost.Keys)
                {
                    int x = i % state.Width, y = i / state.Width;
                    if (Movement.StopAt(state, u, x, y) == StopKind.Empty && Movement.Distance(x, y, e.X, e.Y) == 1) { set.Add(state.Index(e.X, e.Y)); break; }
                }
            }
            return set;
        }

        void BuildMenu()
        {
            menu.Clear();
            var u = selected;
            UnitCommand Cmd(UnitAction a) => new UnitCommand { Army = state.Active, UnitId = u.Id, ToX = destX, ToY = destY, Action = a };
            bool Ok(Command c) => RulesEngine.Check(state, c) == null;

            var targets = state.Units.Where(e => e.Army != u.Army && Ok(new UnitCommand { Army = state.Active, UnitId = u.Id, ToX = destX, ToY = destY, Action = UnitAction.Attack, TargetId = e.Id })).ToList();
            if (targets.Count > 0) menu.Add(("攻撃", () => EnterTarget(targets)));
            if (Ok(Cmd(UnitAction.Capture))) menu.Add(("占領", () => { if (Try(Cmd(UnitAction.Capture))) Done(); }));
            if (Ok(Cmd(UnitAction.Supply))) menu.Add(("補給", () => { if (Try(Cmd(UnitAction.Supply))) Done(); }));
            if (Ok(Cmd(UnitAction.Join))) menu.Add(("合流", () => { if (Try(Cmd(UnitAction.Join))) Done(); }));
            if (Ok(Cmd(UnitAction.Load))) menu.Add(("搭載", () => { if (Try(Cmd(UnitAction.Load))) Done(); }));
            foreach (var cid in u.Cargo)
            {
                int id = cid;
                var drops = DropTiles(id);
                if (drops.Count > 0) menu.Add(("降車: " + Labels.Get(state.Def(state.UnitById(id)).NameKey), () => EnterDrop(id, drops)));
            }
            if (Ok(Cmd(UnitAction.Wait))) menu.Add(("待機", () => { if (Try(Cmd(UnitAction.Wait))) Done(); }));
        }

        List<int> DropTiles(int cargoId)
        {
            var list = new List<int>();
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            for (int k = 0; k < 4; k++)
            {
                var c = new UnitCommand { Army = state.Active, UnitId = selected.Id, ToX = destX, ToY = destY, Action = UnitAction.Unload, CargoId = cargoId, DropX = destX + dx[k], DropY = destY + dy[k] };
                if (RulesEngine.Check(state, c) == null) list.Add(state.Index(c.DropX, c.DropY));
            }
            return list;
        }

        void EnterTarget(List<UnitState> targets)
        {
            mode = Mode.Target;
            hud.HideActionMenu();
            board.ClearHighlights();
            board.Highlight(targets.Select(t => state.Index(t.X, t.Y)), new Color(1f, 0.25f, 0.2f, 0.55f));
            hud.Hint("攻撃する相手を選択(右クリックで戻る)");
        }

        void EnterDrop(int cargoId, List<int> tiles)
        {
            cargoForDrop = cargoId;
            mode = Mode.Drop;
            hud.HideActionMenu();
            board.ClearHighlights();
            board.Highlight(tiles, new Color(0.3f, 1f, 0.5f, 0.5f));
            hud.Hint("降ろすマスを選択(右クリックで戻る)");
        }

        void OpenProduce(int x, int y)
        {
            if (RulesEngine.UnitCount(state, state.Active) >= state.Rules.UnitLimit) { hud.Toast("部隊数が上限(" + state.Rules.UnitLimit + ")に達しているため生産できません", true); return; }
            produceX = x; produceY = y;
            mode = Mode.Produce;
            var list = RulesEngine.ProducibleAt(state, state.Active, x, y);
            hud.ShowProduce(Labels.Get(state.TerrainAt(x, y).NameKey), state.Funds[(int)state.Active], list, d =>
            {
                if (Try(new ProduceCommand { Army = state.Active, X = produceX, Y = produceY, UnitType = d.Id })) Cancel();
            });
        }

        void Cancel()
        {
            if (mode == Mode.GameOver || mode == Mode.Screens) return;
            if (mode == Mode.Target || mode == Mode.Drop)
            {
                mode = Mode.Menu;
                board.ClearHighlights();
                hud.Hint(null);
                hud.ShowActionMenu(menu, cam.WorldToScreenPoint(new Vector3(destX, -destY, 0)));
                return;
            }
            mode = Mode.Idle;
            selected = null;
            board.ClearHighlights();
            board.Refresh();
            hud.HideActionMenu();
            hud.HideProduce();
            hud.Hint(null);
        }

        void Done()
        {
            selected = null;
            board.ClearHighlights();
            board.Refresh();
            hud.HideActionMenu();
            hud.Hint(null);
            if (!state.GameOver) mode = Mode.Idle;
        }

        bool Try(Command c)
        {
            if (online)
            {
                // check locally for instant feedback; the server checks again and has the final word
                var err = RulesEngine.Check(state, c);
                if (err != null) { hud.Toast(err, true); return false; }
                awaiting = true;
                FwClient.Play(c);
                RefreshHud();
                return true;
            }
            var cast = BattleCast(c);
            var r = RulesEngine.Apply(state, c);
            if (!r.Ok) { hud.Toast(r.Error, true); return false; }
            Report(r, cast);
            board.Refresh();
            RefreshHud();
            return true;
        }

        /// <returns>true when a battle cut-in started.</returns>
        bool Report(ApplyResult r, BattleSetup cast)
        {
            var lines = new List<string>();
            bool cutIn = false;
            foreach (var e in r.Events)
            {
                switch (e)
                {
                    case BattleEvent b:
                        if (cast != null && BattleAnimationOn)
                        {
                            cast.Event = b;
                            hud.PlayBattle(cast, GameSettings.BattleTime[Speed]);
                            cutIn = true;
                        }
                        else lines.Add($"戦闘  攻撃側 {b.AttackerCountBefore}→{b.AttackerCountAfter}   防御側 {b.DefenderCountBefore}→{b.DefenderCountAfter}" + (b.Counter ? "" : "(反撃なし)"));
                        break;
                    case JoinedEvent j:
                        lines.Add($"合流  {j.CountB} + {j.CountA} → {j.CountAfter}" + (j.Overflow > 0 ? $"(超過した{j.Overflow}機は失われました)" : ""));
                        break;
                    case CaptureEvent cap:
                        lines.Add(cap.Completed ? "占領完了" : $"占領中  {cap.Progress}/{cap.Goal}");
                        break;
                    case PhaseStartEvent p:
                        hud.PhaseBanner(p.Army, p.Day, p.Income, ControllerName(p.Army));
                        break;
                    case ResuppliedEvent s:
                        lines.Add($"全補しました({s.UnitIds.Count}部隊、{s.Cost:N0})");
                        break;
                    case UnitLostEvent l when l.Reason != "撃破":
                        lines.Add("部隊が" + l.Reason + "しました");
                        break;
                    case GameOverEvent g:
                        mode = Mode.GameOver;
                        board.SetCursor(0, 0, false);
                        hud.HideActionMenu(); hud.HideProduce(); hud.Hint(null);
                        StartCoroutine(GameOverAfterBattle(g));
                        break;
                }
            }
            if (lines.Count > 0) hud.Toast(string.Join("\n", lines));
            return cutIn;
        }

        IEnumerator GameOverAfterBattle(GameOverEvent g)
        {
            yield return null;   // the cut-in for the final blow starts in the same frame
            while (hud.BattlePlaying) yield return null;
            hud.ShowGameOver(g.Winner, g.Reason);
        }

        // ---------------- online ----------------

        void ShowOnlineLobby()
        {
            LeaveMatch();
            online = false;
            pendingLobby = null;
            hud.ShowOnline(NetConfig.DefaultAddress, roomCode, FwClient.IsConnected ? "接続しています" : "", false);
        }

        void OnlineGo(string address, string action, string code)
        {
            if (action == "join" && (string.IsNullOrEmpty(code) || code.Trim().Length != 4)) { hud.OnlineStatus("4桁の部屋番号を入れてください", true); return; }
            FwNetworkManager.Create(data);
            pendingLobby = action; pendingCode = code?.Trim();
            hud.SetOnlineBusy(true);
            hud.OnlineStatus("接続しています…", false);
            if (FwClient.IsConnected) SendPending();
            else if (!FwClient.Connect(address)) { pendingLobby = null; hud.SetOnlineBusy(false); hud.OnlineStatus("アドレスの形式が正しくありません", true); }
            else StartCoroutine(ConnectTimeout());
        }

        IEnumerator ConnectTimeout()
        {
            yield return new WaitForSecondsRealtime(10);
            if (pendingLobby != null && !FwClient.IsConnected)
            {
                pendingLobby = null;
                FwClient.Disconnect();
                hud.SetOnlineBusy(false);
                hud.OnlineStatus("サーバーに接続できませんでした。アドレスを確かめてください", true);
            }
        }

        void SendPending()
        {
            if (pendingLobby == "create") FwClient.CreateRoom(settings.MapId);
            else if (pendingLobby == "join") FwClient.JoinRoom(pendingCode);
            pendingLobby = null;
        }

        void LeaveOnline()
        {
            if (FwClient.IsConnected && roomCode != null) FwClient.LeaveRoom();
            online = false; awaiting = false; replica = null;
        }

        void OnNetConnected() => SendPending();

        void OnNetError(string reason)
        {
            if (hud.OnlineLobbyVisible) { hud.SetOnlineBusy(false); hud.OnlineStatus("通信エラー: " + reason, true); }
        }

        void OnNetDisconnected()
        {
            if (online && InMatch)
            {
                var code = roomCode;
                LeaveMatch();
                online = false; awaiting = false;
                hud.ShowOnline(NetConfig.DefaultAddress, code, "サーバーとの接続が切れました。「部屋に入る」で続きから再開できます", true);
            }
            else if (hud.OnlineLobbyVisible)
            {
                hud.ShowOnline(null, null, pendingLobby != null ? "サーバーに接続できませんでした" : "切断されました", true);
                pendingLobby = null;
            }
        }

        void OnNetStatus(RoomStatusMsg m)
        {
            roomCode = m.code;
            myArmy = m.myArmy;
            if (!m.started)
            {
                hud.ShowOnlineWaiting(m.code, "相手を待っています。この番号を相手に伝えてください");
                return;
            }
            if (online && InMatch && !state.GameOver)
                hud.Hint(m.opponentPresent ? null : "相手の接続が切れました。戻ってくるのを待っています");
        }

        void OnNetLobbyError(string e)
        {
            if (online && InMatch) hud.Toast(e, true);
            else { hud.ShowOnline(null, null, e, true); }
        }

        void OnNetRejected(string e)
        {
            awaiting = false;
            if (onlineBot != null && botLast != null) onlineBot.Rejected(botLast);
            hud.Toast(e, true);
            Cancel();
            if (state != null) { board.Refresh(); RefreshHud(); }
        }

        /// <summary>The server sent the whole game (match start, rejoin or resync): rebuild and show it.</summary>
        void StartOnlineMatch(GameLogMsg m)
        {
            LeaveMatch();
            online = true; awaiting = false;
            myArmy = m.myArmy;
            ai[0] = ai[1] = null;
            onlineBot = onlineBotLevel > 0 ? new AiPlayer((Army)myArmy, AiProfile.ForLevel(onlineBotLevel), (uint)System.Environment.TickCount) : null;
            var map = MapDef.Parse(Text("Maps/" + m.mapId), data);
            replica = new OnlineReplica();
            replica.Rebuild(data, map, m.cmds ?? new string[0], m.seeds ?? new uint[0]);
            state = replica.State;
            BuildBoard();
            hud.ShowMatchChrome(true);
            mode = state.GameOver ? Mode.GameOver : Mode.Idle;
            hud.PhaseBanner(state.Active, state.Day, Income(state.Active), ControllerName(state.Active));
            hud.Toast("あなたは" + Labels.Army((Army)myArmy) + "です" + (replica.Applied > 0 ? "(続きから再開)" : ""));
            RefreshHud();
            if (state.GameOver) hud.ShowGameOver(state.Winner, "対戦は終了しています");
        }

        /// <summary>One accepted command from the server (ours or the opponent's).</summary>
        void ApplyRemote(AppliedMsg m)
        {
            if (!online || replica == null) return;
            var cmd = CommandCodec.Decode(m.cmd);
            var cast = cmd != null ? BattleCast(cmd) : null;
            var r = replica.Step(m.index, m.cmd, m.seed, m.hash);
            if (r == null) { FwClient.RequestResync(); return; }
            if (cmd != null && (int)cmd.Army == myArmy) awaiting = false;
            Report(r, cast);
            board.Refresh();
            RefreshHud();
        }

        void OnDestroy()
        {
            FwClient.Connected -= OnNetConnected;
            FwClient.Disconnected -= OnNetDisconnected;
            FwClient.Error -= OnNetError;
            FwClient.Status -= OnNetStatus;
            FwClient.Log -= StartOnlineMatch;
            FwClient.Applied -= ApplyRemote;
            FwClient.Rejected -= OnNetRejected;
            FwClient.LobbyError -= OnNetLobbyError;
        }

        static string Range(int a, int b) => a == b ? a.ToString() : a + "〜" + b;
    }
}
