using System;
using System.Linq;
using System.Reflection;
using Meshup.EditorTools;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class GeneratorConsoleTests
    {
        private const string PrefabPath = "Assets/Art/GeneratorConsole/IntegratedGeneratorConsole.prefab";
        private static MeshupMatchState PreparedState()
        {
            var host = new MeshupMatchState(new System.Random(7));
            host.Begin(new[] { new ParticipantInfo("one", "One", true), new ParticipantInfo("two", "Two", true) });
            Assert.That(host.MimeEntered(host.MimePeerId, new[] { "jump", "swim" }), Is.True);
            Assert.That(host.SelectWord(host.MimePeerId, 0), Is.True);
            return host;
        }

        [Test]
        public void OnlyCurrentMimeCanChangeSharedSelectionDuringPreparation()
        {
            var host = PreparedState();
            var other = host.Players.Single(p => p.peerId != host.MimePeerId).peerId;
            Assert.That(host.TrySelectSize(other, GeneratedObjectSize.Small), Is.False);
            Assert.That(host.TrySelectSize("unknown", GeneratedObjectSize.Small), Is.False);
            Assert.That(host.TrySelectSize(host.MimePeerId, (GeneratedObjectSize)42), Is.False);
            Assert.That(host.SelectedSize, Is.EqualTo(GeneratedObjectSize.Medium));
            Assert.That(host.TrySelectSize(host.MimePeerId, GeneratedObjectSize.ExtraLarge), Is.True);
            Assert.That(host.GenerationTokens, Is.EqualTo(3));
            Assert.That(host.TryBeginGeneration(host.MimePeerId), Is.True);
            Assert.That(host.TrySelectSize(host.MimePeerId, GeneratedObjectSize.Small), Is.False);
            host.EndGeneration();
            host.StartTimer(host.MimePeerId);
            Assert.That(host.TrySelectSize(host.MimePeerId, GeneratedObjectSize.Small), Is.False);
        }

        [Test]
        public void SelectionSurvivesSnapshotSerializationAndResetsForNextMime()
        {
            var host = PreparedState();
            host.TrySelectSize(host.MimePeerId, GeneratedObjectSize.Small);
            var received = JsonUtility.FromJson<MeshupMatchSnapshot>(JsonUtility.ToJson(host.CreateSnapshot()));
            Assert.That(received.selectedSize, Is.EqualTo(GeneratedObjectSize.Small));
            Assert.That(received.sizeSelectionRevision, Is.EqualTo(1));
            var version = received.version;
            host.TrySelectSize(host.MimePeerId, GeneratedObjectSize.Small);
            Assert.That(host.SizeSelectionRevision, Is.EqualTo(2), "Repeated presses still have a shared feedback revision.");
            Assert.That(host.Version, Is.GreaterThan(version));
            host.Disconnect(host.MimePeerId);
            Assert.That(host.CreateSnapshot().selectedSize, Is.EqualTo(GeneratedObjectSize.Medium));
        }

        [Test]
        public void NoTokensRejectSelectionWithoutChangingSnapshot()
        {
            var host = PreparedState();
            for (var i = 0; i < 3; i++)
            {
                Assert.That(host.TryBeginGeneration(host.MimePeerId), Is.True);
                host.EndGeneration();
            }
            var before = JsonUtility.ToJson(host.CreateSnapshot());
            Assert.That(host.TrySelectSize(host.MimePeerId, GeneratedObjectSize.Small), Is.False);
            Assert.That(JsonUtility.ToJson(host.CreateSnapshot()), Is.EqualTo(before));
        }

        [Test]
        public void TwoPeersAndLateJoinerDisplayAuthoritativeSelectionWithoutEcho()
        {
            var host = PreparedState();
            var peers = Enumerable.Range(0, 3).Select(_ => UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))).ToArray();
            try
            {
                var requests = 0;
                var selectors = peers.Select((peer, index) =>
                {
                    var selector = peer.AddComponent<GeneratedObjectSizeSelector>();
                    var buttons = peer.GetComponentsInChildren<ConsoleButtonFeedback>(true);
                    selector.Configure(buttons.Single(b => b.name == "Button_Small").gameObject,
                        buttons.Single(b => b.name == "Button_Medium").gameObject,
                        buttons.Single(b => b.name == "Button_ExtraLarge").gameObject,
                        () => index == 0, size => { requests++; host.TrySelectSize(host.MimePeerId, size); });
                    selector.SetInteractable(index == 0);
                    return selector;
                }).ToArray();
                Assert.That(selectors[0].TrySelect(GeneratedObjectSize.ExtraLarge), Is.True);
                Assert.That(selectors[0].SelectedSize, Is.EqualTo(GeneratedObjectSize.Medium), "Wait for host acceptance.");
                Assert.That(selectors[1].TrySelect(GeneratedObjectSize.Small), Is.False);
                var packet = JsonUtility.ToJson(host.CreateSnapshot());
                foreach (var selector in selectors)
                {
                    selector.ApplySelectedSize(JsonUtility.FromJson<MeshupMatchSnapshot>(packet).selectedSize);
                    Assert.That(selector.SelectedSize, Is.EqualTo(GeneratedObjectSize.ExtraLarge));
                    var transforms = selector.GetComponentsInChildren<Transform>(true);
                    Assert.That(transforms.Single(t => t.name == "Lit_ExtraLarge").localScale, Is.EqualTo(Vector3.one));
                    Assert.That(transforms.Single(t => t.name == "Lit_Medium").localScale.x, Is.LessThan(.002f));
                    Assert.That(selector.GetComponentsInChildren<Light>(true), Is.Empty);
                }
                Assert.That(requests, Is.EqualTo(1), "Remote application must not send another selection request.");
            }
            finally { foreach (var peer in peers) UnityEngine.Object.DestroyImmediate(peer); }
        }

        [Test]
        public void ConsolePrefabHasFourIndependentCollidersAndNoAutoplay()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var controls = prefab.GetComponentsInChildren<ConsoleButtonFeedback>(true);
            Assert.That(controls, Has.Length.EqualTo(4));
            foreach (var control in controls)
            {
                var collider = control.GetComponent<BoxCollider>();
                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.size.sqrMagnitude, Is.GreaterThan(0));
                var feedback = new SerializedObject(control);
                Assert.That(feedback.FindProperty("indicator").objectReferenceValue, Is.Not.Null);
                Assert.That(feedback.FindProperty("pressOffset").vector3Value.magnitude, Is.EqualTo(.014f).Within(1e-6));
            }
            Assert.That(prefab.GetComponentsInChildren<Animator>(true).All(a => !a.enabled), Is.True);
            Assert.That(prefab.GetComponentsInChildren<Animation>(true).All(a => !a.enabled && !a.playAutomatically), Is.True);
            using var scope = new SceneValidationScope("Assets/Scenes/GameScene.unity");
            var all = scope.Scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            Assert.That(all.Count(t => t.name == "IntegratedGeneratorConsole"), Is.EqualTo(1));
            Assert.That(all.Any(t => t.name == "geneartor_button" || t.name == "button_-_sb_cosmic_shake"), Is.False);
        }

        [Test]
        public void FeedbackReleasesToAuthoredRestAndKeepsSelectionLit()
        {
            var peer = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                var control = peer.GetComponentsInChildren<ConsoleButtonFeedback>().Single(b => b.name == "Button_Small");
                var rest = control.transform.localPosition;
                control.SetSelected(true);
                control.SetHeld(true);
                typeof(ConsoleButtonFeedback).GetField("amount", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(control, 1f);
                typeof(ConsoleButtonFeedback).GetMethod("ApplyVisuals", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { 1f });
                Assert.That(Vector3.Distance(rest, control.transform.localPosition), Is.EqualTo(.014f).Within(1e-6));
                typeof(ConsoleButtonFeedback).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, null);
                Assert.That(control.transform.localPosition, Is.EqualTo(rest));
                Assert.That(control.transform.Find("Lit_Small").localScale, Is.EqualTo(Vector3.one));
            }
            finally { UnityEngine.Object.DestroyImmediate(peer); }
        }
    }
}
