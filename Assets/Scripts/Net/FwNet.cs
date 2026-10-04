using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using FamiconWars.Core;
using Mirror;
using Mirror.SimpleWeb;
using UnityEngine;

namespace FamiconWars.Net
{
    // ---------------- wire messages (thin mirrors of the Core protocol) ----------------

    public struct CreateRoomMsg : NetworkMessage { public int version; public string token; public string mapId; }
    public struct JoinRoomMsg : NetworkMessage { public int version; public string code; public string token; }
    public struct LeaveRoomMsg : NetworkMessage { }
    public struct PlayMsg : NetworkMessage { public string cmd; }
    public struct ResyncMsg : NetworkMessage { }

    public struct RoomStatusMsg : NetworkMessage { public string code; public string mapId; public int myArmy; public bool started, opponentPresent, gameOver; }
    public struct GameLogMsg : NetworkMessage { public string mapId; public int myArmy; public string[] cmds; public uint[] seeds; }
    public struct AppliedMsg : NetworkMessage { public int index; public string cmd; public uint seed; public int hash; }
    public struct RejectedMsg : NetworkMessage { public string error; }
    public struct LobbyErrorMsg : NetworkMessage { public string error; }
    public struct LeftRoomMsg : NetworkMessage { }

    /// <summary>
    /// Where the server lives. Same layout as the other games on the VM: Caddy terminates TLS on
    /// nine.freeddns.org and routes paths starting with /fw to this server on 127.0.0.1:7782.
    /// </summary>
    public static class NetConfig
    {
        /// <summary>Bump whenever messages or rules change: client and server must match.</summary>
        public const int ProtocolVersion = 1;
        public static ushort ServerPort = 7782;
        public const string PublicUrl = "wss://nine.freeddns.org/fw";
        public const string TokenKey = "fw.clientToken";

        /// <summary>Lobby default: the public server in the web build, the local machine elsewhere.</summary>
        public static string DefaultAddress
        {
            get
            {
                if (Application.platform != RuntimePlatform.WebGLPlayer) return "localhost";
                // index.html?server=localhost points the web build at another server (testing)
                var url = Application.absoluteURL ?? "";
                int q = url.IndexOf("server=", StringComparison.Ordinal);
                if (q >= 0)
                {
                    var v = url.Substring(q + 7);
                    int amp = v.IndexOfAny(new[] { '&', '#' });
                    if (amp >= 0) v = v.Substring(0, amp);
                    v = Uri.UnescapeDataString(v);
                    if (v.Length > 0) return v;
                }
                return PublicUrl;
            }
        }

        /// <summary>"wss://host/path" or "ws://host:port" as typed, or a bare host name (ws on ServerPort).</summary>
        public static Uri ToUri(string address)
        {
            address = string.IsNullOrWhiteSpace(address) ? DefaultAddress : address.Trim();
            if (!address.Contains("://")) address = "ws://" + address + (address.Contains(":") ? "" : ":" + ServerPort);
            return Uri.TryCreate(address, UriKind.Absolute, out var u) ? u : null;
        }

        public static string ClientToken
        {
            get
            {
                string t = "";
                try { t = PlayerPrefs.GetString(TokenKey, ""); } catch (Exception) { }
                if (string.IsNullOrEmpty(t))
                {
                    t = Guid.NewGuid().ToString("N");
                    try { PlayerPrefs.SetString(TokenKey, t); PlayerPrefs.Save(); } catch (Exception) { }
                }
                return t;
            }
        }

        public static bool IsDedicatedServer
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return Array.IndexOf(Environment.GetCommandLineArgs(), "-server") >= 0;
#endif
            }
        }
    }

    /// <summary>
    /// Message-only Mirror setup: no player objects, no spawning. The server keeps rooms in
    /// OnlineRoomServer (Core); clients receive events through FwClient.
    /// </summary>
    public class FwNetworkManager : NetworkManager
    {
        public static FwNetworkManager Instance;
        OnlineRoomServer rooms;
        GameData data;

        public static FwNetworkManager Create(GameData data)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Network");
            go.SetActive(false);                      // configure before Awake runs
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-port" && ushort.TryParse(args[i + 1], out var port)) NetConfig.ServerPort = port;
            var t = go.AddComponent<SimpleWebTransport>();
            t.port = NetConfig.ServerPort;
            t.maxMessageSize = 1 << 20;               // a full log for a long game fits easily
            t.sslEnabled = false;                     // TLS ends at Caddy
            var nm = go.AddComponent<FwNetworkManager>();
            nm.transport = t;
            nm.autoCreatePlayer = false;
            nm.dontDestroyOnLoad = true;
            nm.offlineScene = ""; nm.onlineScene = "";
            nm.maxConnections = 200;
            nm.data = data;
            go.SetActive(true);
            Instance = nm;
            return nm;
        }

        // ---------------- server ----------------

        public override void OnStartServer()
        {
            base.OnStartServer();
            uint Seed()
            {
                var b = new byte[4];
                using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
                return BitConverter.ToUInt32(b, 0) | 1u;
            }
            rooms = new OnlineRoomServer(data, LoadMap, SendTo, Seed, Environment.TickCount);
            NetworkServer.RegisterHandler<CreateRoomMsg>((c, m) => { if (VersionOk(c, m.version)) rooms.Create(c.connectionId, m.token, m.mapId); }, false);
            NetworkServer.RegisterHandler<JoinRoomMsg>((c, m) => { if (VersionOk(c, m.version)) rooms.Join(c.connectionId, m.code, m.token); }, false);
            NetworkServer.RegisterHandler<LeaveRoomMsg>((c, m) => rooms.Leave(c.connectionId, true), false);
            NetworkServer.RegisterHandler<PlayMsg>((c, m) => rooms.Play(c.connectionId, m.cmd), false);
            NetworkServer.RegisterHandler<ResyncMsg>((c, m) => rooms.Resync(c.connectionId), false);
            Debug.Log("[FW] server started on port " + NetConfig.ServerPort);
        }

        static bool VersionOk(NetworkConnectionToClient c, int v)
        {
            if (v == NetConfig.ProtocolVersion) return true;
            c.Send(new LobbyErrorMsg { error = "サーバーとゲームのバージョンが違います。ページを再読み込みしてください" });
            return false;
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            // no authentication step: mark ready so messages flow both ways
            NetworkServer.SetClientReady(conn);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            rooms?.Disconnected(conn.connectionId);
            base.OnServerDisconnect(conn);
        }

        MapDef LoadMap(string id)
        {
            var e = Game.MapCatalog.Find(id);
            if (e == null || !e.Ready) return null;
            var ta = Resources.Load<TextAsset>("Maps/" + id);
            return ta == null ? null : MapDef.Parse(ta.text, data);
        }

        static void SendTo(int connId, ServerMsg m)
        {
            if (!NetworkServer.connections.TryGetValue(connId, out var c)) return;
            switch (m)
            {
                case RoomStatus s: c.Send(new RoomStatusMsg { code = s.Code, mapId = s.MapId, myArmy = s.MyArmy, started = s.Started, opponentPresent = s.OpponentPresent, gameOver = s.GameOver }); break;
                case GameLog l: c.Send(new GameLogMsg { mapId = l.MapId, myArmy = l.MyArmy, cmds = l.Cmds, seeds = l.Seeds }); break;
                case Applied a: c.Send(new AppliedMsg { index = a.Index, cmd = a.Cmd, seed = a.Seed, hash = a.Hash }); break;
                case Rejected r: c.Send(new RejectedMsg { error = r.Error }); break;
                case LobbyError e: c.Send(new LobbyErrorMsg { error = e.Error }); break;
                case LeftRoom _: c.Send(new LeftRoomMsg()); break;
            }
        }

        // ---------------- client ----------------

        public override void OnStartClient()
        {
            base.OnStartClient();
            NetworkClient.RegisterHandler<RoomStatusMsg>(m => FwClient.Raise(m), false);
            NetworkClient.RegisterHandler<GameLogMsg>(m => FwClient.Raise(m), false);
            NetworkClient.RegisterHandler<AppliedMsg>(m => FwClient.Raise(m), false);
            NetworkClient.RegisterHandler<RejectedMsg>(m => FwClient.Raise(m), false);
            NetworkClient.RegisterHandler<LobbyErrorMsg>(m => FwClient.Raise(m), false);
            NetworkClient.RegisterHandler<LeftRoomMsg>(m => FwClient.Raise(m), false);
        }

        public override void OnClientConnect()
        {
            // skip base: it would request a player object
            if (!NetworkClient.ready) NetworkClient.Ready();
            FwClient.RaiseConnected();
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            FwClient.RaiseDisconnected();
        }

        public override void OnClientError(TransportError error, string reason)
        {
            base.OnClientError(error, reason);
            FwClient.RaiseError(reason);
        }
    }

    /// <summary>Client facade used by the game: connect, send, and events for everything the server says.</summary>
    public static class FwClient
    {
        public static event Action Connected, Disconnected;
        public static event Action<string> Error, LobbyError, Rejected;
        public static event Action<RoomStatusMsg> Status;
        public static event Action<GameLogMsg> Log;
        public static event Action<AppliedMsg> Applied;
        public static event Action Left;

        public static bool IsConnected => NetworkClient.isConnected;

        /// <returns>false when the address cannot be understood.</returns>
        public static bool Connect(string address)
        {
            var nm = FwNetworkManager.Instance;
            if (NetworkClient.active) return true;
            var uri = NetConfig.ToUri(address);
            if (uri == null) return false;
            nm.networkAddress = uri.Host;
            nm.StartClient(uri);
            return true;
        }

        public static void Disconnect() { if (NetworkClient.active) FwNetworkManager.Instance.StopClient(); }

        public static void CreateRoom(string mapId) => NetworkClient.Send(new CreateRoomMsg { version = NetConfig.ProtocolVersion, token = NetConfig.ClientToken, mapId = mapId });
        public static void JoinRoom(string code) => NetworkClient.Send(new JoinRoomMsg { version = NetConfig.ProtocolVersion, code = code, token = NetConfig.ClientToken });
        public static void LeaveRoom() => NetworkClient.Send(new LeaveRoomMsg());
        public static void Play(Command c) => NetworkClient.Send(new PlayMsg { cmd = CommandCodec.Encode(c) });
        public static void RequestResync() => NetworkClient.Send(new ResyncMsg());

        internal static void Raise(RoomStatusMsg m) => Status?.Invoke(m);
        internal static void Raise(GameLogMsg m) => Log?.Invoke(m);
        internal static void Raise(AppliedMsg m) => Applied?.Invoke(m);
        internal static void Raise(RejectedMsg m) => Rejected?.Invoke(m.error);
        internal static void Raise(LobbyErrorMsg m) => LobbyError?.Invoke(m.error);
        internal static void Raise(LeftRoomMsg m) => Left?.Invoke();
        internal static void RaiseConnected() => Connected?.Invoke();
        internal static void RaiseDisconnected() => Disconnected?.Invoke();
        internal static void RaiseError(string r) => Error?.Invoke(r);
    }
}
