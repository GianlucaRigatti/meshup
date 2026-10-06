using System;
using System.Collections;
using System.Linq;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.Editor.Tests
{
    public sealed class GeneratorConsoleTests
    {
        private const string PrefabPath = "Assets/EXTRA_Resources/Art/GeneratorConsole/IntegratedGeneratorConsole.prefab";
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
        public void FailedGenerationReleasesPendingStateAndRestoresAttempt()
        {
            var host = PreparedState();
            var mime = host.MimePeerId;
            Assert.That(host.TryBeginGeneration(mime), Is.True);
            Assert.That(host.GenerationTokens, Is.EqualTo(2));
            Assert.That(host.EndGeneration(false), Is.True);
            Assert.That(host.GenerationPending, Is.False);
            Assert.That(host.GenerationTokens, Is.EqualTo(3));
            Assert.That(host.TryBeginGeneration(mime), Is.True);
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
                    var data = new SerializedObject(selector);
                    data.FindProperty("smallButton").objectReferenceValue = buttons.Single(b => b.name == "Button_Small").gameObject;
                    data.FindProperty("mediumButton").objectReferenceValue = buttons.Single(b => b.name == "Button_Medium").gameObject;
                    data.FindProperty("extraLargeButton").objectReferenceValue = buttons.Single(b => b.name == "Button_ExtraLarge").gameObject;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    selector.Configure(() => index == 0,
                        size => { requests++; host.TrySelectSize(host.MimePeerId, size); });
                    selector.SetInteractable(index == 0);
                    return selector;
                }).ToArray();
                Assert.That(selectors.Select(selector => selector.SelectedSize),
                    Is.All.EqualTo(GeneratedObjectSize.Medium));
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
                selectors[2].ResetToMedium();
                Assert.That(selectors[2].SelectedSize, Is.EqualTo(GeneratedObjectSize.Medium));
                Assert.That(requests, Is.EqualTo(1), "Resetting a local display must not send a selection request.");
            }
            finally { foreach (var peer in peers) UnityEngine.Object.DestroyImmediate(peer); }
        }

        [Test]
        public void ConsolePrefabHasInteractionAudioAndNoAutoplay()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var controls = prefab.GetComponentsInChildren<ConsoleButtonFeedback>(true);
            Assert.That(controls, Has.Length.EqualTo(4));
            foreach (var control in controls)
            {
                var collider = control.GetComponent<BoxCollider>();
                var interactable = control.GetComponent<XRSimpleInteractable>();
                var source = control.GetComponent<AudioSource>();
                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.size.sqrMagnitude, Is.GreaterThan(0));
                Assert.That(interactable, Is.Not.Null);
                Assert.That(interactable.colliders, Does.Contain(collider));
                Assert.That(source, Is.Not.Null);
                Assert.That(source.playOnAwake, Is.False);
                Assert.That(source.spatialBlend, Is.EqualTo(1f));
                var feedback = new SerializedObject(control);
                Assert.That(feedback.FindProperty("indicator").objectReferenceValue, Is.Not.Null);
            }
            Assert.That(controls.Count(control => control.GetComponent<AudioSource>().clip != null),
                Is.EqualTo(3), "The three size buttons have authored click sounds.");
            Assert.That(prefab.GetComponentsInChildren<Animator>(true).All(a => !a.enabled), Is.True);
            Assert.That(prefab.GetComponentsInChildren<Animation>(true).All(a => !a.enabled && !a.playAutomatically), Is.True);
        }

        [UnityTest]
        public IEnumerator FeedbackReleasesToAuthoredRestAndKeepsSelectionLit()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var peer = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                var control = peer.GetComponentsInChildren<ConsoleButtonFeedback>().Single(b => b.name == "Button_Small");
                var rest = control.transform.localPosition;
                var travel = new Vector3(0.02f, -0.03f, 0.01f);
                control.Configure(control.transform.Find("Lit_Small"), travel);
                control.SetSelected(true);
                control.SetHeld(true);
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(Vector3.Distance(rest + travel, control.transform.localPosition), Is.LessThan(1e-6));
                control.enabled = false;
                Assert.That(control.transform.localPosition, Is.EqualTo(rest));
                Assert.That(control.transform.Find("Lit_Small").localScale, Is.EqualTo(Vector3.one));
            }
            finally { UnityEngine.Object.DestroyImmediate(peer); }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator GenerateButtonPlaysSoundsOnPressAndRelease()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var peer = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                var button = peer.GetComponentsInChildren<ConsoleButtonFeedback>(true)
                    .Single(control => control.name == "Button_Generate").gameObject;
                var client = button.AddComponent<MeshupAssetGeneratorClient>();
                client.Configure(null);
                var interactable = button.GetComponent<XRSimpleInteractable>();
                var source = button.GetComponent<AudioSource>();
                interactable.selectEntered.Invoke(new SelectEnterEventArgs { interactableObject = interactable });
                Assert.That(source.isPlaying, Is.True, "Pressing the button plays its sound.");
                source.Stop();
                interactable.selectExited.Invoke(new SelectExitEventArgs { interactableObject = interactable });
                Assert.That(source.isPlaying, Is.True, "Releasing the button plays its sound.");
            }
            finally { UnityEngine.Object.DestroyImmediate(peer); }
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
