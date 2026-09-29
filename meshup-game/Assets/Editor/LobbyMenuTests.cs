using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meshup.EditorTools;
using Meshup.Lobby;
using Meshup.Multiplayer;
using NUnit.Framework;
using Ubiq.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.SceneManagement;

namespace Meshup.Editor.Tests
{
    public sealed class LobbyMenuTests
    {
        private static int roomClicks;

        [Test]
        public void AuthoredMenuKeepsItsPhysicalScaleCameraAndInteractionWiring()
        {
            using var scope = new SceneValidationScope("Assets/Scenes/SampleScene.unity");
            LobbyTokenExperienceValidation.ValidateExperience();
            LobbyDualModePlayerValidation.Validate();
            var panel = scope.Scene.GetRootGameObjects().Single(go => go.name == "Lobby UI")
                .GetComponent<RoomTotemPanel>();
            var canvas = panel.PanelRoot.GetComponent<Canvas>();
            Assert.That(canvas.gameObject.activeSelf, Is.False);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(canvas.worldCamera, Is.SameAs(panel.GetComponent<Canvas>().worldCamera));
            Assert.That(new SerializedObject(canvas).FindProperty("m_OverrideSorting").boolValue, Is.True);
            Assert.That(canvas.sortingOrder, Is.EqualTo(panel.GetComponent<Canvas>().sortingOrder + 1));
            foreach (var scale in new[] { canvas.transform.lossyScale.x,
                canvas.transform.lossyScale.y, canvas.transform.lossyScale.z })
                Assert.That(scale, Is.EqualTo(0.005f).Within(0.000001f));

            var components = canvas.GetComponentsInChildren<Component>(true);
            Assert.That(components.All(c => c != null), Is.True, "No missing scripts.");
            Assert.That(components.OfType<BrowsePanelController>(), Is.Empty);
            Assert.That(components.OfType<BrowseMenuControl>(), Is.Empty);
            Assert.That(components.OfType<BrowseMenuControlJoinButton>(), Is.Empty);
            Assert.That(components.OfType<Ubiq.Samples.Social.NameTextEntry>(), Is.Empty);
            foreach (var listener in components.OfType<TextEntryKeyboardListener>())
            {
                Assert.That(listener.keyboard, Is.Not.Null);
                Assert.That(listener.textEntry, Is.Not.Null);
                Assert.That(listener.textEntry.text, Is.Not.Null);
                foreach (var key in listener.keyboard.GetComponentsInChildren<Key>(true))
                    Assert.That(key.keyboard, Is.SameAs(listener.keyboard));
            }
            foreach (var button in components.OfType<Button>())
            {
                var methods = Enumerable.Range(0, button.onClick.GetPersistentEventCount()).ToArray();
                Assert.That(methods.Any(i => button.onClick.GetPersistentMethodName(i) == "PlayOneShot"),
                    Is.True, button.name + " keeps its authored click sound");
                foreach (var i in methods)
                    Assert.That(button.onClick.GetPersistentTarget(i), Is.Not.Null, button.name);
            }

            // Inspector references must survive scene-object renames.
            var originalName = canvas.name;
            try
            {
                canvas.name = "Renamed authored menu";
                LobbyTokenExperienceValidation.ValidateExperience();
            }
            finally { canvas.name = originalName; }
        }

        [UnityTest]
        public IEnumerator TrackedRaysCanNavigateTypeAndClickRoomRowsAfterStartup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject menuOwner;
            using (var scope = new SceneValidationScope("Assets/Scenes/SampleScene.unity"))
            {
                var source = scope.Scene.GetRootGameObjects().Single(go => go.name == "Lobby UI");
                menuOwner = Object.Instantiate(source);
                SceneManager.MoveGameObjectToScene(menuOwner, SceneManager.GetActiveScene());
                menuOwner.name = "Lobby UI";
                menuOwner.GetComponent<HologramBillboard>().enabled = false;
            }
            menuOwner.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var camera = new GameObject("Test Camera", typeof(Camera), typeof(AudioListener))
                .GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = Vector3.back * 10f;
            menuOwner.GetComponent<Canvas>().worldCamera = camera;
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            menuOwner.SetActive(true);

            yield return new UnityEngine.TestTools.EnterPlayMode();
            var owner = GameObject.Find("Lobby UI");
            var panel = owner.GetComponent<RoomTotemPanel>();
            panel.Open(null);
            yield return null;
            var canvas = panel.PanelRoot.GetComponent<Canvas>();
            Assert.That(canvas.overrideSorting, Is.True);
            Assert.That(canvas.worldCamera, Is.SameAs(owner.GetComponent<Canvas>().worldCamera));
            var main = canvas.transform.Find("Main Panel");

            Click(main.Find("Menu Panel/User Panel/User Customization Buttons /Name").GetComponent<Button>(), canvas);
            yield return null;
            var namePanel = main.Find("Set Name Panel");
            var nameEntry = namePanel.Find("Content/Text Input Area/Text").GetComponent<TextEntry>();
            Assert.That(nameEntry.text.text, Is.EqualTo("Name"));
            ClickKey(namePanel, "A", canvas);
            ClickKey(namePanel, "B", canvas);
            Assert.That(nameEntry.text.text, Is.EqualTo("ab"));
            ClickKey(namePanel, "backspace", canvas);
            Assert.That(nameEntry.text.text, Is.EqualTo("a"));
            Click(namePanel.Find("Title/Back Button").GetComponent<Button>(), canvas);
            yield return null;

            Click(main.Find("Menu Panel/Current Room Panel/Not In Room/Buttons/New Room").GetComponent<Button>(), canvas);
            yield return null;
            var roomPanel = main.Find("New Room Panel");
            Assert.That(panel.GeneratedRoomName, Is.Not.Empty.And.Not.EqualTo("My Room"));
            ClickKey(roomPanel, "D", canvas);
            Assert.That(panel.GeneratedRoomName, Is.EqualTo("d"));
            Click(roomPanel.Find("Title/Back Button").GetComponent<Button>(), canvas);
            yield return null;

            Click(main.Find("Menu Panel/Current Room Panel/Not In Room/Buttons/Join Room").GetComponent<Button>(), canvas);
            yield return null;
            var joinPanel = main.Find("Join Room Panel");
            ClickKey(joinPanel, "1", canvas);
            ClickKey(joinPanel, "2", canvas);
            Assert.That(joinPanel.Find("Content/Text Input Area/Text").GetComponent<TextEntry>().text.text,
                Is.EqualTo("12"));
            Click(joinPanel.Find("Title/Back Button").GetComponent<Button>(), canvas);
            yield return null;

            Click(main.Find("Menu Panel/Current Room Panel/Not In Room/Buttons/Browse").GetComponent<Button>(), canvas);
            yield return null;
            var browse = main.Find("Browse Panel");
            var template = browse.Find("Browse Menu Control Template").GetComponent<RoomListItemView>();
            Assert.That(template, Is.Not.Null);
            var row = Object.Instantiate(template,
                browse.Find("Room List/Viewport/Controls"));
            Assert.That(row, Is.Not.Null);
            row.Bind(new RoomListing("Test room", "test-room", "12345"), null);
            row.gameObject.SetActive(true);
            var joinButton = row.GetComponentInChildren<Button>();
            Assert.That(joinButton, Is.Not.Null);
            roomClicks = 0;
            joinButton.onClick.AddListener(CountRoomClick);
            yield return null;
            Click(joinButton, canvas);
            Assert.That(roomClicks, Is.EqualTo(1));
            Click(joinButton, canvas, tracked: false);
            Assert.That(roomClicks, Is.EqualTo(2), "Desktop clicks still reach the same room row.");
            row.SetInteractable(false);
            Click(joinButton, canvas);
            Assert.That(roomClicks, Is.EqualTo(2), "Busy room rows reject tracked clicks.");

            yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying)
                yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        private static void ClickKey(Transform panel, string key, Canvas canvas)
        {
            Click(panel.Find("Keyboard/Keyboard/" + key).GetComponent<Button>(), canvas);
        }

        private static void CountRoomClick() => roomClicks++;

        private static void Click(Button button, Canvas canvas, bool tracked = true)
        {
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            var center = rect.TransformPoint(rect.rect.center);
            PointerEventData data = tracked
                ? new TrackedDeviceEventData(EventSystem.current)
                {
                    layerMask = -1,
                    rayPoints = new List<Vector3>
                    {
                        center - canvas.transform.forward,
                        center + canvas.transform.forward
                    }
                }
                : new PointerEventData(EventSystem.current);
            data.button = PointerEventData.InputButton.Left;
            data.position = canvas.worldCamera.WorldToScreenPoint(center);
            var hits = new List<RaycastResult>();
            if (tracked) canvas.GetComponent<TrackedDeviceGraphicRaycaster>().Raycast(data, hits);
            else canvas.GetComponent<GraphicRaycaster>().Raycast(data, hits);
            Assert.That(hits, Is.Not.Empty, button.name + " receives a UI ray");
            var target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.That(target, Is.SameAs(button.gameObject), button.name + " is the first interactive hit");
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
        }
    }
}
