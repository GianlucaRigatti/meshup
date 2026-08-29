using System;
using System.Linq;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        public void AuthoredGameSceneContainsRuntimeAttachmentPoints()
        {
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
    }
}
