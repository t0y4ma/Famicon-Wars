using System.Collections.Generic;
using System.IO;
using System.Linq;
using FamiconWars.Core;
using NUnit.Framework;
using UnityEngine;

namespace FamiconWars.Tests
{
    /// <summary>Room server + client replicas without any network: messages go through lists.</summary>
    public class OnlineTests
    {
        GameData data;
        string mapText;
        Dictionary<int, List<ServerMsg>> inbox;
        OnlineRoomServer server;
        uint seed;

        [SetUp]
        public void Setup()
        {
            var dir = Path.Combine(Application.dataPath, "Resources/Data");
            data = GameData.Load(File.ReadAllText(Path.Combine(dir, "units.csv")), File.ReadAllText(Path.Combine(dir, "terrains.csv")),
                File.ReadAllText(Path.Combine(dir, "damage.csv")), File.ReadAllText(Path.Combine(dir, "rules.csv")));
            mapText = File.ReadAllText(Path.Combine(Application.dataPath, "Resources/Maps/map01.txt"));
            inbox = new Dictionary<int, List<ServerMsg>>();
            seed = 99;
            server = new OnlineRoomServer(data, id => id == "map01" || id == "map05" ? MapDef.Parse(id == "map01" ? mapText : File.ReadAllText(Path.Combine(Application.dataPath, "Resources/Maps/map05.txt")), data) : null,
                (c, m) => { if (!inbox.TryGetValue(c, out var l)) inbox[c] = l = new List<ServerMsg>(); l.Add(m); },
                () => seed = seed * 1664525u + 1013904223u, 1);
        }

        List<ServerMsg> Box(int c) => inbox.TryGetValue(c, out var l) ? l : (inbox[c] = new List<ServerMsg>());
        T Last<T>(int c) where T : ServerMsg => Box(c).OfType<T>().LastOrDefault();

        /// <summary>Host (conn 1) creates, a second player (conn 2) joins into the free blue seat, the host starts.</summary>
        string StartMatch()
        {
            server.Create(1, "tokA", "Aさん", "map01");
            var code = Last<RoomStatus>(1).Code;
            server.Join(2, code, "tokB", "Bさん");
            server.Action(1, "start", 0, null);
            return code;
        }

        OnlineReplica ReplicaFrom(GameLog log)
        {
            var r = new OnlineReplica();
            r.Rebuild(data, MapDef.Parse(mapText, data), log.Cmds, log.Seeds);
            return r;
        }

        [Test]
        public void ReplicasStayInStepWithTheServer()
        {
            var code = StartMatch();
            var room = server.Find(code);
            Assert.IsTrue(room.Started);
            var reps = new[] { ReplicaFrom(Last<GameLog>(1)), ReplicaFrom(Last<GameLog>(2)) };
            var ais = new[] { new AiPlayer(Army.Red, AiProfile.ForLevel(2), 5), new AiPlayer(Army.Blue, AiProfile.ForLevel(2), 6) };
            int seen = 0;
            for (int step = 0; step < 3000 && !room.State.GameOver && room.State.Day <= 12; step++)
            {
                int seat = (int)room.State.Active;
                var cmd = ais[seat].Next(reps[seat].State);      // each player decides on its own replica
                server.Play(seat + 1, CommandCodec.Encode(cmd));
                var applied = Box(1).OfType<Applied>().ToList();
                if (applied.Count == seen) { ais[seat].Rejected(cmd); continue; }
                for (; seen < applied.Count; seen++)
                {
                    var a = applied[seen];
                    foreach (var r in reps) Assert.IsNotNull(r.Step(a.Index, a.Cmd, a.Seed, a.Hash), "replica drifted at " + a.Index);
                }
            }
            Assert.Greater(seen, 50);
            Assert.AreEqual(CommandCodec.Hash(room.State), CommandCodec.Hash(reps[0].State));
            Assert.AreEqual(CommandCodec.Hash(room.State), CommandCodec.Hash(reps[1].State));
        }

        [Test]
        public void RoomShowsSeatsHostAndSpectators()
        {
            server.Create(1, "tokA", "Aさん", "map01");
            var code = Last<RoomStatus>(1).Code;
            server.Join(2, code, "tokB", "Bさん");
            server.Join(3, code, "tokC", "Cさん");
            var st = Last<RoomStatus>(3);
            Assert.AreEqual(-1, st.MySeat, "seats are full: the third member watches");
            Assert.AreEqual(SeatKind.Human, st.SeatKinds[0]); Assert.AreEqual("Aさん", st.SeatNames[0]);
            Assert.AreEqual(SeatKind.Human, st.SeatKinds[1]); Assert.AreEqual("Bさん", st.SeatNames[1]);
            Assert.AreEqual(Last<RoomStatus>(1).MyId, st.HostId);
            Assert.AreEqual(3, st.MemberIds.Length);

            server.Action(2, "stand", 0, null);
            Assert.AreEqual(SeatKind.Empty, Last<RoomStatus>(1).SeatKinds[1]);
            server.Action(3, "sit", 1, null);
            Assert.AreEqual(1, Last<RoomStatus>(3).MySeat);
            server.Action(2, "sit", 1, null);
            Assert.IsNotNull(Last<LobbyError>(2), "a taken seat cannot be taken");
        }

        [Test]
        public void OnlyTheHostSetsTheMapAndBotsAndStarts()
        {
            server.Create(1, "tokA", "Aさん", "map01");
            var code = Last<RoomStatus>(1).Code;
            server.Join(2, code, "tokB", "Bさん");
            server.Action(2, "map", 0, "map05");
            Assert.IsNotNull(Last<LobbyError>(2));
            server.Action(2, "start", 0, null);
            Assert.IsFalse(server.Find(code).Started);
            server.Action(1, "map", 0, "map05");
            Assert.AreEqual("map05", Last<RoomStatus>(2).MapId);
            server.Action(1, "bot", 1, "3");
            Assert.IsNotNull(Last<LobbyError>(1), "a person sits there");
            server.Action(2, "stand", 0, null);
            server.Action(1, "bot", 1, "3");
            var st = Last<RoomStatus>(2);
            Assert.AreEqual(SeatKind.Bot, st.SeatKinds[1]);
            Assert.AreEqual(3, st.SeatLevels[1]);
            server.Action(1, "start", 0, null);
            Assert.IsTrue(server.Find(code).Started);
            Assert.AreEqual(-1, Last<GameLog>(2).MyArmy, "the member who stood up watches");
        }

        [Test]
        public void HostRoleCanBeHandedOverAndPassesOnWhenTheHostLeaves()
        {
            server.Create(1, "tokA", "Aさん", "map01");
            var code = Last<RoomStatus>(1).Code;
            server.Join(2, code, "tokB", "Bさん");
            server.Join(3, code, "tokC", "Cさん");
            int idA = Last<RoomStatus>(1).MyId, idC = Last<RoomStatus>(3).MyId;
            server.Action(2, "host", idC, null);
            Assert.IsNotNull(Last<LobbyError>(2), "only the host hands the role over");
            server.Action(1, "host", idC, null);
            Assert.AreEqual(idC, Last<RoomStatus>(1).HostId);
            server.Leave(3, true);
            Assert.AreEqual(idA, Last<RoomStatus>(1).HostId, "a seated player inherits the host role");
        }

        [Test]
        public void BotSeatPlaysByItselfAndSpectatorsWatch()
        {
            server.Create(1, "tokA", "Aさん", "map01");
            var code = Last<RoomStatus>(1).Code;
            server.Action(1, "bot", 1, "1");
            server.Action(1, "start", 0, null);
            server.Join(5, code, "tokS", "観戦者");
            Assert.AreEqual(-1, Last<GameLog>(5).MyArmy);
            var room = server.Find(code);
            var watch = ReplicaFrom(Last<GameLog>(5));
            double t = 0;
            for (int day = 0; day < 3; day++)
            {
                server.Play(1, CommandCodec.Encode(new EndPhaseCommand { Army = Army.Red }));
                for (int k = 0; k < 400 && room.State.Active == Army.Blue && !room.GameOver; k++) server.Tick(t += 1);
                Assert.AreEqual(Army.Red, room.State.Active, "the BOT ends its phase");
            }
            server.Play(5, CommandCodec.Encode(new EndPhaseCommand { Army = Army.Red }));
            Assert.IsNotNull(Last<Rejected>(5), "a spectator cannot play");
            foreach (var a in Box(5).OfType<Applied>()) Assert.IsNotNull(watch.Step(a.Index, a.Cmd, a.Seed, a.Hash));
            Assert.AreEqual(CommandCodec.Hash(room.State), CommandCodec.Hash(watch.State));
            Assert.Greater(room.Cmds.Count, 6, "the BOT did more than just end its phases");
        }

        [Test]
        public void ReturningPlayerGetsTheSeatAndTheWholeGame()
        {
            var code = StartMatch();
            var room = server.Find(code);
            server.Play(1, CommandCodec.Encode(new EndPhaseCommand { Army = Army.Red }));
            server.Play(2, CommandCodec.Encode(new EndPhaseCommand { Army = Army.Blue }));
            server.Disconnected(2);
            Assert.IsFalse(Last<RoomStatus>(1).SeatOnline[1]);
            server.Join(7, code, "stranger", "x");
            Assert.AreEqual(-1, Last<GameLog>(7).MyArmy, "a newcomer mid-game watches");
            server.Join(8, code, "tokB", "Bさん");
            var log = Last<GameLog>(8);
            Assert.AreEqual(1, log.MyArmy);
            Assert.AreEqual(2, log.Cmds.Length);
            Assert.AreEqual(CommandCodec.Hash(room.State), CommandCodec.Hash(ReplicaFrom(log).State));
            Assert.IsTrue(Last<RoomStatus>(1).SeatOnline[1]);
        }

        [Test]
        public void LeavingMidGameIsASurrenderAndTheHostCanReopenTheRoom()
        {
            var code = StartMatch();
            server.Leave(2, true);
            var room = server.Find(code);
            Assert.IsTrue(room.State.GameOver);
            Assert.AreEqual(Army.Red, room.State.Winner);
            Assert.IsNotNull(Last<LeftRoom>(2));
            server.Action(1, "reset", 0, null);
            var st = Last<RoomStatus>(1);
            Assert.IsFalse(st.Started);
            Assert.AreEqual(0, st.MySeat);
            Assert.AreEqual(SeatKind.Empty, st.SeatKinds[1]);
        }

        [Test]
        public void OutOfTurnAndMalformedCommandsAreRefused()
        {
            var code = StartMatch();
            server.Play(2, CommandCodec.Encode(new EndPhaseCommand { Army = Army.Blue }));
            Assert.IsNotNull(Last<Rejected>(2));
            server.Play(2, CommandCodec.Encode(new EndPhaseCommand { Army = Army.Red }));
            Assert.AreEqual(2, Box(2).OfType<Rejected>().Count(), "blue cannot end red's phase");
            server.Play(1, "U,0,zz");
            Assert.IsNotNull(Last<Rejected>(1));
            Assert.AreEqual(0, server.Find(code).Cmds.Count);
        }

        [Test]
        public void AfterTheMatchTheRoomCanBeRearrangedAndTheNextMatchStarted()
        {
            var code = StartMatch();
            var room = server.Find(code);
            server.Play(2, CommandCodec.Encode(new SurrenderCommand { Army = Army.Blue }));
            Assert.IsTrue(room.GameOver);
            Assert.IsTrue(Last<RoomStatus>(1).GameOver);

            // seats, map and BOTs can change without "reset" first
            server.Action(2, "stand", 0, null);
            Assert.AreEqual(-1, Last<RoomStatus>(2).MySeat);
            server.Action(1, "sit", 1, null);
            Assert.AreEqual(1, Last<RoomStatus>(1).MySeat);
            server.Action(1, "map", 0, "map05");
            Assert.AreEqual("map05", Last<RoomStatus>(1).MapId);
            server.Action(1, "bot", 0, "2");
            Assert.AreEqual(SeatKind.Bot, Last<RoomStatus>(1).SeatKinds[0]);
            Assert.IsNull(Last<LobbyError>(1));
            Assert.IsNull(Last<LobbyError>(2));

            // the finished game still rebuilds on its own map until the next match starts
            Assert.AreEqual("map01", room.PlayedMapId);

            int logs = Box(2).OfType<GameLog>().Count();
            server.Action(1, "start", 0, null);
            Assert.IsTrue(room.Started && !room.GameOver);
            Assert.AreEqual("map05", Last<GameLog>(1).MapId);
            Assert.AreEqual(1, Last<GameLog>(1).MyArmy);
            Assert.AreEqual(logs + 1, Box(2).OfType<GameLog>().Count());
            Assert.AreEqual(-1, Last<GameLog>(2).MyArmy);
            Assert.AreEqual(0, Last<GameLog>(1).Cmds.Length);
        }

        [Test]
        public void AfterTheMatchANewcomerTakesAFreeSeatAndGoneMembersLoseTheirs()
        {
            var code = StartMatch();
            server.Disconnected(2);                                   // blue drops out mid-game: place kept
            Assert.AreEqual(SeatKind.Human, Last<RoomStatus>(1).SeatKinds[1]);
            server.Play(1, CommandCodec.Encode(new SurrenderCommand { Army = Army.Red }));
            var st = Last<RoomStatus>(1);
            Assert.IsTrue(st.GameOver);
            Assert.AreEqual(SeatKind.Empty, st.SeatKinds[1], "gone player gives the seat up when the match ends");
            int logs = Box(3).OfType<GameLog>().Count();
            server.Join(3, code, "tokC", "Cさん");
            Assert.AreEqual(1, Last<RoomStatus>(3).MySeat);
            Assert.AreEqual(logs, Box(3).OfType<GameLog>().Count(), "no finished game is pushed onto a newcomer");
        }

        [Test]
        public void PublicRoomsAreListedPrivateOnesAreNot()
        {
            server.Create(1, "tokA", "Aさん", "map01", true);
            var pub = Last<RoomStatus>(1);
            Assert.IsTrue(pub.Public);
            server.Create(2, "tokB", "Bさん", "map05", false);
            var priv = Last<RoomStatus>(2).Code;
            server.List(9);
            var list = Last<RoomList>(9);
            Assert.AreEqual(1, list.Codes.Length);
            Assert.AreEqual(pub.Code, list.Codes[0]);
            Assert.AreEqual("Aさん", list.HostNames[0]);
            Assert.AreEqual(0, list.States[0]);
            Assert.AreEqual(1, list.Members[0]);
            Assert.AreEqual(1, list.SeatsTaken[0]);

            // only the host switches it; the change shows in the list
            server.Join(3, priv, "tokC", "Cさん");
            server.Action(3, "public", 1, null);
            Assert.IsNotNull(Last<LobbyError>(3));
            server.Action(2, "public", 1, null);
            Assert.IsTrue(Last<RoomStatus>(2).Public);
            server.Action(1, "public", 0, null);
            server.List(9);
            list = Last<RoomList>(9);
            Assert.AreEqual(1, list.Codes.Length);
            Assert.AreEqual(priv, list.Codes[0]);
            Assert.AreEqual(2, list.Members[0]);
            Assert.AreEqual(2, list.SeatsTaken[0]);
        }

        [Test]
        public void RoomDisappearsWhenEveryoneIsGone()
        {
            var code = StartMatch();
            server.Disconnected(1);
            server.Disconnected(2);
            Assert.IsNull(server.Find(code));
            Assert.AreEqual(0, server.RoomCount);
        }

        [Test]
        public void CommandCodecRoundTrips()
        {
            var u = new UnitCommand { Army = Army.Blue, UnitId = 12, ToX = 3, ToY = 4, Action = UnitAction.Unload, CargoId = 7, DropX = 3, DropY = 5 };
            Assert.AreEqual(CommandCodec.Encode(u), CommandCodec.Encode(CommandCodec.Decode(CommandCodec.Encode(u))));
            var p = new ProduceCommand { Army = Army.Red, X = 2, Y = 9, UnitType = "TANK_B" };
            Assert.AreEqual(CommandCodec.Encode(p), CommandCodec.Encode(CommandCodec.Decode(CommandCodec.Encode(p))));
            Assert.IsNull(CommandCodec.Decode("U,5,1,1,1,1,1,1,1,1"), "army out of range");
            Assert.IsNull(CommandCodec.Decode("X,0"));
        }
    }
}
