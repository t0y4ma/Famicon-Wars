using System;
using System.Collections.Generic;

namespace FamiconWars.Core
{
    // ------------------------------------------------------------------------------------------
    // Online play, transport-independent. The server owns the only real GameState per room and
    // validates every command with RulesEngine. Clients keep a replica and stay in sync by
    // re-applying each accepted command with the random seed the server drew for it (a fresh seed
    // per command, revealed only when it is used, so nobody can predict combat rolls).
    // Traffic is one short text line per action; a joining/returning player gets the whole log.
    // ------------------------------------------------------------------------------------------

    public abstract class ServerMsg { }
    /// <summary>Room state for one player. MyArmy: 0 red, 1 blue.</summary>
    public sealed class RoomStatus : ServerMsg { public string Code, MapId; public int MyArmy; public bool Started, OpponentPresent, GameOver; }
    /// <summary>Everything needed to rebuild the game: map + every accepted command with its seed.</summary>
    public sealed class GameLog : ServerMsg { public string MapId; public int MyArmy; public string[] Cmds; public uint[] Seeds; }
    public sealed class Applied : ServerMsg { public int Index; public string Cmd; public uint Seed; public int Hash; }
    public sealed class Rejected : ServerMsg { public string Error; }
    public sealed class LobbyError : ServerMsg { public string Error; }
    public sealed class LeftRoom : ServerMsg { }

    public sealed class OnlineRoom
    {
        public string Code, MapId;
        public GameState State;
        public readonly List<string> Cmds = new List<string>();
        public readonly List<uint> Seeds = new List<uint>();
        public readonly string[] Tokens = new string[2];     // server-only identity per seat (rejoin)
        public readonly int[] Conns = { -1, -1 };            // live connection per seat, -1 = away
        public bool Started => State != null;
        public int SeatOf(int conn) => Conns[0] == conn ? 0 : Conns[1] == conn ? 1 : -1;
        public bool Empty => Conns[0] < 0 && Conns[1] < 0;
    }

    public sealed class OnlineRoomServer
    {
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

        public void Create(int conn, string token, string mapId)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 64) { send(conn, new LobbyError { Error = "接続情報が不正です" }); return; }
            Leave(conn, false);
            if (loadMap(mapId) == null) mapId = "map01";
            string code;
            do code = codes.Next(1000, 10000).ToString(); while (rooms.ContainsKey(code));
            var room = new OnlineRoom { Code = code, MapId = mapId };
            room.Tokens[0] = token; room.Conns[0] = conn;
            rooms[code] = room; byConn[conn] = room;
            SendStatus(room);
        }

        public void Join(int conn, string code, string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 64) { send(conn, new LobbyError { Error = "接続情報が不正です" }); return; }
            var room = Find(code?.Trim());
            if (room == null) { send(conn, new LobbyError { Error = "部屋が見つかりません" }); return; }
            if (room.SeatOf(conn) >= 0) { SendStatus(room); return; }
            Leave(conn, false);

            // a returning player gets their own seat back (same browser token), even mid-game
            for (int i = 0; i < 2; i++)
                if (room.Tokens[i] == token && room.Conns[i] < 0)
                {
                    room.Conns[i] = conn; byConn[conn] = room;
                    SendStatus(room);
                    if (room.Started) send(conn, Log(room, i));
                    return;
                }

            if (room.Started || room.Tokens[1] != null) { send(conn, new LobbyError { Error = "この部屋は満員です" }); return; }
            room.Tokens[1] = token; room.Conns[1] = conn; byConn[conn] = room;

            // both seats filled: the match starts (red = creator moves first)
            room.State = GameState.Create(data, loadMap(room.MapId), nextSeed());
            RulesEngine.StartGame(room.State);
            SendStatus(room);
            for (int i = 0; i < 2; i++) if (room.Conns[i] >= 0) send(room.Conns[i], Log(room, i));
        }

        public void Play(int conn, string text)
        {
            if (!byConn.TryGetValue(conn, out var room) || !room.Started) { send(conn, new Rejected { Error = "対戦中ではありません" }); return; }
            int seat = room.SeatOf(conn);
            var cmd = CommandCodec.Decode(text);
            if (cmd == null) { send(conn, new Rejected { Error = "不正なコマンドです" }); return; }
            if ((int)cmd.Army != seat) { send(conn, new Rejected { Error = "手番ではありません" }); return; }
            Apply(room, cmd);
        }

        void Apply(OnlineRoom room, Command cmd)
        {
            uint seed = nextSeed();
            room.State.Rng = new Rng(seed);
            var r = RulesEngine.Apply(room.State, cmd);
            if (!r.Ok)
            {
                int c = room.Conns[(int)cmd.Army];
                if (c >= 0) send(c, new Rejected { Error = r.Error });
                return;
            }
            var text = CommandCodec.Encode(cmd);
            room.Cmds.Add(text); room.Seeds.Add(seed);
            var msg = new Applied { Index = room.Cmds.Count - 1, Cmd = text, Seed = seed, Hash = CommandCodec.Hash(room.State) };
            for (int i = 0; i < 2; i++) if (room.Conns[i] >= 0) send(room.Conns[i], msg);
            if (room.State.GameOver) SendStatus(room);
        }

        public void Resync(int conn)
        {
            if (!byConn.TryGetValue(conn, out var room) || !room.Started) return;
            send(conn, Log(room, room.SeatOf(conn)));
        }

        /// <summary>
        /// Leaving on purpose during a match is a surrender. A disconnect (closed tab, lost network)
        /// keeps the seat so the player can come back with the same browser.
        /// </summary>
        public void Leave(int conn, bool notify)
        {
            if (!byConn.TryGetValue(conn, out var room)) return;
            int seat = room.SeatOf(conn);
            if (seat >= 0 && room.Started && !room.State.GameOver)
                Apply(room, new SurrenderCommand { Army = (Army)seat });
            Drop(room, conn, seat, freeSeat: true);
            if (notify) send(conn, new LeftRoom());
        }

        public void Disconnected(int conn)
        {
            if (!byConn.TryGetValue(conn, out var room)) return;
            Drop(room, conn, room.SeatOf(conn), freeSeat: !room.Started);
        }

        void Drop(OnlineRoom room, int conn, int seat, bool freeSeat)
        {
            byConn.Remove(conn);
            if (seat >= 0)
            {
                room.Conns[seat] = -1;
                if (freeSeat || (room.Started && room.State.GameOver)) room.Tokens[seat] = room.Started ? room.Tokens[seat] : null;
            }
            if (room.Empty) { rooms.Remove(room.Code); return; }
            SendStatus(room);
        }

        void SendStatus(OnlineRoom room)
        {
            for (int i = 0; i < 2; i++)
            {
                if (room.Conns[i] < 0) continue;
                send(room.Conns[i], new RoomStatus
                {
                    Code = room.Code, MapId = room.MapId, MyArmy = i, Started = room.Started,
                    OpponentPresent = room.Conns[1 - i] >= 0, GameOver = room.Started && room.State.GameOver
                });
            }
        }

        static GameLog Log(OnlineRoom room, int seat) =>
            new GameLog { MapId = room.MapId, MyArmy = seat, Cmds = room.Cmds.ToArray(), Seeds = room.Seeds.ToArray() };
    }

    /// <summary>Client side copy of the server's game, rebuilt from the log and kept in step.</summary>
    public sealed class OnlineReplica
    {
        public GameState State;
        public int Applied;     // number of commands applied

        /// <summary>Starts from the map and replays the log. Returns the results (events) of the last command.</summary>
        public void Rebuild(GameData data, MapDef map, string[] cmds, uint[] seeds)
        {
            State = GameState.Create(data, map, 1);
            RulesEngine.StartGame(State);
            Applied = 0;
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

        ApplyResult ApplyRaw(string text, uint seed)
        {
            var c = CommandCodec.Decode(text);
            if (c == null) return null;
            State.Rng = new Rng(seed);
            var r = RulesEngine.Apply(State, c);
            Applied++;
            return r;
        }
    }
}
