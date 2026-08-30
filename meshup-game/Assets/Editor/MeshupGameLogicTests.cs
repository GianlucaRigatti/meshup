using System;
using System.Linq;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.UI;

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
        public void HintsReachThirtyPercentAtNinetySecondsThenStop()
        {
            var state = CreateState();
            BeginTimedRound(state, "high-five");
            state.Tick(89.9f);
            Assert.That(state.MaskedWord.Count(char.IsLetter), Is.EqualTo(2));
            state.Tick(0.1f);
            Assert.That(state.MaskedWord.Count(char.IsLetter), Is.EqualTo(3));
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
        public void MimeTerminalMountsOnItsScreenAndShowsBothChoices()
        {
            var owner = new GameObject("Game View Owner");
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
                view.Build(monitor.transform, terminal.transform,
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

                var canvas = terminal.transform.Find("MeshUp Mime Terminal UI");
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
                    Is.GreaterThan(0.99f));

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
                    .Find("MeshUp Monitor UI")
                    .GetComponentsInChildren<Text>(true)
                    .Single(text => text.name == "Game Status");
                Assert.That(monitorStatus.text,
                    Does.Contain("thing is an incorrect guess — try again"));
                var listening = monitor.transform
                    .Find("MeshUp Monitor UI")
                    .GetComponentsInChildren<Text>(true)
                    .Single(text => text.name == "Listening Indicator");
                Assert.That(listening.gameObject.activeSelf, Is.True);
                Assert.That(listening.text, Does.Contain("LISTENING"));
            }
            finally
            {
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
                "geneartor_button", "generator_particle_system",
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
                    ContinuousMoveProvider>(FindObjectsSortMode.None)
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
