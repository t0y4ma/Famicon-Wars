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
        RoomStatusMsg? room;                                   // last room status from the server

        // the record of the match being played (感想戦): every command with the seed it was played with
        readonly List<string> recCmds = new List<string>();
        readonly List<uint> recSeeds = new List<uint>();
        Rng recSeedRng = new Rng(1);
        string recMapId;
        bool recSaved;
        MatchRecord lastRecord;
        // reviewing a record
        bool replaying, replayAuto;
        MatchRecord replayRec;
        int replayIndex;
        float replayNext;
        string replayFrom;                                     // "gameover" or "records"
        readonly Queue<AppliedMsg> incoming = new Queue<AppliedMsg>();   // played one by one, after each animation
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
            hud.OnRestart += () => { if (online) BackToRoom(); else ShowSetup(); };
            hud.OnBackToTitle += () => { if (online || FwClient.IsConnected) { LeaveOnline(); FwClient.Disconnect(); } ShowTitle(); };

            hud.OnTitleOnline += ShowOnlineLobby;
            hud.OnOnlineBack += () => { FwClient.Disconnect(); ShowTitle(); };
            hud.OnOnlineCreate += addr => OnlineGo(addr, "create", null);
            hud.OnOnlineJoin += (addr, code) => OnlineGo(addr, "join", code);
            hud.OnGameOverReplay += () => { if (lastRecord != null) StartReplay(lastRecord, "gameover"); else hud.Toast("この対戦の記録がありません", true); };
            hud.OnTitleRecords += ShowRecordsScreen;
            hud.OnRecordsBack += ShowTitle;
            hud.OnRecordPick += i => { var list = MatchRecords.Load(); if (i >= 0 && i < list.Count) StartReplay(list[i], "records"); };
            hud.OnRecordDelete += i => { MatchRecords.Delete(i); ShowRecordsScreen(); };
            hud.OnReplayStep += d => { replayAuto = false; if (d > 0) ReplayForward(true); else ReplayGoto(replayIndex - 1); };
            hud.OnReplayPhase += d => { replayAuto = false; ReplayPhase(d); };
            hud.OnReplayEdge += d => { replayAuto = false; ReplayGoto(d < 0 ? 0 : replayRec.cmds.Length); };
            hud.OnReplayAuto += () => { replayAuto = !replayAuto; replayNext = 0; ReplayInfo(null); };
            hud.OnReplayExit += ExitReplay;
            hud.OnZoomReset += ResetCamera;
            hud.OnRoomSit += seat => FwClient.RoomAction("sit", seat);
            hud.OnRoomStand += () => FwClient.RoomAction("stand");
            hud.OnRoomBot += (seat, level) => FwClient.RoomAction("bot", seat, level.ToString());
            hud.OnRoomHost += id => FwClient.RoomAction("host", id);
            hud.OnRoomMap += StepRoomMap;
            hud.OnRoomStart += () => FwClient.RoomAction(room.HasValue && room.Value.started ? "reset" : "start");
            hud.OnRoomLeave += () => { if (FwClient.IsConnected) FwClient.LeaveRoom(); roomCode = null; room = null; ShowOnlineLobby(); };
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
            replaying = false; replayAuto = false;
            hud.ShowReplayBar(false);
            camKey = null;              // the next board starts with a fresh view
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
            recMapId = settings.MapId; recCmds.Clear(); recSeeds.Clear(); recSaved = false;
            recSeedRng = new Rng((uint)System.Environment.TickCount | 1u);
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

        // ---- camera: zoom with the wheel, scroll by holding the cursor at a screen edge ----
        // Zoom and scrolling are free as long as the board still covers at least a fifth of the screen
        // (it may hang off the screen); the reset button returns to the starting view.
        const float CamMinSize = 2.5f;       // closest zoom (about 5 rows on screen)
        const float MinTilePx = 46f;         // a big map starts zoomed in so units stay this big at least
        const float EdgePx = 14f;            // cursor this close to a screen edge scrolls
        const float MinBoardShare = 0.2f;    // the board must cover at least this much of the screen
        float camFit, camSize, camHome;
        Vector3 camGoal, camHomePos;
        string camKey;

        void SetupCamera()
        {
            cam = Camera.main;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.07f);
            cam.transform.rotation = Quaternion.identity;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            // the whole board (with room for the top bar, and for the control bar when reviewing)
            camFit = Mathf.Max(state.Height / 2f + 1.3f, (state.Width / 2f + 0.5f) / aspect) * (replaying ? 1.16f : 1f);
            string key = state.Width + "x" + state.Height + (replaying ? "r" : "");
            if (key == camKey && camSize > 0)
            {
                // same board rebuilt (e.g. jumping through a replay): keep the view
                cam.orthographicSize = camSize;
                cam.transform.position = camGoal;
                ShowZoom();
                return;
            }
            camKey = key;
            float readable = Screen.height / (2f * MinTilePx);
            camSize = Mathf.Clamp(Mathf.Min(camFit, readable), CamMinSize, Mathf.Max(CamMinSize, camFit));
            cam.orthographicSize = camSize;
            var center = new Vector3((state.Width - 1) / 2f, -(state.Height - 1) / 2f + camSize * (replaying ? -0.06f : 0.07f), -10);
            if (camSize < camFit - 0.01f)
            {
                // zoomed in: start at our own HQ
                Army mine = online ? (myArmy >= 0 ? (Army)myArmy : Army.Red) : (ai[0] == null || ai[1] != null ? Army.Red : Army.Blue);
                for (int i = 0; i < state.Owner.Length; i++)
                    if (state.Owner[i] == mine && state.TerrainAt(i % state.Width, i / state.Width).Id == "HQ")
                        center = new Vector3(i % state.Width, -(i / state.Width), -10);
            }
            camGoal = center;
            cam.transform.position = camGoal;
            camHome = camSize; camHomePos = camGoal;
            ShowZoom();
        }

        /// <summary>Furthest zoom-out: the board then covers MinBoardShare of the screen.</summary>
        float CamMaxSize => Mathf.Max(CamMinSize, Mathf.Sqrt(state.Width * state.Height / (4f * MinBoardShare * Mathf.Max(0.1f, cam.aspect))));

        /// <summary>Share of the screen the board covers with the camera at p and half-height h.</summary>
        float BoardShare(Vector3 p, float h)
        {
            float w = h * cam.aspect;
            float ox = Mathf.Max(0, Mathf.Min(p.x + w, state.Width - 0.5f) - Mathf.Max(p.x - w, -0.5f));
            float oy = Mathf.Max(0, Mathf.Min(p.y + h, 0.5f) - Mathf.Max(p.y - h, -(state.Height - 0.5f)));
            return ox * oy / (4f * w * h);
        }

        /// <summary>Pulls the view back toward the board until the board covers enough of the screen.</summary>
        Vector3 ClampCam(Vector3 p)
        {
            p.z = -10;
            if (cam == null || state == null) return p;
            float h = cam.orthographicSize;
            if (BoardShare(p, h) >= MinBoardShare) return p;
            var center = new Vector3((state.Width - 1) / 2f, -(state.Height - 1) / 2f, -10);
            if (BoardShare(center, h) < MinBoardShare) return center;
            float lo = 0, hi = 1;                  // fraction of the way from p to the centre
            for (int i = 0; i < 14; i++)
            {
                float mid = (lo + hi) / 2;
                if (BoardShare(Vector3.Lerp(p, center, mid), h) >= MinBoardShare) hi = mid; else lo = mid;
            }
            return Vector3.Lerp(p, center, hi);
        }

        void ShowZoom() => hud.SetZoom(Mathf.RoundToInt(camHome / Mathf.Max(0.01f, camSize) * 100f));

        void ResetCamera()
        {
            if (cam == null || state == null) return;
            camSize = camHome;
            cam.orthographicSize = camSize;
            camGoal = camHomePos;
            ShowZoom();
        }

        void UpdateCamera()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            float dt = Time.unscaledDeltaTime;
            Vector2 mp = mouse.position.ReadValue();
            bool inside = mp.x >= 0 && mp.y >= 0 && mp.x <= Screen.width && mp.y <= Screen.height;

            // wheel: zoom around the point under the cursor
            float wheel = mouse.scroll.ReadValue().y;
            if (wheel != 0 && inside && !hud.PointerOverUi && !hud.BattlePlaying)
            {
                var before = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, 10));
                camSize = Mathf.Clamp(camSize * (wheel > 0 ? 0.87f : 1.15f), CamMinSize, CamMaxSize);
                cam.orthographicSize = camSize;
                var after = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, 10));
                var shift = before - after; shift.z = 0;
                cam.transform.position = ClampCam(cam.transform.position + shift);
                camGoal = ClampCam(camGoal + shift);
                ShowZoom();
            }

            // cursor at a screen edge: scroll (faster when zoomed out)
            if (inside && Application.isFocused && !hud.BattlePlaying)
            {
                var dir = Vector2.zero;
                if (mp.x <= EdgePx) dir.x = -1; else if (mp.x >= Screen.width - EdgePx) dir.x = 1;
                if (mp.y <= EdgePx) dir.y = -1; else if (mp.y >= Screen.height - EdgePx) dir.y = 1;
                if (dir != Vector2.zero) camGoal = ClampCam(camGoal + (Vector3)(dir.normalized * camSize * 1.6f * dt));
            }
            cam.transform.position = Vector3.Lerp(cam.transform.position, camGoal, 1f - Mathf.Exp(-14f * dt));
        }

        /// <summary>Brings a tile into view if it is off screen (COM / opponent / replay moves).</summary>
        void FocusOn(int x, int y)
        {
            if (cam == null || state == null) return;
            float h = cam.orthographicSize - 1f, w = h * cam.aspect - 0.5f;
            var c = camGoal;
            if (Mathf.Abs(x - c.x) <= w && Mathf.Abs(-y - c.y) <= h) return;
            camGoal = ClampCam(new Vector3(x, -y, -10));
        }

        void FocusOn(Command c)
        {
            if (c is UnitCommand uc)
            {
                var u = state.UnitById(uc.UnitId);
                FocusOn(uc.ToX, uc.ToY);
                if (u != null) FocusOn(u.X, u.Y);
            }
            else if (c is ProduceCommand pc) FocusOn(pc.X, pc.Y);
        }

        bool InMatch => state != null && mode != Mode.Screens;
        bool ComTurn => InMatch && !replaying && !online && !state.GameOver && ai[(int)state.Active] != null;
        bool HumanTurn => InMatch && !replaying && !state.GameOver && (online ? myArmy >= 0 && (int)state.Active == myArmy && !awaiting && incoming.Count == 0 : ai[(int)state.Active] == null);

        string ControllerName(Army a) => replaying ? (a == Army.Red ? replayRec.red : replayRec.blue) : online ? ((int)a == myArmy ? "あなた" : room.HasValue && room.Value.seatNames != null && !string.IsNullOrEmpty(room.Value.seatNames[(int)a]) ? room.Value.seatNames[(int)a] : "相手")
            : ai[(int)a] == null ? "人間" : "COM " + ai[(int)a].Profile.Name;

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
            UpdateCamera();
            // moves from the server are shown one at a time, each after the previous animation
            if (online && incoming.Count > 0 && !hud.BattlePlaying && !hud.BannerShowing) ApplyIncoming(incoming.Dequeue());
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

            if (replaying)
            {
                if (replayAuto && !hud.BannerShowing && Time.unscaledTime >= replayNext)
                {
                    if (replayIndex >= replayRec.cmds.Length) { replayAuto = false; ReplayInfo(null); }
                    else { ReplayForward(true); replayNext = Time.unscaledTime + 0.35f; }
                }
                if (kb != null && kb.rightArrowKey.wasPressedThisFrame) { replayAuto = false; ReplayForward(true); }
                if (kb != null && kb.leftArrowKey.wasPressedThisFrame) { replayAuto = false; ReplayGoto(replayIndex - 1); }
                if (mouse != null)
                {
                    Vector2 rp = mouse.position.ReadValue();
                    var rw = cam.ScreenToWorldPoint(new Vector3(rp.x, rp.y, 10));
                    if (!hud.PointerOverUi) hud.ShowInfo(state, Mathf.RoundToInt(rw.x), Mathf.RoundToInt(-rw.y), null);
                }
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

                FocusOn(cmd);
                if (d > 0) yield return ShowIntent(cmd, d);

                var cast = BattleCast(cmd);
                var r = ApplyRecorded(cmd);
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
            }, state.Active);
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
            var r = ApplyRecorded(c);
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
                    case GameOverEvent g when replaying:
                        lines.Add((g.Winner == Army.None ? "引き分け" : Labels.Army(g.Winner) + "の勝ち") + "(" + g.Reason + ")");
                        break;
                    case GameOverEvent g:
                        SaveRecord(g.Winner, g.Reason);
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
            hud.ShowOnline(NetConfig.DefaultAddress, roomCode, FwClient.IsConnected ? "接続しています" : "", false, NetConfig.PlayerName);
        }

        void OnlineGo(string address, string action, string code)
        {
            if (action == "join" && (string.IsNullOrEmpty(code) || code.Trim().Length != 4)) { hud.OnlineStatus("4桁の部屋番号を入れてください", true); return; }
            FwNetworkManager.Create(data);
            if (!string.IsNullOrEmpty(hud.OnlinePlayerName)) NetConfig.PlayerName = hud.OnlinePlayerName;
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
            online = false; awaiting = false; replica = null; room = null;
            incoming.Clear();
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
            else if (hud.RoomVisible)
            {
                room = null;
                hud.ShowOnline(NetConfig.DefaultAddress, roomCode, "サーバーとの接続が切れました。「部屋に入る」で入り直せます", true);
            }
            else if (hud.OnlineLobbyVisible)
            {
                hud.ShowOnline(null, null, pendingLobby != null ? "サーバーに接続できませんでした" : "切断されました", true);
                pendingLobby = null;
            }
        }

        void OnNetStatus(RoomStatusMsg m)
        {
            room = m;
            roomCode = m.code;
            if (replaying) return;               // reviewing a match: the room screen comes back on exit
            myArmy = m.mySeat;
            if (!m.started)
            {
                // before the match, or the host reopened the room after one: the room screen
                if (online && InMatch) { LeaveMatch(); online = false; replica = null; incoming.Clear(); }
                hud.ShowRoom(ToView(m), Preview);
                return;
            }
            if (online && InMatch)
            {
                if (!state.GameOver && myArmy >= 0)
                    hud.Hint(m.seatOnline != null && !m.seatOnline[1 - myArmy] ? "相手の接続が切れました。戻ってくるのを待っています" : null);
                RefreshHud();
            }
            else if (hud.RoomVisible) hud.ShowRoom(ToView(m), Preview);
        }

        RoomView ToView(RoomStatusMsg m)
        {
            var v = new RoomView { Code = m.code, MapId = m.mapId, Started = m.started, GameOver = m.gameOver, IAmHost = m.myId == m.hostId, MySeat = m.mySeat };
            for (int s = 0; s < 2; s++)
            {
                v.SeatKinds[s] = m.seatKinds != null ? (SeatKind)m.seatKinds[s] : SeatKind.Empty;
                v.SeatNames[s] = m.seatNames != null ? m.seatNames[s] : "";
                v.SeatLevels[s] = m.seatLevels != null ? m.seatLevels[s] : 0;
                v.SeatOnline[s] = m.seatOnline != null && m.seatOnline[s];
            }
            for (int i = 0; m.memberIds != null && i < m.memberIds.Length; i++)
                v.Members.Add((m.memberIds[i], m.memberNames[i], m.memberSeats[i], m.memberOnline[i], m.memberIds[i] == m.hostId, m.memberIds[i] == m.myId));
            return v;
        }

        /// <summary>Host only: the previous / next playable map.</summary>
        void StepRoomMap(int dir)
        {
            if (!room.HasValue) return;
            var ready = new List<MapEntry>();
            foreach (var e in MapCatalog.All) if (e.Ready) ready.Add(e);
            int i = ready.FindIndex(e => e.Id == room.Value.mapId);
            if (ready.Count == 0) return;
            i = ((i < 0 ? 0 : i) + dir + ready.Count) % ready.Count;
            FwClient.RoomAction("map", 0, ready[i].Id);
        }

        /// <summary>From the game-over panel of an online match back to the room screen.</summary>
        void BackToRoom()
        {
            LeaveMatch();
            online = false; awaiting = false; replica = null; incoming.Clear();
            if (room.HasValue) hud.ShowRoom(ToView(room.Value), Preview);
            else ShowOnlineLobby();
        }

        void OnNetLobbyError(string e)
        {
            if (hud.RoomVisible) hud.RoomError(e);
            else if (online && InMatch) hud.Toast(e, true);
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
            incoming.Clear();
            myArmy = m.myArmy;
            ai[0] = ai[1] = null;
            onlineBot = onlineBotLevel > 0 && myArmy >= 0 ? new AiPlayer((Army)myArmy, AiProfile.ForLevel(onlineBotLevel), (uint)System.Environment.TickCount) : null;
            var map = MapDef.Parse(Text("Maps/" + m.mapId), data);
            replica = new OnlineReplica();
            replica.Rebuild(data, map, m.cmds ?? new string[0], m.seeds ?? new uint[0]);
            state = replica.State;
            recMapId = m.mapId; recSaved = false;
            BuildBoard();
            hud.ShowMatchChrome(true);
            mode = state.GameOver ? Mode.GameOver : Mode.Idle;
            if (state.GameOver) SaveRecord(state.Winner, "対戦は終了しています");
            hud.PhaseBanner(state.Active, state.Day, Income(state.Active), ControllerName(state.Active));
            hud.Toast(myArmy < 0 ? "観戦しています" : "あなたは" + Labels.Army((Army)myArmy) + "です" + (replica.Applied > 0 ? "(続きから再開)" : ""));
            if (myArmy < 0) hud.Hint("観戦中");
            RefreshHud();
            if (state.GameOver) hud.ShowGameOver(state.Winner, "対戦は終了しています");
        }

        /// <summary>One accepted command from the server (ours, the opponent's or a BOT's): queued for display.</summary>
        void ApplyRemote(AppliedMsg m)
        {
            if (!online || replica == null) return;
            incoming.Enqueue(m);
        }

        void ApplyIncoming(AppliedMsg m)
        {
            if (!online || replica == null) return;
            var cmd = CommandCodec.Decode(m.cmd);
            if (cmd != null && (int)cmd.Army != myArmy) FocusOn(cmd);
            var cast = cmd != null ? BattleCast(cmd) : null;
            var r = replica.Step(m.index, m.cmd, m.seed, m.hash);
            if (r == null) { incoming.Clear(); FwClient.RequestResync(); return; }
            if (cmd != null && (int)cmd.Army == myArmy) awaiting = false;
            Report(r, cast);
            board.Refresh();
            RefreshHud();
        }

        // ---------------- match records and review (感想戦) ----------------

        /// <summary>Offline: every command is played with a fresh seed that is recorded with it.</summary>
        ApplyResult ApplyRecorded(Command c)
        {
            uint sd = recSeedRng.Next() | 1u;
            state.Rng = new Rng(sd);
            var r = RulesEngine.Apply(state, c);
            if (r.Ok) { recCmds.Add(CommandCodec.Encode(c)); recSeeds.Add(sd); }
            return r;
        }

        string RecordName(Army a)
        {
            if (online) return room.HasValue && room.Value.seatNames != null && !string.IsNullOrEmpty(room.Value.seatNames[(int)a]) ? room.Value.seatNames[(int)a] : Labels.Army(a);
            return ai[(int)a] == null ? "人間" : "COM " + ai[(int)a].Profile.Name;
        }

        void SaveRecord(Army winner, string reason)
        {
            if (recSaved || replaying) return;
            List<string> cmds = online && replica != null ? replica.Cmds : recCmds;
            List<uint> seeds = online && replica != null ? replica.Seeds : recSeeds;
            if (cmds.Count == 0 || string.IsNullOrEmpty(recMapId)) return;
            var rec = new MatchRecord
            {
                mapId = recMapId, date = System.DateTime.Now.ToString("yyyy/MM/dd HH:mm"),
                red = RecordName(Army.Red), blue = RecordName(Army.Blue), online = online,
                result = (winner == Army.None ? "引き分け" : Labels.Army(winner) + "の勝ち") + "(" + reason + ")",
                cmds = cmds.ToArray(), seeds = seeds.Select(x => unchecked((int)x)).ToArray()
            };
            MatchRecords.Add(rec);
            lastRecord = rec;
            recSaved = true;
        }

        void ShowRecordsScreen()
        {
            LeaveMatch();
            var items = new List<(string, string)>();
            foreach (var r in MatchRecords.Load())
            {
                var e = MapCatalog.Find(r.mapId);
                items.Add((r.date + "　" + (e != null ? e.Name : r.mapId) + "　" + r.red + " 対 " + r.blue,
                           r.result + "　" + r.cmds.Length + "手" + (r.online ? "　オンライン" : "")));
            }
            hud.ShowRecords(items);
        }

        void StartReplay(MatchRecord rec, string from)
        {
            if (MapCatalog.Find(rec.mapId) == null) { hud.Toast("このマップはもうありません", true); return; }
            LeaveMatch();
            replaying = true; replayAuto = false; replayRec = rec; replayFrom = from;
            hud.ShowMatchChrome(true);
            hud.ShowReplayBar(true);
            ReplayGoto(0);
            mode = Mode.GameOver;                 // nothing on the board can be ordered
            hud.Hint(null);
        }

        /// <summary>Shows the position after the first n commands (rebuilt from the start, no animation).</summary>
        void ReplayGoto(int n)
        {
            n = Mathf.Clamp(n, 0, replayRec.cmds.Length);
            var map = MapDef.Parse(Text("Maps/" + replayRec.mapId), data);
            state = GameState.Create(data, map, 1);
            RulesEngine.StartGame(state);
            for (int i = 0; i < n; i++)
            {
                var c = CommandCodec.Decode(replayRec.cmds[i]);
                if (c == null) break;
                state.Rng = new Rng(replayRec.Seed(i));
                RulesEngine.Apply(state, c);
            }
            replayIndex = n;
            hud.SkipBattle();
            BuildBoard();
            RefreshHud();
            ReplayInfo(n > 0 ? "" : replayRec.red + " 対 " + replayRec.blue + "　" + replayRec.result);
        }

        /// <summary>Plays the next command (with its battle scene when animate).</summary>
        void ReplayForward(bool animate)
        {
            if (replayIndex >= replayRec.cmds.Length) return;
            var c = CommandCodec.Decode(replayRec.cmds[replayIndex]);
            if (c == null) { replayIndex = replayRec.cmds.Length; return; }
            string what = Describe(c);
            if (animate) FocusOn(c);
            var cast = animate ? BattleCast(c) : null;
            state.Rng = new Rng(replayRec.Seed(replayIndex));
            var r = RulesEngine.Apply(state, c);
            replayIndex++;
            if (animate && r.Ok) Report(r, cast);
            if (animate) { board.Refresh(); RefreshHud(); ReplayInfo(what); }
        }

        /// <summary>Jumps to the start of the next / previous phase.</summary>
        void ReplayPhase(int dir)
        {
            var cmds = replayRec.cmds;
            if (dir > 0)
            {
                while (replayIndex < cmds.Length)
                {
                    bool end = cmds[replayIndex].StartsWith("E,");
                    ReplayForward(false);
                    if (end) break;
                }
                hud.SkipBattle();
                board.Refresh(); RefreshHud();
                ReplayInfo("");
                return;
            }
            // the phase we are in starts after the last end-of-phase before us; if we are at its start, go one further back
            int start = LastPhaseStart(replayIndex);
            if (start == replayIndex) start = LastPhaseStart(Mathf.Max(0, replayIndex - 1));
            ReplayGoto(start);
        }

        int LastPhaseStart(int index)
        {
            for (int j = index - 1; j >= 0; j--) if (replayRec.cmds[j].StartsWith("E,")) return j + 1;
            return 0;
        }

        void ReplayInfo(string move)
        {
            if (!replaying) return;
            string head = replayIndex + " / " + replayRec.cmds.Length + " 手　" + state.Day + "日目　" + Labels.Army(state.Active) + "の手番";
            if (move != null) replayMoveText = move;
            hud.SetReplayInfo(head, replayMoveText, replayAuto);
        }
        string replayMoveText = "";

        /// <summary>One line for a command, read against the position before it is played.</summary>
        string Describe(Command c)
        {
            string side = Labels.Army(c.Army) + ": ";
            switch (c)
            {
                case UnitCommand u:
                {
                    var unit = state.UnitById(u.UnitId);
                    string name = unit != null ? Labels.Get(state.Def(unit).NameKey) : "部隊";
                    string act;
                    switch (u.Action)
                    {
                        case UnitAction.Attack:
                            var t = state.UnitById(u.TargetId);
                            act = "攻撃" + (t != null ? "(" + Labels.Get(state.Def(t).NameKey) + ")" : ""); break;
                        case UnitAction.Capture: act = "占領"; break;
                        case UnitAction.Load: act = "搭載"; break;
                        case UnitAction.Join: act = "合流"; break;
                        case UnitAction.Unload: act = "降車"; break;
                        case UnitAction.Supply: act = "補給"; break;
                        default: act = unit != null && (unit.X != u.ToX || unit.Y != u.ToY) ? "移動" : "待機"; break;
                    }
                    return side + name + " " + act;
                }
                case ProduceCommand p: return side + Labels.Get(data.Unit(p.UnitType).NameKey) + "を生産";
                case EndPhaseCommand _: return side + "手番終了";
                case ResupplyAllCommand _: return side + "全補";
                case SurrenderCommand _: return side + "降伏";
            }
            return side;
        }

        void ExitReplay()
        {
            string from = replayFrom;
            LeaveMatch();
            if (from == "records") ShowRecordsScreen();
            else if (room.HasValue && FwClient.IsConnected && roomCode != null) { online = false; hud.ShowRoom(ToView(room.Value), Preview); }
            else ShowTitle();
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
