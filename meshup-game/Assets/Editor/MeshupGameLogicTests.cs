using System;
using System.Linq;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Ubiq.Voip;

namespace Meshup.Editor.Tests
{
    public sealed class MeshupGameLogicTests
    {
        [Test]
        public void EveryConnectedPlayerMimesOnceAndScoresAtomically()
        {
            var state = CreateState();
            var seenMimes = new System.Collections.Generic.HashSet<string>();
            while (state.Phase != MeshupGamePhase.Finished)
            {
                var mime = state.MimePeerId;
                Assert.That(seenMimes.Add(mime), Is.True);
                var guesser = state.Players.First(item =>
                    item.peerId != mime && item.connected).peerId;
                BeginTimedRound(state, "jump");
                Assert.That(state.SubmitGuess(guesser, "  JUMP! "), Is.True);
                Assert.That(state.SubmitGuess(guesser, "jump"), Is.False);
                Assert.That(state.MimeExited(mime), Is.True);
            }

            Assert.That(seenMimes, Has.Count.EqualTo(3));
            Assert.That(state.Players.Sum(item => item.points), Is.EqualTo(6));
        }

        [Test]
        public void PreparationRejectsGuessesAndStartWaitsForGeneration()
        {
            var state = CreateState();
            var mime = state.MimePeerId;
            var guesser = state.Players.First(item => item.peerId != mime).peerId;
            Assert.That(state.MimeEntered(mime, new[] { "jump", "swim" }),
                Is.True);
            Assert.That(state.SelectWord(mime, 0), Is.True);
            Assert.That(state.SubmitGuess(guesser, "jump"), Is.False);
            Assert.That(state.TryBeginGeneration(mime), Is.True);
            Assert.That(state.GenerationTokens, Is.EqualTo(2));
            Assert.That(state.StartTimer(mime), Is.False);
            Assert.That(state.EndGeneration(), Is.True);
            Assert.That(state.StartTimer(mime), Is.True);
        }

        [Test]
        public void HintsReachFiftyPercentAtNinetySecondsThenStop()
        {
            var state = CreateState();
            BeginTimedRound(state, "high-five");
            state.Tick(89.9f);
            Assert.That(state.MaskedWord.Count(char.IsLetter), Is.EqualTo(3));
            state.Tick(0.1f);
            Assert.That(state.MaskedWord.Count(char.IsLetter), Is.EqualTo(4));
            var mask = state.MaskedWord;
            state.Tick(29.5f);
            Assert.That(state.MaskedWord, Is.EqualTo(mask));
            state.Tick(0.5f);
            Assert.That(state.Phase, Is.EqualTo(MeshupGamePhase.Result));
            Assert.That(state.ResultMessage, Is.EqualTo("Time's up"));
            Assert.That(state.ResultWord, Is.EqualTo("high-five"));
        }

        [TestCase(" JUMP! ", "jump")]
        [TestCase("...High   Five?", "high five")]
        [TestCase("jump now", "jump now")]
        [TestCase("!!!", "")]
        public void GuessNormalizationIsExactAndPredictable(string input,
            string expected)
        {
            Assert.That(MeshupMatchState.NormalizeGuess(input),
                Is.EqualTo(expected));
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

        [Test]
        public void LobbyContainsRoomScopedVoipManager()
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var session = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    UbiqRoomSession>(true))
                .Single();

            Assert.That(session.GetComponentInChildren<
                VoipPeerConnectionManager>(true), Is.Not.Null);
        }

        [Test]
        public void WindowsStoreBuildDeclaresMicrophoneCapability()
        {
            Assert.That(UnityEditor.PlayerSettings.WSA.GetCapability(
                UnityEditor.PlayerSettings.WSACapability.Microphone), Is.True);
        }

        [Test]
        public void SharedVoiceCaptureReportsFramesAndDuration()
        {
            var capture = new VoiceCapture(new float[32000], 2, 16000);

            Assert.That(capture.SampleFrames, Is.EqualTo(16000));
            Assert.That(capture.DurationSeconds, Is.EqualTo(1f));
            Assert.That(capture.HasAudio, Is.True);
        }

        [Test]
        public void PcmWavEncodingProducesMonoSixteenBitHeader()
        {
            var wav = MeshupAssetGeneratorClient.EncodeWav(
                new short[] { short.MinValue, 0, short.MaxValue }, 16000);

            Assert.That(System.Text.Encoding.ASCII.GetString(wav, 0, 4),
                Is.EqualTo("RIFF"));
            Assert.That(System.Text.Encoding.ASCII.GetString(wav, 8, 4),
                Is.EqualTo("WAVE"));
            Assert.That(BitConverter.ToInt16(wav, 22), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(16000));
            Assert.That(BitConverter.ToInt16(wav, 34), Is.EqualTo(16));
            Assert.That(BitConverter.ToInt32(wav, 40), Is.EqualTo(6));
        }

        [Test]
        public void GeneratorButtonLoadsSpatialPressAndReleaseSounds()
        {
            var button = new GameObject("Generator audio test");
            try
            {
                var client = button.AddComponent<MeshupAssetGeneratorClient>();
                client.Configure(null);
                var source = button.GetComponent<AudioSource>();
                Assert.That(source, Is.Not.Null);
                Assert.That(source.playOnAwake, Is.False);
                Assert.That(source.spatialBlend, Is.EqualTo(1f));

                var flags = System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic;
                Assert.That(typeof(MeshupAssetGeneratorClient)
                    .GetField("pressClip", flags)?.GetValue(client),
                    Is.EqualTo(Resources.Load<AudioClip>(
                        "GenerateButtonPress")));
                Assert.That(typeof(MeshupAssetGeneratorClient)
                    .GetField("releaseClip", flags)?.GetValue(client),
                    Is.EqualTo(Resources.Load<AudioClip>(
                        "GenerateButtonRelease")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(button);
            }
        }

        [Test]
        public void DisconnectRetainsScoreAndSkipsFutureMimeTurn()
        {
            var state = CreateState();
            var disconnected = state.Players.First(item =>
                item.peerId != state.MimePeerId);
            disconnected.points = 2;
            Assert.That(state.Disconnect(disconnected.peerId), Is.True);
            while (state.Phase != MeshupGamePhase.Finished)
            {
                Assert.That(state.MimePeerId, Is.Not.EqualTo(disconnected.peerId));
                var mime = state.MimePeerId;
                Assert.That(state.MimeEntered(mime,
                    new[] { "jump", "swim" }), Is.True);
                Assert.That(state.SelectWord(mime, 0), Is.True);
                Assert.That(state.StartTimer(mime), Is.True);
                state.Tick(120f);
                state.MimeExited(mime);
            }
            Assert.That(disconnected.points, Is.EqualTo(2));
            Assert.That(disconnected.connected, Is.False);
        }

        [Test]
        public void MimeWordServiceReturnsDistinctVerbs()
        {
            var service = MimeWordService.LoadDefault();
            var choices = service.GetDistinctRandomVerbs(2,
                new System.Random(42));
            Assert.That(choices, Has.Length.EqualTo(2));
            Assert.That(choices[0], Is.Not.EqualTo(choices[1]));
            Assert.That(service.Verbs, Does.Contain(choices[0]));
            Assert.That(service.Verbs, Does.Contain(choices[1]));
        }

        [Test]
        public void SnapshotRetainsEveryGeneratedObjectInTheRound()
        {
            var state = CreateState();
            var generated = new[]
            {
                new MeshupGeneratedObjectState
                {
                    objectId = "first",
                    url = "http://server/first.glb"
                },
                new MeshupGeneratedObjectState
                {
                    objectId = "second",
                    url = "http://server/second.glb"
                }
            };

            var snapshot = state.CreateSnapshot(generated);

            Assert.That(snapshot.generatedObjects.Select(item => item.objectId),
                Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void GeneratedObjectsSpawnCenteredAndWellAboveTheGenerator()
        {
            var anchor = new GameObject("Generator Anchor");
            try
            {
                anchor.transform.SetPositionAndRotation(
                    new Vector3(4f, 1f, -3f),
                    Quaternion.Euler(90f, 35f, 20f));
                using var objects = new GeneratedObjectManager(anchor.transform,
                    (_, _, _) => throw new InvalidOperationException(
                        "Adding host state must not start a local import."), null);
                var first = objects.Add("https://example.test/first.glb");
                objects.Add("https://example.test/second.glb");
                var later = objects.Add("https://example.test/third.glb");
                Assert.That(first.position, Is.EqualTo(new Vector3(4f, 3.5f, -3f)));
                Assert.That(later.position, Is.EqualTo(first.position),
                    "Spawn slots must remain centered on the generator.");
                Assert.That(first.rotation, Is.EqualTo(Quaternion.identity));
                Assert.That(first.scale, Is.EqualTo(Vector3.one));
                Assert.That(objects.States.Select(item => item.objectId).Distinct().Count(),
                    Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(anchor);
            }
        }

        [TestCase(4f, 10f, 2f, 1f, 0.5f, 2f)]
        [TestCase(10f, 4f, 0.5f, 2f, 1f, 2f)]
        [TestCase(4f, 10f, 0.5f, 1f, 2f, 2f)]
        public void GeneratedObjectNormalizationStopsAtFirstDimensionLimit(
            float maxWidth, float maxHeight, float width, float height,
            float depth, float expectedScale)
        {
            Assert.That(MeshupGeneratedObject.CalculateNormalizationScale(
                maxWidth, maxHeight, new Vector3(width, height, depth)),
                Is.EqualTo(expectedScale)
                .Within(0.0001f));
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(-1f, -2f, -3f)]
        public void GeneratedObjectNormalizationFallsBackForInvalidBounds(
            float width, float height, float depth)
        {
            Assert.That(MeshupGeneratedObject.CalculateNormalizationScale(
                GeneratedObjectSizes.MediumMaxWidth,
                GeneratedObjectSizes.MediumMaxHeight,
                new Vector3(width, height, depth)), Is.EqualTo(1f));
        }

        [Test]
        public void SizeSelectorDefaultsToMediumAndHonorsPermissions()
        {
            var owner = new GameObject("Size selector test");
            var model = new GameObject("Size selector model");
            var small = SizeButton("SmallButton");
            var medium = SizeButton("MediumButton");
            var extraLarge = SizeButton("LargeButton");
            small.transform.SetParent(model.transform);
            medium.transform.SetParent(model.transform);
            extraLarge.transform.SetParent(model.transform);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var buttonMaterials = Enumerable.Range(0, 3).Select(index =>
                new Material(shader) { name = index == 0 ? "Button"
                    : $"Button.00{index}" }).ToArray();
            var extraLargePhysical = PhysicalSizeButton(model.transform,
                "XL Physical Button", -1f, buttonMaterials[0]);
            var mediumPhysical = PhysicalSizeButton(model.transform,
                "Medium Physical Button", 0f, buttonMaterials[1]);
            var smallPhysical = PhysicalSizeButton(model.transform,
                "Small Physical Button", 1f, buttonMaterials[2]);
            var allowed = false;
            try
            {
                var selector = owner.AddComponent<GeneratedObjectSizeSelector>();
                selector.Configure(small, medium, extraLarge, () => allowed);
                Assert.That(small.GetComponent<BoxCollider>().enabled, Is.False,
                    "The letter itself must not remain a hit target.");
                Assert.That(smallPhysical.GetComponent<XRSimpleInteractable>(),
                    Is.Not.Null);
                Assert.That(mediumPhysical.GetComponent<XRSimpleInteractable>(),
                    Is.Not.Null);
                Assert.That(extraLargePhysical.GetComponent<XRSimpleInteractable>(),
                    Is.Not.Null);
                var sizeButtonAudio = smallPhysical.GetComponent<AudioSource>();
                Assert.That(sizeButtonAudio, Is.Not.Null);
                Assert.That(sizeButtonAudio.playOnAwake, Is.False);
                Assert.That(sizeButtonAudio.spatialBlend, Is.EqualTo(1f));
                Assert.That(sizeButtonAudio.clip, Is.EqualTo(
                    Resources.Load<AudioClip>("SizeButtonPress")));
                selector.SetInteractable(true);
                Assert.That(selector.SelectedSize,
                    Is.EqualTo(GeneratedObjectSize.Medium));
                Assert.That(mediumPhysical.GetComponentInChildren<Light>().enabled,
                    Is.True);
                Assert.That(smallPhysical.GetComponentInChildren<Light>().enabled,
                    Is.False);
                Assert.That(selector.TrySelect(GeneratedObjectSize.Small), Is.False);
                allowed = true;
                Assert.That(selector.TrySelect(GeneratedObjectSize.ExtraLarge),
                    Is.True);
                Assert.That(selector.SelectedSize,
                    Is.EqualTo(GeneratedObjectSize.ExtraLarge));
                Assert.That(extraLargePhysical.GetComponentInChildren<Light>().enabled,
                    Is.True);
                Assert.That(mediumPhysical.GetComponentInChildren<Light>().enabled,
                    Is.False);
                selector.ResetToMedium();
                Assert.That(selector.SelectedSize,
                    Is.EqualTo(GeneratedObjectSize.Medium));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(model);
                foreach (var material in buttonMaterials)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        [Test]
        public void TransformCommandsAndHostBroadcastsUseDifferentRoutes()
        {
            var coordinatorType = typeof(MeshupGameCoordinator);
            var kindType = coordinatorType.GetNestedType("MessageKind",
                System.Reflection.BindingFlags.NonPublic);
            var messageType = coordinatorType.GetNestedType("GameMessage",
                System.Reflection.BindingFlags.NonPublic);
            var routeMethod = coordinatorType.GetMethod(
                "IsAuthoritativeInbound",
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic);
            Assert.That(kindType, Is.Not.Null);
            Assert.That(messageType, Is.Not.Null);
            Assert.That(routeMethod, Is.Not.Null);

            var transformKind = Enum.Parse(kindType, "ObjectTransform");
            var message = Activator.CreateInstance(messageType);
            Assert.That(routeMethod.Invoke(null,
                new[] { transformKind, message }), Is.False,
                "A mime transform must reach the host command handler.");

            messageType.GetField("creatorPeerId")?.SetValue(message, "host");
            Assert.That(routeMethod.Invoke(null,
                new[] { transformKind, message }), Is.True,
                "A host transform must be applied as authoritative state.");
        }

        [Test]
        public void GeneratedObjectsUseCollisionSafeGrabMovement()
        {
            var gameObject = new GameObject("Generated Object Test");
            try
            {
                var generated = gameObject.AddComponent<MeshupGeneratedObject>();
                var addInteractionComponents = typeof(MeshupGeneratedObject)
                    .GetMethod("AddInteractionComponents",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);

                Assert.That(addInteractionComponents, Is.Not.Null);
                addInteractionComponents.Invoke(generated, null);

                var body = gameObject.GetComponent<Rigidbody>();
                var grab = gameObject.GetComponent<UnityEngine.XR.Interaction
                    .Toolkit.Interactables.XRGrabInteractable>();
                Assert.That(body, Is.Not.Null);
                Assert.That(grab, Is.Not.Null);
                Assert.That(grab.useDynamicAttach, Is.True,
                    "Each grab must start from the object's current pose.");
                Assert.That(grab.matchAttachPosition, Is.True);
                Assert.That(grab.matchAttachRotation, Is.True,
                    "Grabbing must preserve the object's placed orientation.");
                Assert.That(grab.trackRotation, Is.True,
                    "Held objects must turn with the player's hand.");
                Assert.That(grab.movementType, Is.EqualTo(
                    UnityEngine.XR.Interaction.Toolkit.Interactables
                        .XRBaseInteractable.MovementType.VelocityTracking));
                Assert.That(grab.throwOnDetach, Is.False,
                    "Kinematic generated objects must not receive throw velocity on release.");
                Assert.That(body.isKinematic, Is.True,
                    "Generated objects must remain fixed after being placed.");
                Assert.That(body.collisionDetectionMode,
                    Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
                Assert.That(body.interpolation,
                    Is.EqualTo(RigidbodyInterpolation.Interpolate));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void MimeTerminalMountsOnItsScreenAndShowsBothChoices()
        {
            var owner = new GameObject("Game View Owner");
            var desktopOverlay = new GameObject("Desktop Hints",
                typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster));
            desktopOverlay.GetComponent<Canvas>().renderMode =
                RenderMode.ScreenSpaceOverlay;
            var monitor = CreateScreenProp("Monitor", "pPlane1_monter_MTL_0",
                new Vector3(-3f, 1f, 0f), new Vector3(2f, 1f, 1f));
            var terminal = CreateScreenProp("Terminal", "screen_low_Material_0",
                new Vector3(2f, 1.5f, 0f), new Vector3(0.2f, 1.2f, 1.6f),
                PrimitiveType.Cube);
            var viewer = new GameObject("Viewer");
            viewer.transform.position = new Vector3(0f, 1.5f, 0f);
            var startInvoked = false;

            try
            {
                var view = owner.AddComponent<MeshupGameView>();
                var monitorFront = new GameObject("Monitor Front Mount").transform;
                monitorFront.SetParent(monitor.transform, false);
                var monitorBack = new GameObject("Monitor Back Mount").transform;
                monitorBack.SetParent(monitor.transform, false);
                var terminalMount = new GameObject("Terminal UI Mount").transform;
                terminalMount.SetParent(terminal.transform, false);
                terminalMount.position = terminal.transform.GetChild(0).position
                    + Vector3.right * 0.125f;
                terminalMount.rotation = Quaternion.LookRotation(Vector3.left);
                view.Build(monitorFront, monitorBack, terminalMount,
                    viewer.transform, _ => { }, () => startInvoked = true);
                view.Render(new MeshupMatchSnapshot
                {
                    phase = (int)MeshupGamePhase.ChoosingWord,
                    mimePeerId = "mime",
                    scores = new[]
                    {
                        new MeshupPlayerScore
                        {
                            peerId = "mime",
                            displayName = "Mime",
                            connected = true
                        }
                    }
                }, "mime", new[] { "jump", "swim" }, string.Empty);

                var canvas = terminal.transform.Find("Terminal UI Mount/MeshUp Mime Terminal UI");
                Assert.That(canvas, Is.Not.Null);
                Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);
                Assert.That(canvas.GetComponent<TrackedDeviceGraphicRaycaster>(),
                    Is.Not.Null);
                var centerDistance = Vector3.Distance(canvas.position,
                    terminal.transform.GetChild(0).position);
                Assert.That(centerDistance, Is.GreaterThan(0.1f),
                    "The canvas must clear the terminal casing.");
                Assert.That(centerDistance, Is.LessThan(0.15f));
                Assert.That(Vector3.Dot(-canvas.forward,
                    (viewer.transform.position - canvas.position).normalized),
                    Is.LessThan(-0.99f),
                    "The mime UI must mount on the terminal's opposite face.");

                var initialPosition = canvas.position;
                var initialRotation = canvas.rotation;
                viewer.transform.position = new Vector3(4f, 1.5f, 0f);
                typeof(MeshupGameView).GetMethod("LateUpdate",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)?.Invoke(view, null);
                Assert.That(canvas.position, Is.EqualTo(initialPosition),
                    "The mime UI must remain on its original display face.");
                Assert.That(canvas.rotation, Is.EqualTo(initialRotation),
                    "The mime UI must not turn around to follow the player.");

                var buttons = canvas.GetComponentsInChildren<Button>(true);
                Assert.That(buttons.Single(button => button.name == "First Choice")
                    .GetComponentInChildren<Text>().text, Is.EqualTo("jump"));
                Assert.That(buttons.Single(button => button.name == "Second Choice")
                    .GetComponentInChildren<Text>().text, Is.EqualTo("swim"));
                Assert.That(buttons.Where(button => button.name.Contains("Choice"))
                    .All(button => button.gameObject.activeSelf && button.interactable),
                    Is.True);

                view.Render(new MeshupMatchSnapshot
                {
                    phase = (int)MeshupGamePhase.Preparation,
                    mimePeerId = "mime",
                    generationTokens = 2,
                    scores = new[]
                    {
                        new MeshupPlayerScore
                        {
                            peerId = "mime",
                            displayName = "Mime",
                            connected = true
                        }
                    }
                }, "mime", Array.Empty<string>(), "jump");
                var start = buttons.Single(button => button.name == "Start");
                Assert.That(start.gameObject.activeSelf, Is.True);
                Assert.That(start.interactable, Is.True);
                var cursorField = typeof(MeshupGameView).GetField(
                    "cursorReleasedForTerminal",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
                Assert.That(cursorField?.GetValue(view), Is.True,
                    "Desktop interaction must remain active for Start.");
                Assert.That(desktopOverlay.GetComponent<GraphicRaycaster>().enabled,
                    Is.False, "Decorative desktop hints must not consume clicks.");
                start.onClick.Invoke();
                Assert.That(startInvoked, Is.True);

                view.Render(new MeshupMatchSnapshot
                {
                    phase = (int)MeshupGamePhase.TimedGuessing,
                    mimePeerId = "mime",
                    maskedWord = "____",
                    remainingSeconds = 100,
                    scores = new[]
                    {
                        new MeshupPlayerScore
                        {
                            peerId = "guesser",
                            displayName = "Guesser",
                            connected = true
                        }
                    }
                }, "guesser", Array.Empty<string>(), string.Empty,
                    string.Empty,
                    "thing is an incorrect guess — try again", true);
                var monitorStatus = monitor.transform
                    .Find("Monitor Front Mount/MeshUp Monitor UI")
                    .GetComponentsInChildren<Text>(true)
                    .Single(text => text.name == "Game Status");
                Assert.That(monitorStatus.text,
                    Does.Contain("thing is an incorrect guess — try again"));
                var listening = monitor.transform
                    .Find("Monitor Front Mount/MeshUp Monitor UI")
                    .GetComponentsInChildren<Text>(true)
                    .Single(text => text.name == "Listening Indicator");
                Assert.That(listening.gameObject.activeSelf, Is.True);
                Assert.That(listening.text, Does.Contain("LISTENING"));
                Assert.That(desktopOverlay.GetComponent<GraphicRaycaster>().enabled,
                    Is.True, "The desktop overlay raycaster must be restored.");

                view.Render(new MeshupMatchSnapshot
                {
                    phase = (int)MeshupGamePhase.Finished,
                    scores = new[]
                    {
                        new MeshupPlayerScore
                        {
                            peerId = "winner",
                            displayName = "Ada",
                            points = 5,
                            connected = true
                        },
                        new MeshupPlayerScore
                        {
                            peerId = "runner-up",
                            displayName = "Grace",
                            points = 3,
                            connected = true
                        }
                    }
                }, "winner", Array.Empty<string>(), string.Empty);
                Assert.That(monitorStatus.text,
                    Is.EqualTo("FINAL LEADERBOARD\n\nAda won"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(desktopOverlay);
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(monitor);
                UnityEngine.Object.DestroyImmediate(terminal);
                UnityEngine.Object.DestroyImmediate(viewer);
            }
        }

        [Test]
        public void AuthoredGameSceneContainsRuntimeAttachmentPoints()
        {
            Assert.That(UnityEditor.PlayerSettings.insecureHttpOption.ToString(),
                Is.EqualTo("AlwaysAllowed"));
            var scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/GameScene.unity", OpenSceneMode.Single);
            var names = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(item => item.name).ToArray();
            foreach (var required in new[]
            {
                "Invisible_wall_game_area", "guesser_monitor", "mime_terminal",
                "Button_Generate", "generator_particle_system",
                "3D_Model_Generator"
            })
            {
                Assert.That(names, Does.Contain(required));
            }
            Assert.That(UnityEngine.Object.FindAnyObjectByType<
                GameStartCoordinator>(), Is.Not.Null);
            Assert.That(UnityEngine.Object.FindAnyObjectByType<
                PlayerMovementAuthority>(), Is.Not.Null);
            var desktopMove = UnityEngine.Object.FindObjectsByType<
                    ContinuousMoveProvider>()
                .Single(item => item.name == "Traditional Locomotion Provider");
            Assert.That(desktopMove.moveSpeed, Is.EqualTo(2.75f));
        }

        private static MeshupMatchState CreateState()
        {
            var state = new MeshupMatchState(new System.Random(7));
            state.Begin(new[]
            {
                new ParticipantInfo("one", "One", true),
                new ParticipantInfo("two", "Two", true),
                new ParticipantInfo("three", "Three", true)
            });
            return state;
        }

        private static GameObject SizeButton(string name)
        {
            var button = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            button.AddComponent<BoxCollider>();
            button.AddComponent<XRSimpleInteractable>();
            return button;
        }

        private static GameObject PhysicalSizeButton(Transform parent,
            string name, float x, Material material)
        {
            var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = name;
            button.transform.SetParent(parent, false);
            button.transform.localPosition = new Vector3(x, 1f, 0f);
            button.GetComponent<Renderer>().sharedMaterial = material;
            return button;
        }

        private static void BeginTimedRound(MeshupMatchState state, string word)
        {
            var mime = state.MimePeerId;
            Assert.That(state.MimeEntered(mime, new[] { word, "swim" }), Is.True);
            Assert.That(state.SelectWord(mime, 0), Is.True);
            Assert.That(state.StartTimer(mime), Is.True);
        }

        private static GameObject CreateScreenProp(string rootName,
            string surfaceName, Vector3 surfacePosition, Vector3 surfaceScale,
            PrimitiveType primitive = PrimitiveType.Quad)
        {
            var root = new GameObject(rootName);
            var surface = GameObject.CreatePrimitive(primitive);
            surface.name = surfaceName;
            surface.transform.SetParent(root.transform, false);
            surface.transform.position = surfacePosition;
            surface.transform.localScale = surfaceScale;
            return root;
        }
    }
}
