using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meshup.EditorTools;
using Meshup.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Meshup.Editor.Tests
{
    public sealed class GameUiTests
    {
        private static int choices;
        private static int selectedChoice;
        private static int starts;

        [Test]
        public void SavedGameUsesUiPrefabsWithAuthoredButtonCollidersAndSounds()
        {
            using var scope = new SceneValidationScope("Assets/Scenes/GameScene.unity");
            MeshupGameSceneValidation.Validate(scope.Scene);
            var view = scope.Scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<MeshupGameView>(true)).Single();
            var data = new SerializedObject(view);
            var monitor = (Transform)data.FindProperty("monitorCanvas").objectReferenceValue;
            var terminal = (Canvas)data.FindProperty("terminalCanvas").objectReferenceValue;
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(monitor),
                Is.EqualTo("Assets/Prefabs/Game UI/Monitor UI.prefab"));
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(terminal),
                Is.EqualTo("Assets/Prefabs/Game UI/Mime Terminal UI.prefab"));
            Assert.That(monitor.GetComponent<Canvas>().worldCamera, Is.Not.Null);
            Assert.That(terminal.worldCamera, Is.SameAs(monitor.GetComponent<Canvas>().worldCamera));
            Canvas.ForceUpdateCanvases();
            foreach (var button in terminal.GetComponentsInChildren<Button>(true))
            {
                var rect = button.GetComponent<RectTransform>();
                var collider = button.GetComponent<BoxCollider>();
                Assert.That(collider.size.x, Is.GreaterThanOrEqualTo(rect.rect.width));
                Assert.That(collider.size.y, Is.GreaterThanOrEqualTo(rect.rect.height));
                Assert.That(collider.size.z, Is.GreaterThan(0f));
                Assert.That(button.GetComponent<XRSimpleInteractable>(), Is.Not.Null);
                Assert.That(Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                    .Any(index => button.onClick.GetPersistentMethodName(index) == "PlayOneShot"
                        && button.onClick.GetPersistentTarget(index) == button.GetComponent<AudioSource>()),
                    Is.True, "Each button keeps its click sound.");
            }

            var title = (Text)data.FindProperty("terminalTitle").objectReferenceValue;
            var originalName = title.name;
            try
            {
                title.name = "Renamed terminal title";
                MeshupGameSceneValidation.Validate(scope.Scene);
            }
            finally { title.name = originalName; }
        }

        [UnityTest]
        public IEnumerator TerminalSupportsDesktopTrackedUiAndXrSelectionWithoutDoubleClicks()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var owner = new GameObject("Test Game View");
            var view = owner.AddComponent<MeshupGameView>();
            var front = NewMount("Front", owner.transform, Vector3.zero);
            var back = NewMount("Back", owner.transform, Vector3.forward);
            back.rotation = Quaternion.Euler(0f, 180f, 0f);
            var terminalMount = NewMount("Terminal", owner.transform, Vector3.right * 2f);
            MountPrefabs(view, front, terminalMount);
            var camera = new GameObject("Test Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(2f, 0f, -2f);
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            new GameObject("XR Interaction Manager", typeof(XRInteractionManager));

            yield return new EnterPlayMode();
            view = GameObject.Find("Test Game View").GetComponent<MeshupGameView>();
            front = view.transform.Find("Front");
            back = view.transform.Find("Back");
            terminalMount = view.transform.Find("Terminal");
            camera = GameObject.Find("Test Camera");
            Configure(view, front, back, camera.transform, ChooseWord, StartRound);
            choices = starts = 0;
            Render(view, MeshupGamePhase.ChoosingWord);
            yield return null;
            var terminal = terminalMount.GetComponentInChildren<Canvas>();
            var first = terminal.GetComponentsInChildren<Button>(true).Single(b => b.name == "First Choice");
            var second = terminal.GetComponentsInChildren<Button>(true).Single(b => b.name == "Second Choice");
            var start = terminal.GetComponentsInChildren<Button>(true).Single(b => b.name == "Start");
            Assert.That(first.GetComponentInChildren<Text>().text, Is.EqualTo("jump"));
            Assert.That(second.GetComponentInChildren<Text>().text, Is.EqualTo("swim"));
            Click(first, terminal, tracked: false);
            Select(first);
            Assert.That(choices, Is.EqualTo(1), "Desktop and XR events in one frame produce one choice.");
            Assert.That(selectedChoice, Is.EqualTo(0));
            yield return null;
            Click(second, terminal, tracked: true);
            Select(second);
            Assert.That(choices, Is.EqualTo(2), "Tracked UI and collider events produce one choice.");
            Assert.That(selectedChoice, Is.EqualTo(1));

            Render(view, MeshupGamePhase.Preparation, generationPending: true);
            yield return null;
            Assert.That(start.interactable, Is.False);
            Assert.That(start.GetComponent<XRSimpleInteractable>().enabled, Is.False);
            Click(start, terminal, tracked: true);
            Select(start);
            Assert.That(starts, Is.Zero, "Generation blocks both input paths.");
            Render(view, MeshupGamePhase.Preparation);
            yield return null;
            Assert.That(start.GetComponent<XRSimpleInteractable>().colliders,
                Does.Contain(start.GetComponent<BoxCollider>()));
            Physics.SyncTransforms();
            var center = start.GetComponent<RectTransform>().TransformPoint(start.GetComponent<RectTransform>().rect.center);
            Assert.That(Physics.RaycastAll(center - terminal.transform.forward, terminal.transform.forward, 2f)
                .Select(hit => hit.collider), Does.Contain(start.GetComponent<BoxCollider>()));
            Select(start);
            Click(start, terminal, tracked: true);
            Assert.That(starts, Is.EqualTo(1), "Start also rejects duplicate events.");

            var terminalPosition = terminal.transform.position;
            var terminalRotation = terminal.transform.rotation;
            camera.transform.position = new Vector3(0f, 0f, 3f);
            yield return null;
            Assert.That(back.GetComponentInChildren<Canvas>(), Is.Not.Null, "Monitor follows the viewer to the back mount.");
            Assert.That(front.GetComponentInChildren<Canvas>(), Is.Null);
            Assert.That(terminal.transform.position, Is.EqualTo(terminalPosition));
            Assert.That(terminal.transform.rotation, Is.EqualTo(terminalRotation));
            camera.transform.position = new Vector3(0f, 0f, -2f);
            yield return null;
            Assert.That(front.GetComponentInChildren<Canvas>(), Is.Not.Null, "Monitor returns to the front mount.");

            Object.Destroy(view);
            yield return null;
            Select(start);
            Assert.That(starts, Is.EqualTo(1), "Destroying the view removes its XR callbacks.");
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [Test]
        public void MonitorShowsGuessFeedbackListeningStateAndTheWinner()
        {
            var owner = new GameObject("Game view test");
            try
            {
                var view = owner.AddComponent<MeshupGameView>();
                MountPrefabs(view, owner.transform, owner.transform);
                var data = new SerializedObject(view);
                var status = (Text)data.FindProperty("status").objectReferenceValue;
                var listening = (Text)data.FindProperty("listeningIndicator").objectReferenceValue;
                var snapshot = new MeshupMatchSnapshot
                {
                    phase = (int)MeshupGamePhase.TimedGuessing, mimePeerId = "mime",
                    maskedWord = "____", remainingSeconds = 100
                };
                view.Render(snapshot, "guesser", System.Array.Empty<string>(), "",
                    guessFeedback: "Incorrect guess", isListening: true);
                Assert.That(status.text, Does.Contain("Incorrect guess"));
                Assert.That(status.text, Does.Contain("1:40"));
                Assert.That(listening.gameObject.activeSelf, Is.True);
                view.Render(snapshot, "guesser", System.Array.Empty<string>(), "");
                Assert.That(status.text, Does.Not.Contain("Incorrect guess"));
                Assert.That(listening.gameObject.activeSelf, Is.False);

                snapshot.phase = (int)MeshupGamePhase.Finished;
                snapshot.scores = new[]
                {
                    new MeshupPlayerScore { peerId = "winner", displayName = "Ada", points = 5 },
                    new MeshupPlayerScore { peerId = "runner-up", displayName = "Grace", points = 3 }
                };
                view.Render(snapshot, "winner", System.Array.Empty<string>(), "");
                Assert.That(status.text, Does.Contain("Ada won"));
                var leaderboard = (Text)data.FindProperty("leaderboard").objectReferenceValue;
                Assert.That(leaderboard.text.IndexOf("Ada"), Is.LessThan(leaderboard.text.IndexOf("Grace")));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        internal static void Configure(MeshupGameView view, Transform front,
            Transform back, Transform viewer, System.Action<int> chooseWord, System.Action startRound)
        {
            var data = new SerializedObject(view);
            data.FindProperty("monitorFrontMount").objectReferenceValue = front;
            data.FindProperty("monitorBackMount").objectReferenceValue = back;
            data.FindProperty("localViewer").objectReferenceValue = viewer;
            data.ApplyModifiedPropertiesWithoutUndo();
            view.Configure(chooseWord, startRound);
        }

        internal static void MountPrefabs(MeshupGameView view, Transform monitorMount, Transform terminalMount)
        {
            var monitor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Game UI/Monitor UI.prefab"), monitorMount, false);
            var terminal = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Game UI/Mime Terminal UI.prefab"), terminalMount, false);
            monitor.name = "MeshUp Monitor UI";
            terminal.name = "MeshUp Mime Terminal UI";
            var data = new SerializedObject(view);
            void Set(string field, Object value) => data.FindProperty(field).objectReferenceValue = value;
            var interaction = view.GetComponent<GameInteractionState>()
                ?? view.gameObject.AddComponent<GameInteractionState>();
            var interactionData = new SerializedObject(interaction);
            var overlays = Object.FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Include)
                .Where(ray => ray.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay
                    && ray.GetComponent<GameSessionMenu>() == null).ToArray();
            var array = interactionData.FindProperty("desktopOverlays");
            array.arraySize = overlays.Length;
            for (var i = 0; i < overlays.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = overlays[i];
            interactionData.ApplyModifiedPropertiesWithoutUndo();
            Set("interactionState", interaction);
            Set("monitorCanvas", monitor.transform);
            Set("terminalCanvas", terminal.GetComponent<Canvas>());
            Set("leaderboard", monitor.transform.Find("Background/Leaderboard/Scores").GetComponent<Text>());
            Set("status", monitor.transform.Find("Background/Game Status").GetComponent<Text>());
            Set("listeningIndicator", monitor.transform.Find("Background/Listening Indicator").GetComponent<Text>());
            Set("terminalTitle", terminal.transform.Find("Background/Title").GetComponent<Text>());
            Set("terminalStatus", terminal.transform.Find("Background/Status").GetComponent<Text>());
            foreach (var (field, name, label) in new[]
            {
                ("firstChoice", "First Choice", "firstChoiceLabel"),
                ("secondChoice", "Second Choice", "secondChoiceLabel"),
                ("startButton", "Start", "startButtonLabel")
            })
            {
                var button = terminal.transform.Find("Background/" + name).GetComponent<Button>();
                Set(field, button);
                Set(label, button.GetComponentInChildren<Text>(true));
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform NewMount(string name, Transform parent, Vector3 position)
        {
            var mount = new GameObject(name).transform;
            mount.SetParent(parent, false);
            mount.position = position;
            mount.localScale = Vector3.one * 0.001f;
            return mount;
        }

        private static void ChooseWord(int index) { choices++; selectedChoice = index; }
        private static void StartRound() => starts++;

        private static void Render(MeshupGameView view, MeshupGamePhase phase, bool generationPending = false)
        {
            view.Render(new MeshupMatchSnapshot
            {
                phase = (int)phase, mimePeerId = "mime", generationTokens = 3,
                generationPending = generationPending
            }, "mime", new[] { "jump", "swim" }, "jump");
        }

        private static void Select(Button button)
        {
            var xr = button.GetComponent<XRSimpleInteractable>();
            xr.selectEntered.Invoke(new SelectEnterEventArgs { interactableObject = xr });
        }

        internal static void Click(Button button, Canvas canvas, bool tracked)
        {
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            var center = rect.TransformPoint(rect.rect.center);
            PointerEventData data = tracked ? new TrackedDeviceEventData(EventSystem.current)
            {
                layerMask = -1,
                rayPoints = new List<Vector3> { center - canvas.transform.forward, center + canvas.transform.forward }
            } : new PointerEventData(EventSystem.current);
            data.button = PointerEventData.InputButton.Left;
            data.position = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? new Vector2(center.x, center.y)
                : (Vector2)canvas.worldCamera.WorldToScreenPoint(center);
            var hits = new List<RaycastResult>();
            if (tracked) canvas.GetComponent<TrackedDeviceGraphicRaycaster>().Raycast(data, hits);
            else canvas.GetComponent<GraphicRaycaster>().Raycast(data, hits);
            Assert.That(hits, Is.Not.Empty, button.name);
            var target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.That(target, Is.SameAs(button.gameObject));
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
        }
    }
}
