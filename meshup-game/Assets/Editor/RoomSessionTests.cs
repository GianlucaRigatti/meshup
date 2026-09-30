using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using Ubiq.Dictionaries;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.Editor.Tests
{
    public sealed class RoomSessionTests
    {
        [Serializable]
        private sealed class RoomPacket
        {
            public string type;
            public string args;
        }

        private sealed class TestRoom : Dictionary<string, string>, IRoom
        {
            public string Name => "Test room";
            public string UUID { get; set; }
            public string JoinCode => "TEST";
            public bool Publish { get; set; }
        }

        private sealed class UnavailableSceneAPI : SceneManagerAPI
        {
            protected override AsyncOperation LoadSceneAsyncByNameOrIndex(string sceneName,
                int sceneBuildIndex, LoadSceneParameters parameters, bool mustCompleteNextFrame) => null;
        }

        private GameObject root;
        private UbiqRoomSession session;
        private RoomClient client;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Room session test");
            var network = root.AddComponent<NetworkScene>();
            client = root.AddComponent<RoomClient>();
            session = root.AddComponent<UbiqRoomSession>();
            Set(session, "roomClient", client);
            Set(session, "lobbySceneName", SceneManager.GetActiveScene().name);
            Set(client, "scene", network);
            Set(client, "servers", Array.Empty<ConnectionDefinition>());
            Invoke(session, "Subscribe");
        }

        [TearDown]
        public void TearDown()
        {
            Invoke(session, "Unsubscribe");
            session.StopAllCoroutines();
            UnityEngine.Object.DestroyImmediate(root);
        }

        [TestCase(null, "")]
        [TestCase("   ", "")]
        [TestCase("  Ada    Lovelace  ", "Ada Lovelace")]
        [TestCase("This username is much too long to fit", "This username is much to")]
        public void LobbyDisplayNamesAreNormalizedAndLimited(string input,
            string expected)
        {
            Assert.That(UbiqRoomSession.NormalizeDisplayName(input),
                Is.EqualTo(expected));
        }

        [Test]
        public void LobbyCreatesAReadableRandomGuestName()
        {
            Assert.That(UbiqRoomSession.GenerateGuestDisplayName(),
                Does.Match("^Guest [0-9]{4}$"));
        }

        [TestCase("EnterInitialLobby", false)]
        [TestCase("EnterInitialLobby", true)]
        [TestCase("Recover", false)]
        [TestCase("Recover", true)]
        public void FailedLobbyEntryStopsAndReportsTheFailure(string operation, bool timeout)
        {
            Prepare(operation, RoomSessionState.Connecting);
            var version = (int)Get(session, "operationVersion");
            var errors = new List<string>();
            session.ErrorOccurred += errors.Add;

            Fail(timeout, version, "Connection failed");

            Assert.That(session.State, Is.EqualTo(RoomSessionState.Error));
            Assert.That(session.LastError, Is.EqualTo("Connection failed"));
            Assert.That(errors, Is.EqualTo(new[] { "Connection failed" }));
            Assert.That(Get(session, "pendingOperation").ToString(), Is.EqualTo("None"));
            Assert.That((int)Get(session, "operationVersion"), Is.GreaterThan(version));
        }

        [TestCase("EnterInitialLobby")]
        [TestCase("Recover")]
        [TestCase("Create")]
        [TestCase("Join")]
        [TestCase("Leave")]
        public void AnOldTimeoutCannotChangeTheCurrentOperation(string operation)
        {
            Prepare(operation, RoomSessionState.Joining);
            Set(session, "operationVersion", 2);
            var timeout = (IEnumerator)Invoke(session, "RoomOperationTimeout", 1, "Old timeout");
            Assert.That(timeout.MoveNext(), Is.True);
            Assert.That(timeout.MoveNext(), Is.False);
            Assert.That(session.State, Is.EqualTo(RoomSessionState.Joining));
            Assert.That(session.LastError, Is.Empty);
            Assert.That(Get(session, "pendingOperation").ToString(), Is.EqualTo(operation));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void FailedCreateOrJoinRecoversOnceAndKeepsTheOriginalError(bool join, bool timeout)
        {
            client.Room.GetType().GetProperty("UUID").SetValue(client.Room, "previous-room");
            Invoke(session, "SetState", RoomSessionState.LobbyReady);
            Assert.That(join ? session.JoinRoom("TEST") : session.CreateRoom(" Test room "), Is.True);
            var version = (int)Get(session, "operationVersion");

            Fail(timeout, version, "Request failed");

            Assert.That(session.State, Is.EqualTo(RoomSessionState.Connecting));
            Assert.That(Get(session, "pendingOperation").ToString(), Is.EqualTo("Recover"));
            Assert.That(Get(session, "roomBeforeOperation"), Is.EqualTo("previous-room"),
                "The previous room must be captured before reconnect resets it.");
            Assert.That(client.Room.UUID, Is.Null);

            client.OnJoinedRoom.Invoke(new TestRoom { UUID = "private-room" });
            Assert.That(session.State, Is.EqualTo(RoomSessionState.LobbyReady));
            Assert.That(session.LastError, Is.EqualTo("Request failed"));
            Assert.That(session.CurrentRoom, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedLeaveClearsTheRoomBeforeReturningToLobby(bool timeout)
        {
            client.Room.GetType().GetProperty("UUID").SetValue(client.Room, "game-room");
            typeof(UbiqRoomSession).GetProperty("CurrentRoom").SetValue(session,
                new RoomListing("Game room", "game-room", "TEST"));
            Invoke(session, "SetState", RoomSessionState.InGame);
            Assert.That(session.LeaveRoom(), Is.True);
            var version = (int)Get(session, "operationVersion");

            var previousAPI = SceneManagerAPI.overrideAPI;
            try
            {
                // Avoid loading a real scene from an Edit Mode test.
                SceneManagerAPI.overrideAPI = new UnavailableSceneAPI();
                Fail(timeout, version, "Leave failed");

                Assert.That(Get(session, "pendingOperation").ToString(), Is.EqualTo("None"));
                Assert.That((int)Get(session, "operationVersion"), Is.GreaterThan(version));
                Assert.That(client.Room.UUID, Is.Null);
                Assert.That(session.CurrentRoom, Is.Null);
            }
            finally
            {
                SceneManagerAPI.overrideAPI = previousAPI;
            }
        }

        [TestCase("EnterInitialLobby")]
        [TestCase("Recover")]
        public void LobbyEntryWaitsForANewPrivateRoom(string operation)
        {
            Prepare(operation, RoomSessionState.Connecting);
            Set(session, "roomBeforeOperation", "previous-room");
            foreach (var room in new[]
                {
                    new TestRoom { UUID = "" },
                    new TestRoom { UUID = "previous-room" },
                    new TestRoom { UUID = "published-room", Publish = true }
                })
            {
                client.OnJoinedRoom.Invoke(room);
                Assert.That(session.State, Is.EqualTo(RoomSessionState.Connecting));
            }

            client.OnJoinedRoom.Invoke(new TestRoom { UUID = "private-room" });
            Assert.That(session.State, Is.EqualTo(RoomSessionState.LobbyReady));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AutomaticRejoinKeepsPlayerIdentityAndHostAuthority(bool isHost)
        {
            var playerId = session.LocalPeerId;
            var creator = isHost ? playerId : "remote-host";
            var properties = (PropertyCollection)Get(Get(client, "room"), "properties");
            client.Room.GetType().GetProperty("UUID").SetValue(client.Room, "game-room");
            properties.Set("meshup.creator", creator);
            properties.Set("meshup.game.started", "true");
            typeof(UbiqRoomSession).GetProperty("CurrentRoom").SetValue(session,
                new RoomListing("Game room", "game-room", "TEST"));
            Invoke(session, "SetState", RoomSessionState.InGame);
            var rejoins = 0;
            session.GameRoomRejoined += () => rejoins++;

            client.Reconnect();

            Assert.That(client.Me.uuid, Is.Not.EqualTo(playerId));
            Assert.That(client.Me["meshup.player"], Is.EqualTo(playerId));
            Assert.That(session.LocalPeerId, Is.EqualTo(playerId));
            Assert.That(session.GetParticipantIds(), Is.EqualTo(new[] { playerId }));
            Assert.That(rejoins, Is.Zero, "Resetting the connection is not a completed rejoin.");
            client.OnJoinedRoom.Invoke(new TestRoom { UUID = "another-room" });
            Assert.That(rejoins, Is.Zero, "A different room cannot resume this match.");

            client.Room.GetType().GetProperty("UUID").SetValue(client.Room, "game-room");
            properties.Set("meshup.creator", creator);
            properties.Set("meshup.game.started", "true");
            client.OnJoinedRoom.Invoke(client.Room);
            client.OnJoinedRoom.Invoke(client.Room);

            Assert.That(rejoins, Is.EqualTo(1));
            Assert.That(session.State, Is.EqualTo(RoomSessionState.InGame));
            Assert.That(session.CurrentRoom.Uuid, Is.EqualTo("game-room"));
            Assert.That(session.GameStarted, Is.True);
            Assert.That(session.IsRoomCreator, Is.EqualTo(isHost));
        }

        [Test]
        public void GuestReconnectRetainsTheRosterScoreAndGuessPermission()
        {
            AddPeer(client, "wire-old", "guest", "Guest");
            var roster = session.GetParticipantIds().ToArray();
            var match = new MeshupMatchState(new System.Random(0));
            match.Begin(session.GetParticipants(), new[] { session.LocalPeerId, "guest" });
            Assert.That(match.MimePeerId, Is.EqualTo(session.LocalPeerId));
            match.MimeEntered(match.MimePeerId, new[] { "jump", "swim" });
            match.SelectWord(match.MimePeerId, 0);
            match.StartTimer(match.MimePeerId);
            match.Players.Single(player => player.peerId == "guest").points = 2;

            RemovePeer(client, "wire-old");
            AddPeer(client, "wire-new", "guest", "Guest");

            Assert.That(session.GetParticipantIds(), Is.EqualTo(roster));
            var guest = session.GetParticipants().Single(player => player.DisplayName == "Guest");
            Assert.That(guest.PeerId, Is.EqualTo("guest"));
            Assert.That(match.SubmitGuess(guest.PeerId, "jump"), Is.True);
            Assert.That(match.Players.Single(player => player.peerId == "guest").points,
                Is.EqualTo(3));
        }

        [Test]
        public void OverlappingOldAndNewConnectionsCountAsOnePlayer()
        {
            AddPeer(client, "wire-old", "guest", "Guest");
            AddPeer(client, "wire-new", "guest", "Guest");

            Assert.That(session.ParticipantCount, Is.EqualTo(2));
            Assert.That(session.GetParticipantIds().Count, Is.EqualTo(2));
            Assert.That(session.GetParticipants().Count, Is.EqualTo(2));
        }

        [Test]
        public void RoomsUsingConnectionIdsAreNotCompatibleWithStablePlayerIds()
        {
            var room = new TestRoom
            {
                ["meshup.application"] = "meshup-game",
                ["meshup.scene"] = "GameScene",
                ["meshup.protocol"] = "1"
            };
            Assert.That(Invoke(session, "IsCompatible", room), Is.False);
            room["meshup.protocol"] = "2";
            Assert.That(Invoke(session, "IsCompatible", room), Is.True);
        }

        internal static void AddPeer(RoomClient target, string connectionId,
            string playerId, string displayName)
        {
            ReceiveRoomPacket(target, "PeerAdded", "{\"peer\":{\"uuid\":\""
                + connectionId + "\",\"keys\":[\"meshup.player\",\""
                + Ubiq.DisplayNameManager.KEY + "\"],\"values\":[\""
                + playerId + "\",\"" + displayName + "\"]}}");
        }

        internal static void RemovePeer(RoomClient target, string connectionId)
        {
            ReceiveRoomPacket(target, "PeerRemoved", "{\"uuid\":\"" + connectionId + "\"}");
        }

        private static void ReceiveRoomPacket(RoomClient target, string kind, string args)
        {
            var message = ReferenceCountedSceneGraphMessage.Rent(
                JsonUtility.ToJson(new RoomPacket { type = kind, args = args }));
            try { Invoke(target, "ProcessMessage", message); }
            finally { message.Release(); }
        }

        private void Prepare(string operation, RoomSessionState state)
        {
            var type = typeof(UbiqRoomSession).GetNestedType("PendingOperation", BindingFlags.NonPublic);
            Invoke(session, "PrepareRoomOperation", Enum.Parse(type, operation), state, null, "");
        }

        private void Fail(bool timeout, int version, string message)
        {
            if (timeout)
            {
                var routine = (IEnumerator)Invoke(session, "RoomOperationTimeout", version, message);
                Assert.That(routine.MoveNext(), Is.True);
                Assert.That(routine.MoveNext(), Is.False);
            }
            else
            {
                client.OnJoinRejected.Invoke(new Rejection { reason = message });
            }
        }

        private static object Get(object target, string field) =>
            target.GetType().GetField(field, Private | BindingFlags.Public).GetValue(target);

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Private).SetValue(target, value);

        private static object Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);
    }
}
