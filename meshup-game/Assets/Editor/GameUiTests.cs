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
            Assert.That(monitor.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(1200f, 600f)));
            Assert.That(terminal.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(800f, 600f)));
            Assert.That(monitor.GetComponent<Canvas>().worldCamera, Is.Not.Null);
            Assert.That(terminal.worldCamera, Is.SameAs(monitor.GetComponent<Canvas>().worldCamera));
            Canvas.ForceUpdateCanvases();
            foreach (var button in terminal.GetComponentsInChildren<Button>(true))
            {
                var rect = button.GetComponent<RectTransform>();
                Assert.That(button.GetComponent<BoxCollider>().size,
                    Is.EqualTo(new Vector3(rect.rect.width, rect.rect.height, 8f)));
                Assert.That(button.GetComponent<XRSimpleInteractable>(), Is.Not.Null);
                Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1));
                Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo("PlayOneShot"));
                Assert.That(button.onClick.GetPersistentTarget(0), Is.SameAs(button.GetComponent<AudioSource>()));
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
            view.Configure(front, back, camera.transform, ChooseWord, StartRound);
            choices = starts = 0;
            Render(view, MeshupGamePhase.ChoosingWord);
            yield return null;
            var terminal = terminalMount.GetComponentInChildren<Canvas>();
            var first = terminal.GetComponentsInChildren<Button>(true).Single(b => b.name == "First Choice");
            var second = terminal.GetComponentsInChildren<Button>(true).Single(b => b.name == "Second Choice");
            var start = terminal.GetComponentsInChildren<Button>(true).Single(b => b.name == "Start");
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

        private static void Click(Button button, Canvas canvas, bool tracked)
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
            data.position = canvas.worldCamera.WorldToScreenPoint(center);
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
