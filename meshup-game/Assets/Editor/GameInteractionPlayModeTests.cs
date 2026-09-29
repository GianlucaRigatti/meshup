using System.Collections;
using System.Linq;
using System.Reflection;
using Meshup.EditorTools;
using Meshup.Game;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Meshup.Editor.Tests
{
    public sealed class GameInteractionPlayModeTests
    {
        private sealed class TestXrMovement : LocomotionProvider
        {
            protected override void Awake() { }
        }

        private static int choices;
        private static void Choose(int index) => choices++;
        private static void StartRound() { }

        [UnityTest]
        public IEnumerator AuthoredMenuAndTerminalKeepDesktopAndTrackedUiUsableAcrossOverlap()
        {
            var testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using (var scope = new SceneValidationScope("Assets/Scenes/GameScene.unity"))
            {
                var authoredMenu = scope.Scene.GetRootGameObjects().SelectMany(root =>
                    root.GetComponentsInChildren<GameSessionMenu>(true)).Single();
                var copy = Object.Instantiate(authoredMenu.gameObject);
                copy.name = "Test Menu";
                SceneManager.MoveGameObjectToScene(copy, testScene);
            }
            var owner = new GameObject("Test View");
            var view = owner.AddComponent<MeshupGameView>();
            var front = Mount("Front", owner.transform, Vector3.zero);
            var back = Mount("Back", owner.transform, Vector3.forward);
            var terminalMount = Mount("Terminal", owner.transform, Vector3.right * 2f);
            var hints = new GameObject("Test Hints", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            hints.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            GameUiTests.MountPrefabs(view, front, terminalMount);
            var interaction = owner.GetComponent<GameInteractionState>();
            GameInteractionTests.Set(GameObject.Find("Test Menu").GetComponent<GameSessionMenu>(), "interactionState", interaction);
            var camera = new GameObject("Test Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(2f, 0f, -2f);
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            new GameObject("XR Interaction Manager", typeof(XRInteractionManager));

            yield return new EnterPlayMode();
            owner = GameObject.Find("Test View");
            view = owner.GetComponent<MeshupGameView>();
            interaction = owner.GetComponent<GameInteractionState>();
            camera = GameObject.Find("Test Camera");
            var menu = GameObject.Find("Test Menu").GetComponent<GameSessionMenu>();
            var menuCanvas = menu.GetComponent<Canvas>();
            var resume = menu.GetComponentsInChildren<Button>(true).Single(button => button.name == "Resume Button");
            var player = new GameObject("Test Player");
            var movement = player.AddComponent<PlayerMovementAuthority>();
            var xrMovement = player.AddComponent<TestXrMovement>();
            var desktopInput = player.AddComponent<UbiqDemoPlayerInputGate>();
            var traditional = new GameObject("Test desktop controls");
            GameInteractionTests.Set(desktopInput, "traditionalController", traditional);
            GameInteractionTests.Set(interaction, "movementAuthority", movement);
            GameInteractionTests.Set(interaction, "desktopInput", desktopInput);
            GameUiTests.Configure(view, owner.transform.Find("Front"), owner.transform.Find("Back"),
                camera.transform, Choose, StartRound);
            yield return null;
            choices = 0;
            Render(view, MeshupGamePhase.ChoosingWord);
            yield return null;
            var terminal = owner.transform.Find("Terminal").GetComponentInChildren<Canvas>();
            var first = terminal.GetComponentsInChildren<Button>(true).Single(button => button.name == "First Choice");
            Assert.That(xrMovement.enabled, Is.True, "Terminal interaction keeps locomotion available.");
            menu.Open();
            Assert.That(xrMovement.enabled, Is.False, "Pause uses the existing movement lock.");
            Assert.That(traditional.activeSelf, Is.False);
            Assert.That(terminal.GetComponent<TrackedDeviceGraphicRaycaster>().enabled, Is.True);
            Assert.That(menuCanvas.GetComponent<TrackedDeviceGraphicRaycaster>().enabled, Is.True);
            yield return null;
            GameUiTests.Click(resume, menuCanvas, tracked: false);
            Assert.That(Cursor.visible, Is.True, "Resuming keeps the terminal cursor free.");
            Assert.That(xrMovement.enabled, Is.True);
            Assert.That(traditional.activeSelf, Is.True);
            yield return null;
            GameUiTests.Click(first, terminal, tracked: true);
            Assert.That(choices, Is.EqualTo(1));

            // Exercise the authored pause buttons in the same world-space mode used on XR.
            menuCanvas.renderMode = RenderMode.WorldSpace;
            menuCanvas.worldCamera = camera.GetComponent<Camera>();
            menuCanvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1024f, 768f);
            menuCanvas.transform.localScale = Vector3.one * 0.0015f;
            typeof(GameSessionMenu).GetField("xrCanvasConfigured", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(menu, true);
            menu.Open();
            yield return null;
            GameUiTests.Click(resume, menuCanvas, tracked: true);
            Assert.That(Cursor.visible, Is.True);
            Assert.That(xrMovement.enabled, Is.True);
            menu.Open();
            Render(view, MeshupGamePhase.TimedGuessing);
            Assert.That(Cursor.visible, Is.True, "The pause menu keeps its cursor when terminal interaction ends.");
            Assert.That(xrMovement.enabled, Is.False);
            yield return null;
            GameUiTests.Click(resume, menuCanvas, tracked: true);
            Assert.That(Cursor.visible, Is.False);
            Assert.That(xrMovement.enabled, Is.True);
            Assert.That(GameObject.Find("Test Hints").GetComponent<GraphicRaycaster>().enabled, Is.True);

            Render(view, MeshupGamePhase.ChoosingWord);
            var originalVisible = (bool)typeof(GameInteractionState)
                .GetField("originalCursorVisible", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(interaction);
            owner.SetActive(false);
            yield return null;
            Assert.That(Cursor.visible, Is.EqualTo(originalVisible), "View teardown must not override the owner's cursor restoration.");
            Assert.That(GameObject.Find("Test Hints").GetComponent<GraphicRaycaster>().enabled, Is.True);
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        private static Transform Mount(string name, Transform parent, Vector3 position)
        {
            var mount = new GameObject(name).transform;
            mount.SetParent(parent, false);
            mount.position = position;
            mount.localScale = Vector3.one * 0.001f;
            return mount;
        }

        private static void Render(MeshupGameView view, MeshupGamePhase phase) => view.Render(
            new MeshupMatchSnapshot { phase = (int)phase, mimePeerId = "mime", generationTokens = 3 },
            "mime", new[] { "jump", "swim" }, "jump");
    }
}
