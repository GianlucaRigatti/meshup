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
                || lobbyUi.transform.Find("Room Totem Panel") != null
                || lobbyUi.transform.Find("Interaction Prompt") != null)
            {
                throw new InvalidOperationException("The automatic VR hologram UI still contains the old panel or E prompt.");
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
            if (panelData.FindProperty("ubiqMenuPrefab").objectReferenceValue == null)
            {
                throw new InvalidOperationException("The Ubiq lobby menu prefab is not assigned.");
            }
            var menuPrefab = panelData.FindProperty("ubiqMenuPrefab")
                .objectReferenceValue as GameObject;
            var menuPath = AssetDatabase.GetAssetPath(menuPrefab);
            if (!menuPath.StartsWith("Assets/Prefabs/Ubiq Sample UI/",
                    StringComparison.Ordinal)
                || menuPrefab.transform.Find("Canvas/Main Panel/Browse Panel")
                    == null
                || menuPrefab.transform.Find(
                    "Canvas/Main Panel/New Room Panel") == null
                || menuPrefab.transform.Find(
                    "Canvas/Main Panel/Join Room Panel") == null
                || menuPrefab.transform.Find(
                    "Canvas/Main Panel/Set Name Panel") == null)
            {
                throw new InvalidOperationException(
                    "The lobby must use the copied Ubiq sample menu prefab.");
            }
            if (panelData.FindProperty("allowClose").boolValue
                || panelData.FindProperty("lockPlayerInputWhenOpen").boolValue)
            {
                throw new InvalidOperationException("The persistent VR hologram must not close or lock locomotion.");
            }

            Debug.Log("Falling-book hologram validation passed with the Ubiq menu prefab.");
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(item => item.name == name)
                ?? throw new InvalidOperationException($"Required root is missing: {name}");
        }

    }
}
