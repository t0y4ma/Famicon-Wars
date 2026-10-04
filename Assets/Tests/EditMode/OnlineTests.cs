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
            server = new OnlineRoomServer(data, id => id == "map01" ? MapDef.Parse(mapText, data) : null,
                (c, m) => { if (!inbox.TryGetValue(c, out var l)) inbox[c] = l = new List<ServerMsg>(); l.Add(m); },
                () => seed = seed * 1664525u + 1013904223u, 1);
        }

        List<ServerMsg> Box(int c) => inbox.TryGetValue(c, out var l) ? l : (inbox[c] = new List<ServerMsg>());
        T Last<T>(int c) where T : ServerMsg => Box(c).OfType<T>().LastOrDefault();

        string StartMatch()
        {
            server.Create(1, "tokA", "map01");
            var code = Last<RoomStatus>(1).Code;
            server.Join(2, code, "tokB");
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
                // each player decides on its own replica, like a real client
                var cmd = ais[seat].Next(reps[seat].State);
                server.Play(seat + 1, CommandCodec.Encode(cmd));
                var rej = Box(seat + 1).Skip(0).OfType<Rejected>().Count();
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
        public void ReturningPlayerGetsTheSeatAndTheWholeGame()
        {
            var code = StartMatch();
            var room = server.Find(code);
            server.Play(1, CommandCodec.Encode(new EndPhaseCommand { Army = Army.Red }));
            server.Play(2, CommandCodec.Encode(new EndPhaseCommand { Army = Army.Blue }));
            server.Disconnected(2);
            Assert.IsFalse(Last<RoomStatus>(1).OpponentPresent);
            server.Join(7, code, "stranger");
            Assert.IsNotNull(Last<LobbyError>(7), "a new player cannot take a seat mid-game");
            server.Join(8, code, "tokB");
            var log = Last<GameLog>(8);
            Assert.AreEqual(1, log.MyArmy);
            Assert.AreEqual(2, log.Cmds.Length);
            Assert.AreEqual(CommandCodec.Hash(room.State), CommandCodec.Hash(ReplicaFrom(log).State));
            Assert.IsTrue(Last<RoomStatus>(1).OpponentPresent);
        }

        [Test]
        public void LeavingMidGameIsASurrender()
        {
            var code = StartMatch();
            server.Leave(1, true);
            var room = server.Find(code);
            Assert.IsTrue(room.State.GameOver);
            Assert.AreEqual(Army.Blue, room.State.Winner);
            Assert.IsNotNull(Last<LeftRoom>(1));
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
