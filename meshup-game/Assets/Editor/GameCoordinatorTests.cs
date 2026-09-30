using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Meshup.EditorTools;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using Ubiq.Dictionaries;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.UI;
using Packet = Meshup.Game.MeshupGameCoordinator.GameMessage;
using MessageKind = Meshup.Game.MeshupGameCoordinator.MessageKind;

namespace Meshup.Editor.Tests
{
    public sealed class GameCoordinatorTests
    {
        private sealed class CaptureConnection : INetworkConnection
        {
            public readonly List<Packet> Packets = new();
            public Action<Packet> Sent;
            public ReferenceCountedMessage Receive() => null;
            public void Dispose() { }
            public void Send(ReferenceCountedMessage message)
            {
                try
                {
                    var packet = new ReferenceCountedSceneGraphMessage(message).FromJson<Packet>();
                    Packets.Add(packet);
                    Sent?.Invoke(packet);
                }
                finally { message.Release(); }
            }
        }

        private GameObject root;
        private MeshupGameCoordinator coordinator;
        private UbiqRoomSession session;
        private RoomClient roomClient;
        private NetworkScene network;
        private CaptureConnection connection;
        private GeneratedObjectManager objects;
        private MeshupGeneratedObject imported;
        private MeshupMatchState host;
        private Button firstChoice;
        private Text terminalTitle;
        private Text status;
        private CursorLockMode originalCursorLock;
        private bool originalCursorVisible;
        private string Local => session.LocalPeerId;

        [SetUp]
        public void SetUp()
        {
            originalCursorLock = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            root = new GameObject("Coordinator test");
            var networkObject = new GameObject("Test network");
            networkObject.transform.SetParent(root.transform);
            network = networkObject.AddComponent<NetworkScene>();
            roomClient = networkObject.AddComponent<RoomClient>();
            Set(roomClient, "scene", network);
            Set(roomClient, "servers", Array.Empty<ConnectionDefinition>());
            session = networkObject.AddComponent<UbiqRoomSession>();
            Set(session, "roomClient", roomClient);
            SetCreator(Local);
            var owner = new GameObject("Test coordinator");
            owner.transform.SetParent(networkObject.transform);
            coordinator = owner.AddComponent<MeshupGameCoordinator>();
            Set(coordinator, "session", session);
            Set(coordinator, "networkContext", NetworkScene.Register(coordinator));
            connection = new CaptureConnection();
            network.AddConnection(connection);
            objects = new GeneratedObjectManager(owner.transform,
                (instance, state, cancellation) =>
                {
                    imported = instance;
                    instance.ApplyState(state, true);
                    return Task.FromResult(true);
                }, message => Assert.Fail(message));
            Set(coordinator, "generatedObjects", objects);
            Set(coordinator, "snapshotEffects", new MeshupGameSnapshotEffects(
                objects, null, null, null, null, _ => { }));
            host = new MeshupMatchState(new System.Random(0));
            host.Begin(new[] { new ParticipantInfo(Local, "Local", true), new ParticipantInfo("peer", "Peer", true) });
            host.MimeEntered(host.MimePeerId, new[] { "jump", "swim" });
            Set(coordinator, "hostState", host);
            var view = owner.AddComponent<MeshupGameView>();
            GameUiTests.MountPrefabs(view, owner.transform, owner.transform);
            view.Configure(index => Receive(new Packet
                { kind = (int)MessageKind.SelectWord, senderPeerId = Local, intValue = index }),
                () => Receive(new Packet { kind = (int)MessageKind.StartTimer, senderPeerId = Local }));
            Set(coordinator, "view", view);
            var ui = new SerializedObject(view);
            firstChoice = (Button)ui.FindProperty("firstChoice").objectReferenceValue;
            terminalTitle = (Text)ui.FindProperty("terminalTitle").objectReferenceValue;
            status = (Text)ui.FindProperty("status").objectReferenceValue;
        }

        [TearDown]
        public void TearDown()
        {
            objects?.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            Cursor.lockState = originalCursorLock;
            Cursor.visible = originalCursorVisible;
        }

        [Test]
        public void HostAppliesSnapshotsLocallyBeforeSendingAndRendersItsPrivateWord()
        {
            connection.Sent = packet =>
            {
                Assert.That(packet.creatorPeerId, Is.EqualTo(Local));
                if (packet.kind == (int)MessageKind.Snapshot)
                    Assert.That(coordinator.CurrentSnapshot.version, Is.EqualTo(packet.snapshot.version));
            };
            Receive(new Packet { kind = (int)MessageKind.RequestSnapshot, senderPeerId = Local });
            Assert.That(firstChoice.gameObject.activeSelf, Is.True);
            Assert.That(firstChoice.GetComponentInChildren<Text>().text, Is.EqualTo("jump"));
            firstChoice.onClick.Invoke();
            Assert.That(coordinator.CurrentSnapshot.phase, Is.EqualTo((int)MeshupGamePhase.Preparation));
            Assert.That(terminalTitle.text, Is.EqualTo("jump"));
            Assert.That(connection.Packets, Has.Count.EqualTo(4));
        }

        [Test]
        public void InboundStateRequiresTheCreatorAndIgnoresOldSnapshotsAndOtherTargets()
        {
            SetCreator("host");
            var next = new MeshupMatchSnapshot
                { version = 10, phase = (int)MeshupGamePhase.ChoosingWord, mimePeerId = Local };
            Receive(new Packet { kind = (int)MessageKind.Snapshot, creatorPeerId = "intruder", snapshot = next });
            Assert.That(coordinator.CurrentSnapshot.version, Is.Zero);
            Receive(new Packet { kind = (int)MessageKind.Snapshot, creatorPeerId = "host", snapshot = next });
            Assert.That(coordinator.CurrentSnapshot.version, Is.EqualTo(10));
            Receive(new Packet { kind = (int)MessageKind.Snapshot, creatorPeerId = "host", snapshot = new MeshupMatchSnapshot { version = 9 } });
            Assert.That(coordinator.CurrentSnapshot.phase, Is.EqualTo((int)MeshupGamePhase.ChoosingWord));
            Receive(new Packet { kind = (int)MessageKind.PrivateWords, creatorPeerId = "host", targetPeerId = "peer", words = new[] { "wrong", "words" } });
            Assert.That(firstChoice.gameObject.activeSelf, Is.False);
            Receive(new Packet { kind = (int)MessageKind.PrivateWords, creatorPeerId = "host", targetPeerId = Local, words = new[] { "jump", "swim" } });
            Assert.That(firstChoice.gameObject.activeSelf, Is.True);
            Assert.That(firstChoice.GetComponentInChildren<Text>().text, Is.EqualTo("jump"));
            Receive(new Packet { kind = (int)MessageKind.SelectWord, senderPeerId = host.MimePeerId, intValue = 0 });
            Assert.That(host.Phase, Is.EqualTo(MeshupGamePhase.ChoosingWord), "Peers do not execute host commands.");
            Assert.That(connection.Packets, Is.Empty);
        }

        [Test]
        public void PrivateWordsAndGuessFeedbackOnlyChangeTheTargetPlayersDisplay()
        {
            SetCreator("host");
            Receive(new Packet
            {
                kind = (int)MessageKind.Snapshot, creatorPeerId = "host",
                snapshot = new MeshupMatchSnapshot
                    { version = 10, phase = (int)MeshupGamePhase.Preparation, mimePeerId = Local }
            });
            Receive(new Packet { kind = (int)MessageKind.PrivateWords, creatorPeerId = "host", targetPeerId = "peer", text = "swim" });
            Assert.That(terminalTitle.text, Is.Empty);
            Receive(new Packet { kind = (int)MessageKind.PrivateWords, creatorPeerId = "host", targetPeerId = Local, text = "jump" });
            Assert.That(terminalTitle.text, Is.EqualTo("jump"));
            Receive(new Packet
            {
                kind = (int)MessageKind.Snapshot, creatorPeerId = "host",
                snapshot = new MeshupMatchSnapshot
                    { version = 11, phase = (int)MeshupGamePhase.TimedGuessing, mimePeerId = "peer" }
            });
            Receive(new Packet { kind = (int)MessageKind.GuessFeedback, creatorPeerId = "host", targetPeerId = Local, text = "jump is incorrect" });
            Assert.That(status.text, Does.Contain("jump is incorrect"));
            Receive(new Packet { kind = (int)MessageKind.GuessFeedback, creatorPeerId = "host", targetPeerId = "peer", text = "swim is incorrect" });
            Assert.That(status.text, Does.Contain("jump is incorrect"));
            Assert.That(status.text, Does.Not.Contain("swim is incorrect"));
        }

        [Test]
        public void HostRoutesCommandsAndEnforcesTheMimePermission()
        {
            Receive(new Packet { kind = (int)MessageKind.SelectWord, senderPeerId = "intruder", intValue = 0 });
            Assert.That(host.Phase, Is.EqualTo(MeshupGamePhase.ChoosingWord));
            Receive(new Packet { kind = (int)MessageKind.SelectWord, senderPeerId = host.MimePeerId, intValue = 0 });
            Assert.That(host.Phase, Is.EqualTo(MeshupGamePhase.Preparation));
            Assert.That(coordinator.CurrentSnapshot.phase, Is.EqualTo((int)MeshupGamePhase.Preparation));
            Assert.That(connection.Packets.Select(packet => packet.kind),
                Is.EqualTo(new[] { (int)MessageKind.PrivateWords, (int)MessageKind.Snapshot }));
        }

        [Test]
        public void ObjectPosesStillSendWhileHeldAndImmediatelyOnXrRelease()
        {
            SetCreator("host");
            Set(coordinator, "snapshot", new MeshupMatchSnapshot
                { mimePeerId = Local, phase = (int)MeshupGamePhase.Preparation });
            var objectGame = new GameObject("Test held object");
            objectGame.transform.SetParent(root.transform);
            var generated = objectGame.AddComponent<MeshupGeneratedObject>();
            Set(generated, "coordinator", coordinator);
            Set(generated, "objectId", "object");
            Invoke(generated, "AddInteractionComponentsForBounds", new object[] { null });
            var grab = objectGame.GetComponent<XRGrabInteractable>();
            var body = objectGame.GetComponent<Rigidbody>();
            Assert.That(grab.useDynamicAttach && grab.matchAttachPosition
                && grab.matchAttachRotation && grab.trackRotation, Is.True);
            Assert.That(grab.movementType, Is.EqualTo(XRBaseInteractable.MovementType.VelocityTracking));
            Assert.That(grab.throwOnDetach, Is.False);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
            Assert.That(body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
            grab.selectEntered.Invoke(new SelectEnterEventArgs { interactableObject = grab });
            objectGame.transform.position = Vector3.right;
            Invoke(generated, "Update");
            Assert.That(connection.Packets, Has.Count.EqualTo(1));
            objectGame.transform.position = new Vector3(4f, 5f, 6f);
            objectGame.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            grab.selectExited.Invoke(new SelectExitEventArgs { interactableObject = grab });
            Assert.That(connection.Packets, Has.Count.EqualTo(2));
            var last = connection.Packets.Last();
            Assert.That(last.kind, Is.EqualTo((int)MessageKind.ObjectTransform));
            Assert.That(last.senderPeerId, Is.EqualTo(Local));
            Assert.That(last.generatedObject.position, Is.EqualTo(objectGame.transform.position));
            Assert.That(Quaternion.Angle(last.generatedObject.rotation, objectGame.transform.rotation), Is.LessThan(0.001f));
            Invoke(generated, "Update");
            grab.selectExited.Invoke(new SelectExitEventArgs { interactableObject = grab });
            Assert.That(connection.Packets, Has.Count.EqualTo(2), "Release is sent once even before the next periodic send.");
        }

        [Test]
        public void TransformsRequireTheCreatorOnPeersAndTheMimeOnTheHost()
        {
            var state = objects.Add("test.glb");
            objects.Reconcile(objects.States);
            var instance = imported;
            Assert.That(instance, Is.Not.Null);
            var originalPosition = instance.transform.position;
            var moved = new MeshupGeneratedObjectState
                { objectId = state.objectId, position = new Vector3(4f, 5f, 6f), rotation = Quaternion.identity };
            SetCreator("host");
            Receive(new Packet { kind = (int)MessageKind.ObjectTransform, creatorPeerId = "intruder", generatedObject = moved });
            Assert.That(instance.transform.position, Is.EqualTo(originalPosition));
            Receive(new Packet { kind = (int)MessageKind.ObjectTransform, creatorPeerId = "host", generatedObject = moved });
            Assert.That(instance.transform.position, Is.EqualTo(moved.position));
            Assert.That(connection.Packets, Is.Empty, "Applying authoritative poses must not echo them.");

            SetCreator(Local);
            moved.position = Vector3.right;
            Receive(new Packet { kind = (int)MessageKind.ObjectTransform, senderPeerId = "intruder", generatedObject = moved });
            Assert.That(connection.Packets, Is.Empty);
            Receive(new Packet { kind = (int)MessageKind.ObjectTransform, senderPeerId = host.MimePeerId, generatedObject = moved });
            Assert.That(state.position, Is.EqualTo(moved.position));
            Assert.That(instance.transform.position, Is.EqualTo(moved.position));
            Assert.That(connection.Packets, Has.Count.EqualTo(1));
            Assert.That(connection.Packets[0].creatorPeerId, Is.EqualTo(Local));
        }

        [Test]
        public void GenerationAuthorizationPublishesThePendingStateAndTargetsTheMime()
        {
            host.SelectWord(host.MimePeerId, 0);
            Receive(new Packet { kind = (int)MessageKind.GenerationRequest, senderPeerId = "intruder", requestId = "denied" });
            Assert.That(connection.Packets, Is.Empty);
            Receive(new Packet { kind = (int)MessageKind.GenerationRequest, senderPeerId = host.MimePeerId, requestId = "request" });
            Assert.That(host.GenerationPending, Is.True);
            Assert.That(coordinator.CurrentSnapshot.generationPending, Is.True);
            Assert.That(connection.Packets.Select(packet => packet.kind),
                Is.EqualTo(new[] { (int)MessageKind.Snapshot, (int)MessageKind.GenerationAuthorized }));
            Assert.That(connection.Packets.Last().targetPeerId, Is.EqualTo(host.MimePeerId));
            Assert.That(connection.Packets.Last().requestId, Is.EqualTo("request"));
            Assert.That(connection.Packets.All(packet => packet.creatorPeerId == Local), Is.True);
        }

        [Test]
        public void AuthoredComponentsAndDevelopmentPreviewsKeepTheExistingOwners()
        {
            using var scope = new SceneValidationScope("Assets/Scenes/GameScene.unity");
            MeshupGameSceneValidation.Validate(scope.Scene);
            var runtime = scope.Scene.GetRootGameObjects().SelectMany(go =>
                go.GetComponentsInChildren<MeshupGameCoordinator>(true)).Single();
            var data = new SerializedObject(runtime);
            foreach (var field in new[] { "victoryFireworks", "transcriber", "sizeSelector", "generatorClient" })
            {
                var component = (Component)data.FindProperty(field).objectReferenceValue;
                Assert.That(component.gameObject.GetComponents(component.GetType()), Has.Length.EqualTo(1));
            }
            var preview = runtime.GetComponent<MeshupDevelopmentShortcuts>();
            Assert.That(preview, Is.Not.Null);
            Assert.That(Get(preview, "coordinator"), Is.SameAs(runtime));
            var audio = (GeneratorActivityAudio)data.FindProperty("generatorActivityAudio").objectReferenceValue;
            Assert.That(Get(preview, "generatorActivityAudio"), Is.SameAs(audio));
            Assert.That(Get(preview, "victoryFireworks"), Is.SameAs(data.FindProperty("victoryFireworks").objectReferenceValue));
            Assert.That(Get(preview, "generatorParticles"), Is.SameAs(data.FindProperty("generatorParticles").objectReferenceValue));
            Invoke(preview, "ToggleGeneratorAudioPreview");
            Assert.That(audio.IsGenerating, Is.True);
            Invoke(preview, "ToggleGeneratorAudioPreview");
            Assert.That(audio.IsGenerating, Is.False);
            runtime.CurrentSnapshot.generationPending = true;
            Invoke(preview, "ToggleGeneratorAudioPreview");
            Invoke(preview, "PreviewGeneratorFailure");
            Assert.That(audio.IsGenerating, Is.True, "Previews preserve a real pending generation.");
            runtime.CurrentSnapshot.generationPending = false;
            Invoke(preview, "PreviewGeneratorFailure");
            Assert.That(audio.IsGenerating, Is.False);
        }

        [Test]
        public void RejoinedClientRequestsAMatchSnapshotWithItsStableIdentity()
        {
            roomClient.Me["meshup.player"] = Local;
            var playerId = Local;
            SetCreator("host");
            roomClient.Reconnect();
            SetCreator("host");
            network.AddConnection(connection);

            Invoke(coordinator, "HandleGameRoomRejoined");

            Assert.That(connection.Packets, Has.Count.EqualTo(1));
            Assert.That(connection.Packets[0].kind, Is.EqualTo((int)MessageKind.RequestSnapshot));
            Assert.That(connection.Packets[0].senderPeerId, Is.EqualTo(playerId));
        }

        [Test]
        public void RejoinedHostRepublishesTheExistingRoundAndRetainsMimeAuthority()
        {
            roomClient.Me["meshup.player"] = Local;
            var playerId = Local;
            host = new MeshupMatchState(new System.Random(0));
            host.Begin(new[] { new ParticipantInfo(Local, "Local", true),
                new ParticipantInfo("peer", "Peer", true) }, new[] { Local, "peer" });
            host.MimeEntered(Local, new[] { "jump", "swim" });
            host.SelectWord(Local, 0);
            host.Players.Single(player => player.peerId == Local).points = 2;
            Set(coordinator, "hostState", host);
            roomClient.Reconnect();
            SetCreator(Local);
            network.AddConnection(connection);

            Invoke(coordinator, "HandleGameRoomRejoined");

            Assert.That(session.IsRoomCreator, Is.True);
            Assert.That(host.MimePeerId, Is.EqualTo(playerId));
            Assert.That(host.Phase, Is.EqualTo(MeshupGamePhase.Preparation));
            Assert.That(host.RoundNumber, Is.EqualTo(1));
            Assert.That(host.Players.Single(player => player.peerId == playerId).points, Is.EqualTo(2));
            Assert.That(connection.Packets.Select(packet => packet.kind),
                Is.EqualTo(new[] { (int)MessageKind.Snapshot, (int)MessageKind.PrivateWords }));
            Assert.That(connection.Packets.All(packet => packet.creatorPeerId == playerId), Is.True);
            Assert.That(connection.Packets.Last().targetPeerId, Is.EqualTo(playerId));
            Assert.That(connection.Packets.Last().text, Is.EqualTo("jump"));
            Assert.That(terminalTitle.text, Is.EqualTo("jump"));

            Receive(new Packet { kind = (int)MessageKind.StartTimer, senderPeerId = Local });
            Assert.That(host.Phase, Is.EqualTo(MeshupGamePhase.TimedGuessing));
        }

        [Test]
        public void ReturningRosterPlayerCanGuessAfterDepartureWasConfirmed()
        {
            host = new MeshupMatchState(new System.Random(0));
            host.Begin(new[] { new ParticipantInfo(Local, "Local", true),
                new ParticipantInfo("peer", "Peer", true) }, new[] { Local, "peer" });
            host.MimeEntered(Local, new[] { "jump", "swim" });
            host.SelectWord(Local, 0);
            host.StartTimer(Local);
            host.Players.Single(player => player.peerId == "peer").points = 2;
            host.Disconnect("peer");
            Set(coordinator, "hostState", host);

            RoomSessionTests.AddPeer(roomClient, "new-connection", "peer", "Peer");
            Invoke(coordinator, "HandleParticipantsChanged");
            Assert.That(coordinator.CurrentSnapshot.scores.Single(player => player.peerId == "peer").connected,
                Is.True);
            Receive(new Packet { kind = (int)MessageKind.Guess, senderPeerId = "peer", text = "jump" });

            Assert.That(host.Phase, Is.EqualTo(MeshupGamePhase.Result));
            Assert.That(host.Players.Single(player => player.peerId == "peer").points, Is.EqualTo(3));
        }

        private void SetCreator(string creator) => ((PropertyCollection)Get(Get(roomClient, "room"), "properties"))
            .Set("meshup.creator", creator);

        private void Receive(Packet packet)
        {
            var message = ReferenceCountedSceneGraphMessage.Rent(JsonUtility.ToJson(packet));
            try { coordinator.ProcessMessage(message); }
            finally { message.Release(); }
        }

        private static object Get(object target, string field) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string method, params object[] arguments) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
    }
}
