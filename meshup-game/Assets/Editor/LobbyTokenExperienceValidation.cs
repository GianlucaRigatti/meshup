using System;
using System.Linq;
using Meshup.Lobby;
using UnityEngine.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    /// <summary>Checks the authored scene without rebuilding or saving it.</summary>
    public static class LobbyTokenExperienceValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Meshup/Lobby/Validate Falling Book Hologram")]
        public static void ValidateExperience()
        {
            using var validation = new SceneValidationScope(ScenePath);
            var scene = validation.Scene;
            var player = RequireRoot(scene, "Lobby Player");
            var totem = RequireRoot(scene, "Room Totem");
            var lobbyUi = RequireRoot(scene, "Lobby UI");
            var reveal = totem.GetComponent<FallingBookReveal>();
            var interaction = totem.GetComponent<RoomTotemInteraction>();
            var panel = lobbyUi.GetComponent<RoomTotemPanel>();
            var billboard = lobbyUi.GetComponent<HologramBillboard>();
            var canvas = lobbyUi.GetComponent<Canvas>();

            if (reveal == null || interaction == null || panel == null || billboard == null
                || canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            {
                throw new InvalidOperationException("The falling-book hologram runtime is incomplete.");
            }
            if (totem.transform.Find("Magic Storybook/Flight Book") == null
                || totem.transform.Find("Magic Storybook/Open Book") == null
                || totem.transform.Find("Magic Storybook/Open Book/Hologram Effects") == null
                || totem.transform.Find("Magic Storybook/Hologram Anchor") == null)
            {
                throw new InvalidOperationException("The animated book geometry is incomplete.");
            }
            if (lobbyUi.transform.Find("Hologram Idle") == null
                || lobbyUi.transform.Find("Room Totem Panel") == null
                || lobbyUi.transform.Find("Interaction Prompt") != null)
            {
                throw new InvalidOperationException("The automatic VR hologram UI is incomplete or still requires E.");
            }

            var revealData = new SerializedObject(reveal);
            var landing = revealData.FindProperty("landingPosition").vector3Value;
            var shelf = revealData.FindProperty("shelfPosition").vector3Value;
            if (shelf.y < 1f || landing.y > 0.2f || Vector3.Distance(shelf, landing) < 1.5f
                || revealData.FindProperty("panel").objectReferenceValue != panel
                || revealData.FindProperty("lobbyPlayer").objectReferenceValue
                    != player.GetComponent<LobbyFirstPersonController>())
            {
                throw new InvalidOperationException("The automatic reveal trajectory or menu opening is not configured.");
            }

            var panelData = new SerializedObject(panel);
            foreach (var field in new[]
                     {
                         "panelRoot", "usernameInput", "roomNameText", "createButton", "refreshButton", "closeButton",
                         "roomListContent", "roomListItemTemplate", "statusText", "noRoomsMessage"
                     })
            {
                if (panelData.FindProperty(field).objectReferenceValue == null)
                {
                    throw new InvalidOperationException($"The hologram panel field is missing: {field}");
                }
            }
            if (panelData.FindProperty("allowClose").boolValue
                || panelData.FindProperty("lockPlayerInputWhenOpen").boolValue
                || uiHasActiveCloseButton(lobbyUi.transform))
            {
                throw new InvalidOperationException("The persistent VR hologram must not close or lock locomotion.");
            }

            var usernameInput = lobbyUi.GetComponentInChildren<InputField>(true);
            if (usernameInput == null || !usernameInput.interactable
                || usernameInput.characterLimit < 20)
            {
                throw new InvalidOperationException("The VR hologram needs a ray-interactable username field.");
            }

            var buttons = lobbyUi.GetComponentsInChildren<Button>(true);
            if (buttons.Length < 4 || buttons.Any(button => button.GetComponent<RectTransform>().rect.height < 54f))
            {
                throw new InvalidOperationException("The VR hologram needs large ray-friendly button targets.");
            }

            Debug.Log($"Falling-book hologram validation passed with {buttons.Length} large UI targets.");
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(item => item.name == name)
                ?? throw new InvalidOperationException($"Required root is missing: {name}");
        }

        private static bool uiHasActiveCloseButton(Transform root)
        {
            return root.GetComponentsInChildren<Button>(false)
                .Any(button => button.gameObject.name.Contains("Close", StringComparison.OrdinalIgnoreCase));
        }
    }
}
