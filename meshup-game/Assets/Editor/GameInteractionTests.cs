using System.Reflection;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Meshup.Editor.Tests
{
    public sealed class GameInteractionTests
    {
        private sealed class FakeMovementProvider : MonoBehaviour { }
        private GameObject root;
        private GameInteractionState interaction;
        private PlayerMovementAuthority movement;
        private UbiqDemoPlayerInputGate desktopInput;
        private GameSessionMenu menu;
        private GraphicRaycaster overlay;
        private GraphicRaycaster disabledOverlay;
        private GraphicRaycaster menuRaycaster;
        private TrackedDeviceGraphicRaycaster worldRaycaster;
        private Behaviour provider;
        private CursorLockMode originalCursorLock;
        private bool originalCursorVisible;

        [SetUp]
        public void SetUp()
        {
            originalCursorLock = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            root = new GameObject("Interaction test");
            var player = Child("Player");
            movement = player.AddComponent<PlayerMovementAuthority>();
            desktopInput = player.AddComponent<UbiqDemoPlayerInputGate>();
            provider = player.AddComponent<FakeMovementProvider>();
            movement.Configure(new[] { provider });
            interaction = root.AddComponent<GameInteractionState>();
            overlay = Canvas("Desktop hints", RenderMode.ScreenSpaceOverlay).GetComponent<GraphicRaycaster>();
            disabledOverlay = Canvas("Disabled hints", RenderMode.ScreenSpaceOverlay).GetComponent<GraphicRaycaster>();
            disabledOverlay.enabled = false;
            worldRaycaster = Canvas("World UI", RenderMode.WorldSpace).AddComponent<TrackedDeviceGraphicRaycaster>();
            var menuObject = Canvas("Menu", RenderMode.ScreenSpaceOverlay);
            menuRaycaster = menuObject.GetComponent<GraphicRaycaster>();
            menu = menuObject.AddComponent<GameSessionMenu>();
            var panel = Child("Panel", typeof(RectTransform));
            panel.transform.SetParent(menuObject.transform);
            Set(menu, "panelRoot", panel);
            Set(menu, "interactionState", interaction);
            Set(menu, "resumeButton", Child("Resume", typeof(RectTransform)).AddComponent<Button>());
            Set(menu, "leaveButton", Child("Leave", typeof(RectTransform)).AddComponent<Button>());
            Set(menu, "statusText", Child("Status", typeof(RectTransform)).AddComponent<Text>());
            Set(interaction, "movementAuthority", movement);
            Set(interaction, "desktopInput", desktopInput);
            Set(interaction, "desktopOverlays", new[] { overlay, disabledOverlay });
            Invoke(interaction, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            if (interaction != null) Invoke(interaction, "OnDisable");
            Object.DestroyImmediate(root);
            Cursor.lockState = originalCursorLock;
            Cursor.visible = originalCursorVisible;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ClosingEitherInterfaceKeepsTheOtherInterfacesCursor(bool terminalClosesFirst)
        {
            interaction.SetTerminalActive(true);
            Assert.That(provider.enabled, Is.True, "The mime can still move to the generator.");
            Assert.That(desktopInput.enabled, Is.True);
            Assert.That(overlay.enabled, Is.False);
            Assert.That(worldRaycaster.enabled, Is.True);
            menu.Open();
            Assert.That(provider.enabled, Is.False);
            Assert.That(desktopInput.enabled, Is.False);
            Assert.That(menuRaycaster.enabled, Is.True);
            if (terminalClosesFirst)
            {
                interaction.SetTerminalActive(false);
                Assert.That(provider.enabled, Is.False, "The menu keeps movement locked.");
                Assert.That(Cursor.visible, Is.True);
                Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
                menu.Resume();
            }
            else
            {
                menu.Resume();
                Assert.That(provider.enabled, Is.True);
                Assert.That(desktopInput.enabled, Is.True);
                Assert.That(Cursor.visible, Is.True);
                Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
                Assert.That(overlay.enabled, Is.False);
                interaction.SetTerminalActive(false);
            }
            Assert.That(Cursor.visible, Is.False);
            Assert.That(provider.enabled, Is.True);
            Assert.That(desktopInput.enabled, Is.True);
            Assert.That(overlay.enabled, Is.True);
            Assert.That(disabledOverlay.enabled, Is.False);
            Assert.That(worldRaycaster.enabled, Is.True);
        }

        [Test]
        public void MenuInitializationDoesNotLockAnActiveTerminalCursor()
        {
            interaction.SetTerminalActive(true);
            Invoke(menu, "Start");
            Assert.That(Cursor.visible, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(provider.enabled, Is.True);
            Assert.That(overlay.enabled, Is.False);
        }

        [Test]
        public void PausePreservesOtherMovementLocksAndAnInitiallyDisabledDesktopInput()
        {
            desktopInput.enabled = false;
            movement.SetLock(MovementLockReason.GameStartSequence, true);
            menu.Open();
            menu.Open(); // Repeated opens must not replace the saved input state.
            menu.Resume();
            Assert.That(provider.enabled, Is.False);
            Assert.That(movement.ActiveLocks, Is.EqualTo(MovementLockReason.GameStartSequence));
            Assert.That(desktopInput.enabled, Is.False);
            movement.SetLock(MovementLockReason.GameStartSequence, false);
            Assert.That(provider.enabled, Is.True);
        }

        [Test]
        public void ErrorsAndLeavingUseTheSameMenuInputState()
        {
            menu.Resume();
            Invoke(menu, "HandleError", "The room session is unavailable.");
            Assert.That(movement.ActiveLocks, Is.EqualTo(MovementLockReason.PauseMenu));
            Assert.That(Cursor.visible, Is.True);
            Assert.That(((Text)Get(menu, "statusText")).text, Does.Contain("unavailable"));
            menu.Resume();
            Invoke(menu, "HandleStateChanged", RoomSessionState.Leaving);
            Assert.That(movement.ActiveLocks, Is.EqualTo(MovementLockReason.PauseMenu));
            Assert.That(Cursor.visible, Is.True);
            Assert.That(((Button)Get(menu, "resumeButton")).interactable, Is.False);
            Assert.That(((Button)Get(menu, "leaveButton")).interactable, Is.False);
        }

        [Test]
        public void DisablingTheOwnerRestoresOverlaysAndReleasesOnlyItsOwnMovementLock()
        {
            interaction.SetTerminalActive(true);
            menu.Open();
            movement.SetLock(MovementLockReason.GameStartSequence, true);
            Invoke(interaction, "OnDisable");
            Assert.That(overlay.enabled, Is.True);
            Assert.That(disabledOverlay.enabled, Is.False);
            Assert.That(desktopInput.enabled, Is.True);
            Assert.That(movement.ActiveLocks, Is.EqualTo(MovementLockReason.GameStartSequence));
        }

        [Test]
        public void InterfacesCanDisableAfterTheInteractionOwnerIsDestroyed()
        {
            var view = Child("View").AddComponent<MeshupGameView>();
            Set(view, "interactionState", interaction);
            interaction.SetTerminalActive(true);
            menu.Open();
            Invoke(interaction, "OnDisable");
            Object.DestroyImmediate(interaction);

            Assert.DoesNotThrow(() => Invoke(menu, "OnDisable"));
            Assert.DoesNotThrow(() => Invoke(view, "OnDisable"));
            Assert.That(overlay.enabled, Is.True);
            Assert.That(desktopInput.enabled, Is.True);
            Assert.That(movement.ActiveLocks, Is.EqualTo(MovementLockReason.None));
        }

        private GameObject Child(string name, params System.Type[] components)
        {
            var child = new GameObject(name, components);
            child.transform.SetParent(root.transform);
            return child;
        }

        private GameObject Canvas(string name, RenderMode mode)
        {
            var child = Child(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            child.GetComponent<Canvas>().renderMode = mode;
            return child;
        }

        internal static void Set(Component target, string field, Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static object Get(object target, string field) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string method, params object[] arguments) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
    }
}
