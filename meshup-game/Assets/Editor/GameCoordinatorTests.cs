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

namespace Meshup.Editor.Tests
{
    public sealed class GameCoordinatorTests
    {
        [Serializable]
        public sealed class Packet
        {
            public int kind;
            public string creatorPeerId, senderPeerId, targetPeerId, requestId, text;
            public int intValue;
            public string[] words;
            public MeshupMatchSnapshot snapshot;
            public MeshupGeneratedObjectState generatedObject;
        }

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
        private string Local => session.LocalPeerId;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Coordinator test");
            var networkObject = new GameObject("Test network");
            networkObject.transform.SetParent(root.transform);
            network = networkObject.AddComponent<NetworkScene>();
            roomClient = networkObject.AddComponent<RoomClient>();
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
            var effectsType = typeof(MeshupGameCoordinator).Assembly.GetType("Meshup.Game.MeshupGameSnapshotEffects");
            Set(coordinator, "snapshotEffects", Activator.CreateInstance(effectsType,
                objects, null, null, null, null, new Action<bool>(_ => { })));
            host = new MeshupMatchState(new System.Random(7));
            host.Begin(new[] { new ParticipantInfo(Local, "Local", true), new ParticipantInfo("peer", "Peer", true) });
            host.MimeEntered(host.MimePeerId, new[] { "jump", "swim" });
            Set(coordinator, "hostState", host);
        }

        [TearDown]
        public void TearDown()
        {
            objects?.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void HostPublishesLocallyBeforeSendingAndKeepsPrivateResponsesTargeted()
        {
            connection.Sent = packet =>
            {
                Assert.That(packet.creatorPeerId, Is.EqualTo(Local));
                if (packet.kind == Kind("Snapshot"))
                    Assert.That(coordinator.CurrentSnapshot.version, Is.EqualTo(packet.snapshot.version));
            };
            Invoke(coordinator, "BroadcastSnapshot");
            Assert.That(coordinator.CurrentSnapshot.phase, Is.EqualTo((int)MeshupGamePhase.ChoosingWord));
            Invoke(coordinator, "SendPrivateWords", Local, new[] { "jump", "swim" }, "");
            Assert.That(Get(coordinator, "privateWordOptions"), Is.EqualTo(new[] { "jump", "swim" }));
            Invoke(coordinator, "SendPrivateWords", "peer", Array.Empty<string>(), "swim");
            Assert.That(Get(coordinator, "privateSelectedWord"), Is.Empty);
            Invoke(coordinator, "SendGuessFeedback", Local, "jump");
            var feedback = Get(coordinator, "guessFeedback");
            Invoke(coordinator, "SendGuessFeedback", "peer", "swim");
            Assert.That(Get(coordinator, "guessFeedback"), Is.EqualTo(feedback));
            Assert.That(connection.Packets, Has.Count.EqualTo(5));
            Assert.That(connection.Packets.Last().targetPeerId, Is.EqualTo("peer"));
        }

        [Test]
        public void InboundStateRequiresTheCreatorAndIgnoresOldSnapshotsAndOtherTargets()
        {
            SetCreator("host");
            var next = new MeshupMatchSnapshot { version = 10, phase = (int)MeshupGamePhase.Preparation };
            Receive(new Packet { kind = Kind("Snapshot"), creatorPeerId = "intruder", snapshot = next });
            Assert.That(coordinator.CurrentSnapshot.version, Is.Zero);
            Receive(new Packet { kind = Kind("Snapshot"), creatorPeerId = "host", snapshot = next });
            Assert.That(coordinator.CurrentSnapshot.version, Is.EqualTo(10));
            Receive(new Packet { kind = Kind("Snapshot"), creatorPeerId = "host", snapshot = new MeshupMatchSnapshot { version = 9 } });
            Assert.That(coordinator.CurrentSnapshot.phase, Is.EqualTo((int)MeshupGamePhase.Preparation));
            Receive(new Packet { kind = Kind("PrivateWords"), creatorPeerId = "host", targetPeerId = "peer", words = new[] { "wrong" } });
            Assert.That(Get(coordinator, "privateWordOptions"), Is.Empty);
            Receive(new Packet { kind = Kind("PrivateWords"), creatorPeerId = "host", targetPeerId = Local, words = new[] { "jump", "swim" } });
            Assert.That(Get(coordinator, "privateWordOptions"), Is.EqualTo(new[] { "jump", "swim" }));
            Receive(new Packet { kind = Kind("SelectWord"), senderPeerId = host.MimePeerId, intValue = 0 });
            Assert.That(host.Phase, Is.EqualTo(MeshupGamePhase.ChoosingWord), "Peers do not execute host commands.");
            Assert.That(connection.Packets, Is.Empty);
        }

        [Test]
        public void HostRoutesCommandsAndEnforcesTheMimePermission()
        {
            Receive(new Packet { kind = Kind("SelectWord"), senderPeerId = "intruder", intValue = 0 });
            Assert.That(host.Phase, Is.EqualTo(MeshupGamePhase.ChoosingWord));
            Receive(new Packet { kind = Kind("SelectWord"), senderPeerId = host.MimePeerId, intValue = 0 });
            Assert.That(host.Phase, Is.EqualTo(MeshupGamePhase.Preparation));
            Assert.That(coordinator.CurrentSnapshot.phase, Is.EqualTo((int)MeshupGamePhase.Preparation));
            Assert.That(connection.Packets.Select(packet => packet.kind),
                Is.EqualTo(new[] { Kind("PrivateWords"), Kind("Snapshot") }));
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
            grab.selectEntered.Invoke(new SelectEnterEventArgs { interactableObject = grab });
            objectGame.transform.position = Vector3.right;
            Invoke(generated, "Update");
            Assert.That(connection.Packets, Has.Count.EqualTo(1));
            objectGame.transform.position = new Vector3(4f, 5f, 6f);
            objectGame.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            grab.selectExited.Invoke(new SelectExitEventArgs { interactableObject = grab });
            Assert.That(connection.Packets, Has.Count.EqualTo(2));
            var last = connection.Packets.Last();
            Assert.That(last.kind, Is.EqualTo(Kind("ObjectTransform")));
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
            Receive(new Packet { kind = Kind("ObjectTransform"), creatorPeerId = "intruder", generatedObject = moved });
            Assert.That(instance.transform.position, Is.EqualTo(originalPosition));
            Receive(new Packet { kind = Kind("ObjectTransform"), creatorPeerId = "host", generatedObject = moved });
            Assert.That(instance.transform.position, Is.EqualTo(moved.position));
            Assert.That(connection.Packets, Is.Empty, "Applying authoritative poses must not echo them.");

            SetCreator(Local);
            moved.position = Vector3.right;
            Receive(new Packet { kind = Kind("ObjectTransform"), senderPeerId = "intruder", generatedObject = moved });
            Assert.That(connection.Packets, Is.Empty);
            Receive(new Packet { kind = Kind("ObjectTransform"), senderPeerId = host.MimePeerId, generatedObject = moved });
            Assert.That(state.position, Is.EqualTo(moved.position));
            Assert.That(instance.transform.position, Is.EqualTo(moved.position));
            Assert.That(connection.Packets, Has.Count.EqualTo(1));
            Assert.That(connection.Packets[0].creatorPeerId, Is.EqualTo(Local));
        }

        [Test]
        public void GenerationAuthorizationPublishesThePendingStateAndTargetsTheMime()
        {
            host.SelectWord(host.MimePeerId, 0);
            Receive(new Packet { kind = Kind("GenerationRequest"), senderPeerId = "intruder", requestId = "denied" });
            Assert.That(connection.Packets, Is.Empty);
            Receive(new Packet { kind = Kind("GenerationRequest"), senderPeerId = host.MimePeerId, requestId = "request" });
            Assert.That(host.GenerationPending, Is.True);
            Assert.That(coordinator.CurrentSnapshot.generationPending, Is.True);
            Assert.That(connection.Packets.Select(packet => packet.kind),
                Is.EqualTo(new[] { Kind("Snapshot"), Kind("GenerationAuthorized") }));
            Assert.That(connection.Packets.Last().targetPeerId, Is.EqualTo(host.MimePeerId));
            Assert.That(connection.Packets.Last().requestId, Is.EqualTo("request"));
            Assert.That(connection.Packets.All(packet => packet.creatorPeerId == Local), Is.True);
        }

        [Test]
        public void TeardownRemovesTheRegisteredProcessor()
        {
            Assert.That(network.GetProcessors().Count(pair => pair.Key.Target == coordinator), Is.EqualTo(1));
            Invoke(coordinator, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(coordinator);
            Assert.That(network.GetProcessors().Count(), Is.Zero);
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

        private void SetCreator(string creator) => ((PropertyCollection)Get(Get(roomClient, "room"), "properties"))
            .Set("meshup.creator", creator);

        private void Receive(Packet packet)
        {
            var message = ReferenceCountedSceneGraphMessage.Rent(JsonUtility.ToJson(packet));
            try { coordinator.ProcessMessage(message); }
            finally { message.Release(); }
        }

        private static int Kind(string name) => (int)Enum.Parse(typeof(MeshupGameCoordinator)
            .GetNestedType("MessageKind", BindingFlags.NonPublic), name);
        private static object Get(object target, string field) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string method, params object[] arguments) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
    }
}
