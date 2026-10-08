using System;
using System.Collections.Generic;

namespace FamiconWars.Core
{
    // ------------------------------------------------------------------------------------------
    // Online play, transport-independent. The server owns the only real GameState per room and
    // validates every command with RulesEngine. Clients keep a replica and stay in sync by
    // re-applying each accepted command with the random seed the server drew for it (a fresh seed
    // per command, revealed only when it is used, so nobody can predict combat rolls).
    // Traffic is one short text line per action; a joining/returning member gets the whole log.
    //
    // A room has two seats (red, blue) and any number of spectators. Before the match the host
    // picks the map, puts BOTs (server-side COM) in empty seats, can hand the host role to someone
    // else, and starts the match once both seats are filled. Spectators watch the match live.
    // After a match the room can be rearranged right away (seats, map, BOTs) and the next match
    // started; the finished game stays available until then. Public rooms are listed in the lobby.
    //
    // BOT seats are played by the host's client, not the server (to spare the server's CPU): the
    // BOT is a deterministic AiPlayer.ForOnline with a seed from the server, so every client can work
    // out the same move. The host sends it; the other clients check each BOT move against their own
    // copy and report a mismatch. On a mismatch, a refused BOT move or a host that stops sending,
    // the server takes the BOTs over for the rest of the match.
    // The animation speed is the host's, so everyone sees the match at the same pace.
    // ------------------------------------------------------------------------------------------

    public enum SeatKind { Empty = 0, Human = 1, Bot = 2 }

    public abstract class ServerMsg { }

    /// <summary>Room state as seen by one member. MySeat: 0 red, 1 blue, -1 spectator.</summary>
    public sealed class RoomStatus : ServerMsg
    {
        public string Code, MapId;
        public bool Started, GameOver, Public;
        public int MyId, MySeat, HostId;
        public int Speed;                            // the host's game speed (index into the speed list)
        public bool BotsOnServer;                    // the server plays the BOT seats (else the host does)
        public SeatKind[] SeatKinds = new SeatKind[2];
        public string[] SeatNames = new string[2];
        public int[] SeatLevels = new int[2];        // BOT strength 1-4
        public int[] SeatStyles = new int[2];        // BOT 戦法 chosen by the host: 0 random, 1-3 AiStyle
        public bool[] SeatOnline = new bool[2];
        public int[] MemberIds; public string[] MemberNames; public int[] MemberSeats; public bool[] MemberOnline;
    }
    /// <summary>Everything needed to rebuild the game: map + every accepted command with its seed. MyArmy -1 = spectator.</summary>
    public sealed class GameLog : ServerMsg
    {
        public string MapId; public int MyArmy; public string[] Cmds; public uint[] Seeds;
        public uint[] BotSeeds = new uint[2];        // AiPlayer.ForOnline seed per BOT seat
        public int[] BotLevels = new int[2];         // 0 = not a BOT
        public int[] BotStyles = new int[2];         // 0 = random (from the seed), 1-3 = AiStyle chosen by the host
    }
    public sealed class Applied : ServerMsg { public int Index; public string Cmd; public uint Seed; public int Hash; }
    public sealed class Rejected : ServerMsg { public string Error; }
    public sealed class LobbyError : ServerMsg { public string Error; }
    public sealed class LeftRoom : ServerMsg { }
    /// <summary>The public rooms for the lobby. State: 0 waiting, 1 playing, 2 match over.</summary>
    public sealed class RoomList : ServerMsg
    {
        public string[] Codes, MapIds, HostNames;
        public int[] States, Members, SeatsTaken;
    }

    public sealed class RoomMember
    {
        public int Id, Conn = -1;     // Conn -1 = away (kept during a match so the player can come back)
        public string Token, Name;
        public int Seat = -1;
        public bool Online => Conn >= 0;
    }

    public sealed class OnlineRoom
    {
        public string Code, MapId;
        /// <summary>Map of the match in State (MapId may already be changed for the next one).</summary>
        public string PlayedMapId;
        /// <summary>Listed in the lobby (private rooms are reached by their number only).</summary>
        public bool Public;
        public GameState State;
        public readonly List<string> Cmds = new List<string>();
        public readonly List<uint> Seeds = new List<uint>();
        public readonly List<RoomMember> Members = new List<RoomMember>();
        public readonly SeatKind[] Seats = new SeatKind[2];
        public readonly int[] BotLevels = new int[2];
        public readonly int[] BotStyles = new int[2];         // 0 random, 1-3 AiStyle
        public readonly AiPlayer[] Bots = new AiPlayer[2];
        public int HostId = -1, NextMemberId = 1;
        public double BotNextTime;
        public int Speed = 1;
        /// <summary>The match's BOT seats: seeds for AiPlayer.ForOnline and strengths (fixed at the start).</summary>
        public readonly uint[] BotSeeds = new uint[2];
        public readonly int[] MatchBotLevels = new int[2];
        public readonly int[] MatchBotStyles = new int[2];
        public bool BotsOnServer;
        /// <summary>Last time a command was applied (a host that stops sending BOT moves is replaced).</summary>
        public double LastActivity;
        public bool Started => State != null;
        public bool GameOver => State != null && State.GameOver;
        /// <summary>Seats, map and BOTs can change: before a match or once it is over.</summary>
        public bool Editable => State == null || State.GameOver;
        public RoomMember ByConn(int conn) => Members.Find(m => m.Conn == conn);
        public RoomMember ById(int id) => Members.Find(m => m.Id == id);
        public RoomMember InSeat(int seat) => Members.Find(m => m.Seat == seat);
        public bool AnyoneOnline => Members.Exists(m => m.Online);
    }

    public sealed class OnlineRoomServer
    {
        public const int MaxMembers = 16;
        public const int SpeedCount = 4;
        /// <summary>Seconds without a BOT move from the host before the server plays the BOTs itself.</summary>
        public double BotTimeout = 20;
        double now;
        /// <summary>Seconds between two BOT commands in one room (spares the server; clients animate anyway).</summary>
        public double BotInterval = 0.25;

        readonly GameData data;
        readonly Func<string, MapDef> loadMap;      // map id -> map (null if unknown)
        readonly Action<int, ServerMsg> send;       // connection id, message
        readonly Func<uint> nextSeed;
        readonly Dictionary<string, OnlineRoom> rooms = new Dictionary<string, OnlineRoom>();
        readonly Dictionary<int, OnlineRoom> byConn = new Dictionary<int, OnlineRoom>();
        readonly Random codes;

        public OnlineRoomServer(GameData data, Func<string, MapDef> loadMap, Action<int, ServerMsg> send, Func<uint> nextSeed, int codeSeed)
        {
            this.data = data; this.loadMap = loadMap; this.send = send; this.nextSeed = nextSeed;
            codes = new Random(codeSeed);
        }

        public int RoomCount => rooms.Count;
        public OnlineRoom Find(string code) => code != null && rooms.TryGetValue(code, out var r) ? r : null;

        static string CleanName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 12) name = name.Substring(0, 12);
            return name.Length == 0 ? "名無し" : name;
        }

        static bool BadToken(string token) => string.IsNullOrEmpty(token) || token.Length > 64;

        // ---------------- lobby ----------------

        public const int MaxListed = 30;

        /// <summary>Lobby: the public rooms, waiting ones first.</summary>
        public void List(int conn)
        {
            var list = new List<OnlineRoom>();
            foreach (var r in rooms.Values) if (r.Public) list.Add(r);
            list.Sort((a, b) =>
            {
                int sa = a.Editable ? 0 : 1, sb = b.Editable ? 0 : 1;
                return sa != sb ? sa - sb : string.CompareOrdinal(a.Code, b.Code);
            });
            if (list.Count > MaxListed) list.RemoveRange(MaxListed, list.Count - MaxListed);
            int n = list.Count;
            var msg = new RoomList { Codes = new string[n], MapIds = new string[n], HostNames = new string[n], States = new int[n], Members = new int[n], SeatsTaken = new int[n] };
            for (int i = 0; i < n; i++)
            {
                var r = list[i];
                msg.Codes[i] = r.Code; msg.MapIds[i] = r.MapId;
                msg.HostNames[i] = r.ById(r.HostId)?.Name ?? "";
                msg.States[i] = !r.Started ? 0 : r.GameOver ? 2 : 1;
                msg.Members[i] = r.Members.FindAll(m => m.Online).Count;
                msg.SeatsTaken[i] = (r.Seats[0] != SeatKind.Empty ? 1 : 0) + (r.Seats[1] != SeatKind.Empty ? 1 : 0);
            }
            send(conn, msg);
        }

        public void Create(int conn, string token, string name, string mapId, bool isPublic = false)
        {
            if (BadToken(token)) { send(conn, new LobbyError { Error = "接続情報が不正です" }); return; }
            Leave(conn, false);
            if (loadMap(mapId) == null) mapId = "map01";
            string code;
            do code = codes.Next(1000, 10000).ToString(); while (rooms.ContainsKey(code));
            var room = new OnlineRoom { Code = code, MapId = mapId, Public = isPublic };
            var m = new RoomMember { Id = room.NextMemberId++, Conn = conn, Token = token, Name = CleanName(name), Seat = 0 };
            room.Members.Add(m);
            room.Seats[0] = SeatKind.Human;
            room.HostId = m.Id;
            rooms[code] = room; byConn[conn] = room;
            SendStatus(room);
        }

        public void Join(int conn, string code, string token, string name)
        {
            if (BadToken(token)) { send(conn, new LobbyError { Error = "接続情報が不正です" }); return; }
            var room = Find(code?.Trim());
            if (room == null) { send(conn, new LobbyError { Error = "部屋が見つかりません" }); return; }
            if (room.ByConn(conn) != null) { SendStatus(room); return; }
            Leave(conn, false);

            // a returning member gets their place back (same browser token), even mid-game
            var back = room.Members.Find(x => x.Token == token);
            if (back != null)
            {
                if (back.Online) byConn.Remove(back.Conn);
                back.Conn = conn; back.Name = string.IsNullOrWhiteSpace(name) ? back.Name : CleanName(name);
                byConn[conn] = room;
                if (room.HostId < 0 || room.ById(room.HostId) == null || !room.ById(room.HostId).Online) room.HostId = back.Id;
                SendStatus(room);
                if (room.Started) send(conn, Log(room, back.Seat));
                return;
            }

            if (room.Members.Count >= MaxMembers) { send(conn, new LobbyError { Error = "この部屋は満員です" }); return; }
            var m = new RoomMember { Id = room.NextMemberId++, Conn = conn, Token = token, Name = CleanName(name), Seat = -1 };
            room.Members.Add(m); byConn[conn] = room;
            // between matches a newcomer takes the free seat if there is one; otherwise (and mid-game) they watch
            if (room.Editable)
                for (int s = 0; s < 2; s++)
                    if (room.Seats[s] == SeatKind.Empty) { room.Seats[s] = SeatKind.Human; m.Seat = s; break; }
            SendStatus(room);
            if (room.Started && !room.GameOver) send(conn, Log(room, -1));
        }

        /// <summary>
        /// Lobby actions: "sit" (arg seat), "stand", "bot" (arg seat, level 0-4; host), "botstyle" (arg seat, text 0-3:
        /// random or a 戦法; host), "map" (text map id; host),
        /// "host" (arg member id; host), "public" (arg 1/0; host), "speed" (arg speed index; host),
        /// "start" (host; also right after a match), "abort" (host: ends the current match as a draw),
        /// "reset" (host, after the match: drop the finished game).
        /// </summary>
        public void Action(int conn, string action, int arg, string text)
        {
            if (!byConn.TryGetValue(conn, out var room)) { send(conn, new LobbyError { Error = "部屋に入っていません" }); return; }
            var me = room.ByConn(conn);
            bool host = me.Id == room.HostId;
            string err = null;
            switch (action)
            {
                case "sit":
                    if (!room.Editable) err = "対戦中は席を移れません";
                    else if (arg < 0 || arg > 1) err = "席がありません";
                    else if (me.Seat == arg) { }
                    else if (room.Seats[arg] != SeatKind.Empty) err = "その席は埋まっています";
                    else
                    {
                        if (me.Seat >= 0) room.Seats[me.Seat] = SeatKind.Empty;
                        me.Seat = arg; room.Seats[arg] = SeatKind.Human;
                    }
                    break;
                case "stand":
                    if (!room.Editable) err = "対戦中は席を立てません";
                    else if (me.Seat >= 0) { room.Seats[me.Seat] = SeatKind.Empty; me.Seat = -1; }
                    break;
                case "bot":
                    if (!host) err = "部屋主だけが BOT を置けます";
                    else if (!room.Editable) err = "対戦中は変更できません";
                    else if (arg < 0 || arg > 1) err = "席がありません";
                    else if (room.Seats[arg] == SeatKind.Human) err = "その席には人が座っています";
                    else
                    {
                        int level = Math.Max(0, Math.Min(4, text != null && int.TryParse(text, out var lv) ? lv : 0));
                        room.Seats[arg] = level > 0 ? SeatKind.Bot : SeatKind.Empty;
                        room.BotLevels[arg] = level;
                    }
                    break;
                case "botstyle":
                    if (!host) err = "部屋主だけが BOT の戦法を選べます";
                    else if (!room.Editable) err = "対戦中は変更できません";
                    else if (arg < 0 || arg > 1) err = "席がありません";
                    else room.BotStyles[arg] = Math.Max(0, Math.Min(3, text != null && int.TryParse(text, out var st) ? st : 0));
                    break;
                case "map":
                    if (!host) err = "部屋主だけがマップを選べます";
                    else if (!room.Editable) err = "対戦中は変更できません";
                    else if (loadMap(text) == null) err = "そのマップは使えません";
                    else room.MapId = text;
                    break;
                case "host":
                {
                    var to = room.ById(arg);
                    if (!host) err = "部屋主ではありません";
                    else if (to == null || !to.Online) err = "その人は部屋にいません";
                    else room.HostId = to.Id;
                    break;
                }
                case "speed":
                    if (!host) err = "部屋主だけが速度を変えられます";
                    else room.Speed = Math.Max(0, Math.Min(SpeedCount - 1, arg));
                    break;
                case "abort":
                    if (!host) err = "部屋主だけが対戦を終わらせられます";
                    else if (!room.Started || room.GameOver) err = "対戦中ではありません";
                    else { Apply(room, new AbortCommand { Army = Army.Red }, -1); return; }
                    break;
                case "public":
                    if (!host) err = "部屋主だけが公開範囲を変えられます";
                    else room.Public = arg != 0;
                    break;
                case "start":
                    if (!host) err = "部屋主だけが開始できます";
                    else if (!room.Editable) err = "もう始まっています";
                    else if (room.Seats[0] == SeatKind.Empty || room.Seats[1] == SeatKind.Empty) err = "両方の席が埋まると開始できます";
                    else { if (room.Started) DropFinished(room); StartMatch(room); }
                    break;
                case "reset":
                    if (!host) err = "部屋主だけが部屋に戻せます";
                    else if (!room.Started) { }
                    else if (!room.GameOver) err = "対戦中です";
                    else ResetRoom(room);
                    break;
                default: err = "不明な操作です"; break;
            }
            if (err != null) { send(conn, new LobbyError { Error = err }); return; }
            if (action != "start") SendStatus(room);
        }

        void StartMatch(OnlineRoom room)
        {
            room.PlayedMapId = room.MapId;
            room.State = GameState.Create(data, loadMap(room.MapId), nextSeed());
            RulesEngine.StartGame(room.State);
            room.Cmds.Clear(); room.Seeds.Clear();
            for (int s = 0; s < 2; s++)
            {
                bool bot = room.Seats[s] == SeatKind.Bot;
                room.MatchBotLevels[s] = bot ? Math.Max(1, room.BotLevels[s]) : 0;
                room.MatchBotStyles[s] = bot ? room.BotStyles[s] : 0;
                room.BotSeeds[s] = bot ? nextSeed() : 0;
                room.Bots[s] = null;
            }
            // BOT vs BOT: a random 戦法 never comes out the same as the other side's (the same 戦法 on both
            // sides tends to grind on); 戦法 the host chose are kept as they are
            if (room.MatchBotLevels[0] > 0 && room.MatchBotLevels[1] > 0)
            {
                if (room.MatchBotStyles[1] == 0)
                    room.BotSeeds[1] = AiPlayer.SeedForStyleOtherThan(room.BotSeeds[1], AiPlayer.BotStyle(room.MatchBotStyles[0], room.BotSeeds[0]));
                else if (room.MatchBotStyles[0] == 0)
                    room.BotSeeds[0] = AiPlayer.SeedForStyleOtherThan(room.BotSeeds[0], (AiStyle)room.MatchBotStyles[1]);
            }
            room.BotsOnServer = false;
            room.BotNextTime = 0;
            room.LastActivity = now;
            SendStatus(room);
            foreach (var m in room.Members) if (m.Online) send(m.Conn, Log(room, m.Seat));
        }

        void ResetRoom(OnlineRoom room)
        {
            DropFinished(room);
            SendStatus(room);
        }

        /// <summary>Forgets the finished game (members who are gone already lost their seat when it ended).</summary>
        void DropFinished(OnlineRoom room)
        {
            room.State = null;
            room.Cmds.Clear(); room.Seeds.Clear();
            room.Bots[0] = room.Bots[1] = null;
            room.Members.RemoveAll(m => !m.Online && Unseat(room, m));
        }

        static bool Unseat(OnlineRoom room, RoomMember m)
        {
            if (m.Seat >= 0) room.Seats[m.Seat] = SeatKind.Empty;
            m.Seat = -1;
            return true;
        }

        // ---------------- match ----------------

        public void Play(int conn, string text)
        {
            if (!byConn.TryGetValue(conn, out var room) || !room.Started) { send(conn, new Rejected { Error = "対戦中ではありません" }); return; }
            var me = room.ByConn(conn);
            if (me.Seat < 0) { send(conn, new Rejected { Error = "観戦中は操作できません" }); return; }
            var cmd = CommandCodec.Decode(text);
            if (cmd == null || cmd is AbortCommand) { send(conn, new Rejected { Error = "不正なコマンドです" }); return; }
            if ((int)cmd.Army != me.Seat) { send(conn, new Rejected { Error = "手番ではありません" }); return; }
            Apply(room, cmd, conn);
        }

        /// <summary>
        /// The host's client sends the move of a BOT seat (worked out with AiPlayer.ForOnline). Anything
        /// else than a legal move of the BOT whose turn it is makes the server take the BOTs over.
        /// </summary>
        public void BotPlay(int conn, string text)
        {
            if (!byConn.TryGetValue(conn, out var room) || !room.Started || room.GameOver || room.BotsOnServer) return;
            var me = room.ByConn(conn);
            if (me == null || me.Id != room.HostId) return;                 // only the current host (a late message from a former one is dropped)
            int seat = (int)room.State.Active;
            if (room.MatchBotLevels[seat] == 0) return;
            var cmd = CommandCodec.Decode(text);
            if (cmd == null || cmd is AbortCommand || cmd is SurrenderCommand || (int)cmd.Army != seat) { TakeOverBots(room); return; }
            if (!Apply(room, cmd, -1)) TakeOverBots(room);
        }

        /// <summary>A client's own copy of the BOT worked out a different move than the one played (index into the log).</summary>
        public void BotMismatch(int conn, int index)
        {
            if (!byConn.TryGetValue(conn, out var room) || !room.Started || room.GameOver || room.BotsOnServer) return;
            if (index < 0 || index >= room.Cmds.Count) return;
            var c = CommandCodec.Decode(room.Cmds[index]);
            if (c == null || room.MatchBotLevels[(int)c.Army] == 0) return;
            TakeOverBots(room);
        }

        /// <summary>From now on the server plays the BOT seats of this match.</summary>
        void TakeOverBots(OnlineRoom room)
        {
            if (room.BotsOnServer) return;
            room.BotsOnServer = true;
            for (int s = 0; s < 2; s++)
                // same seed as the host played with: the BOT keeps its 戦法 (and phase-seeded choices)
                room.Bots[s] = room.MatchBotLevels[s] > 0 ? AiPlayer.ForBot((Army)s, room.MatchBotLevels[s], room.MatchBotStyles[s], room.BotSeeds[s]) : null;
            room.BotNextTime = 0;
            SendStatus(room);
        }

        /// <returns>true when accepted.</returns>
        bool Apply(OnlineRoom room, Command cmd, int conn)
        {
            uint seed = nextSeed();
            room.State.Rng = new Rng(seed);
            var r = RulesEngine.Apply(room.State, cmd);
            if (!r.Ok)
            {
                if (conn >= 0) send(conn, new Rejected { Error = r.Error });
                return false;
            }
            var text = CommandCodec.Encode(cmd);
            room.Cmds.Add(text); room.Seeds.Add(seed);
            room.LastActivity = now;
            var msg = new Applied { Index = room.Cmds.Count - 1, Cmd = text, Seed = seed, Hash = CommandCodec.Hash(room.State) };
            foreach (var m in room.Members) if (m.Online) send(m.Conn, msg);
            if (room.State.GameOver)
            {
                // the room can be rearranged now: members who are gone give up their seat
                room.Members.RemoveAll(m => !m.Online && Unseat(room, m));
                SendStatus(room);
            }
            return true;
        }

        /// <summary>Call regularly (every frame is fine): BOTs make one move per room at a time.</summary>
        public void Tick(double now)
        {
            this.now = now;
            foreach (var room in rooms.Values)
            {
                if (!room.Started || room.GameOver) continue;
                int seat = (int)room.State.Active;
                if (room.MatchBotLevels[seat] == 0) continue;
                if (!room.BotsOnServer)
                {
                    // the host plays the BOT; one that stops sending (closed tab, hidden window) is replaced
                    if (now - room.LastActivity > BotTimeout) TakeOverBots(room);
                    continue;
                }
                if (now < room.BotNextTime) continue;
                var bot = room.Bots[seat];
                if (bot == null) continue;
                room.BotNextTime = now + BotInterval;
                var cmd = bot.Next(room.State);
                if (cmd == null) continue;
                if (!Apply(room, cmd, -1)) bot.Rejected(cmd);
            }
        }

        public void Resync(int conn)
        {
            if (!byConn.TryGetValue(conn, out var room) || !room.Started) return;
            send(conn, Log(room, room.ByConn(conn).Seat));
        }

        /// <summary>
        /// Leaving on purpose during a match is a surrender for a seated player. A disconnect (closed
        /// tab, lost network) during a match keeps the member and seat so they can come back.
        /// </summary>
        public void Leave(int conn, bool notify)
        {
            if (!byConn.TryGetValue(conn, out var room)) return;
            var m = room.ByConn(conn);
            if (m.Seat >= 0 && room.Started && !room.GameOver)
                Apply(room, new SurrenderCommand { Army = (Army)m.Seat }, -1);
            byConn.Remove(conn);
            Unseat(room, m);
            room.Members.Remove(m);
            AfterLeave(room, m);
            if (notify) send(conn, new LeftRoom());
        }

        public void Disconnected(int conn)
        {
            if (!byConn.TryGetValue(conn, out var room)) return;
            var m = room.ByConn(conn);
            byConn.Remove(conn);
            if (room.Started && !room.GameOver) m.Conn = -1;     // keep the place for a return
            else { Unseat(room, m); room.Members.Remove(m); }
            AfterLeave(room, m);
        }

        void AfterLeave(OnlineRoom room, RoomMember gone)
        {
            if (!room.AnyoneOnline) { rooms.Remove(room.Code); return; }
            if (room.HostId == gone.Id)
            {
                // the host role passes on: a seated player first, then whoever has been here longest
                var next = room.Members.Find(x => x.Online && x.Seat >= 0) ?? room.Members.Find(x => x.Online);
                room.HostId = next.Id;
            }
            SendStatus(room);
        }

        void SendStatus(OnlineRoom room)
        {
            int n = room.Members.Count;
            var ids = new int[n]; var names = new string[n]; var seats = new int[n]; var online = new bool[n];
            for (int i = 0; i < n; i++) { var m = room.Members[i]; ids[i] = m.Id; names[i] = m.Name; seats[i] = m.Seat; online[i] = m.Online; }
            var seatNames = new string[2]; var seatOnline = new bool[2];
            for (int s = 0; s < 2; s++)
            {
                var occ = room.InSeat(s);
                seatNames[s] = room.Seats[s] == SeatKind.Bot ? "BOT " + AiProfile.Names[Math.Max(1, room.BotLevels[s]) - 1]
                    : occ != null ? occ.Name : room.Seats[s] == SeatKind.Human ? "(退出)" : "";
                seatOnline[s] = room.Seats[s] == SeatKind.Bot || (occ != null && occ.Online);
            }
            foreach (var m in room.Members)
            {
                if (!m.Online) continue;
                send(m.Conn, new RoomStatus
                {
                    Code = room.Code, MapId = room.MapId, Started = room.Started, GameOver = room.GameOver, Public = room.Public,
                    Speed = room.Speed, BotsOnServer = room.BotsOnServer,
                    MyId = m.Id, MySeat = m.Seat, HostId = room.HostId,
                    SeatKinds = (SeatKind[])room.Seats.Clone(), SeatNames = seatNames, SeatLevels = (int[])room.BotLevels.Clone(), SeatStyles = (int[])room.BotStyles.Clone(), SeatOnline = seatOnline,
                    MemberIds = ids, MemberNames = names, MemberSeats = seats, MemberOnline = online
                });
            }
        }

        static GameLog Log(OnlineRoom room, int seat) =>
            new GameLog
            {
                MapId = room.PlayedMapId ?? room.MapId, MyArmy = seat < 0 ? -1 : seat, Cmds = room.Cmds.ToArray(), Seeds = room.Seeds.ToArray(),
                BotSeeds = (uint[])room.BotSeeds.Clone(), BotLevels = (int[])room.MatchBotLevels.Clone(), BotStyles = (int[])room.MatchBotStyles.Clone()
            };
    }

    /// <summary>Client side copy of the server's game, rebuilt from the log and kept in step.</summary>
    public sealed class OnlineReplica
    {
        public GameState State;
        public int Applied;     // number of commands applied
        /// <summary>Every command applied so far with its seed (the record of the match, for reviewing it later).</summary>
        public readonly List<string> Cmds = new List<string>();
        public readonly List<uint> Seeds = new List<uint>();

        /// <summary>Starts from the map and replays the log.</summary>
        public void Rebuild(GameData data, MapDef map, string[] cmds, uint[] seeds)
        {
            State = GameState.Create(data, map, 1);
            RulesEngine.StartGame(State);
            Applied = 0;
            Cmds.Clear(); Seeds.Clear();
            for (int i = 0; i < cmds.Length; i++) ApplyRaw(cmds[i], seeds[i]);
        }

        /// <summary>Applies one accepted command. Returns null when out of order or out of sync (ask for a resync).</summary>
        public ApplyResult Step(int index, string cmd, uint seed, int hash)
        {
            if (index != Applied) return null;
            var r = ApplyRaw(cmd, seed);
            if (r == null || !r.Ok || CommandCodec.Hash(State) != hash) return null;
            return r;
        }

        /// <summary>Applies one command without the order / hash checks (rebuilding a BOT's phase).</summary>
        public ApplyResult Advance(string text, uint seed) => ApplyRaw(text, seed);

        ApplyResult ApplyRaw(string text, uint seed)
        {
            var c = CommandCodec.Decode(text);
            if (c == null) return null;
            State.Rng = new Rng(seed);
            var r = RulesEngine.Apply(State, c);
            Applied++;
            Cmds.Add(text); Seeds.Add(seed);
            return r;
        }
    }
}
