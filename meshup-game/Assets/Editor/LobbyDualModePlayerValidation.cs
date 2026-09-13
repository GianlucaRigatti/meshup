using System;
using System.Linq;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    /// <summary>Checks the authored scene without rebuilding or saving it.</summary>
    public static class LobbyDualModePlayerValidation
    {
        private const string LobbyScenePath = "Assets/Scenes/SampleScene.unity";
        private const string RigRootName = "Lobby Dual Mode Controls";

        [MenuItem("Meshup/Lobby/Validate Ubiq Desktop + VR Controls")]
        public static void Validate()
        {
            using var validation = new SceneValidationScope(LobbyScenePath);
            var lobbyScene = validation.Scene;
            var legacyPlayer = RequireRoot(lobbyScene, "Lobby Player");
            var rigRoot = RequireRoot(lobbyScene, RigRootName);
            var ubiqPlayer = RequireChild(rigRoot.transform, "Ubiq Demo Player").gameObject;
            var avatarInput = RequireChild(rigRoot.transform, "Ubiq Avatar Input (XRI)").gameObject;
            var interactionManager = RequireChild(rigRoot.transform, "XR Interaction Manager").gameObject;
            var lobbyUi = RequireRoot(lobbyScene, "Lobby UI");

            if (legacyPlayer.activeSelf || !rigRoot.activeSelf || !ubiqPlayer.activeSelf
                || !avatarInput.activeSelf || !interactionManager.activeSelf)
            {
                throw new InvalidOperationException("The Ubiq rig is not the sole active lobby player.");
            }

            if (!HasComponentNamed(ubiqPlayer, "XROrigin")
                || ubiqPlayer.GetComponentInChildren<Camera>(true) == null
                || ubiqPlayer.GetComponentsInChildren<Transform>(true)
                    .All(item => item.name != "Traditional Controller")
                || ubiqPlayer.GetComponentsInChildren<Transform>(true)
                    .All(item => item.name != "Near-Far Interactor"))
            {
                throw new InvalidOperationException("The copied player is missing desktop or XRI control components.");
            }

            if (lobbyUi.GetComponents<Component>()
                .All(component => component == null
                    || component.GetType().Name != "TrackedDeviceGraphicRaycaster"))
            {
                throw new InvalidOperationException("The hologram cannot receive tracked-device UI rays.");
            }

            var eventSystem = RequireRoot(lobbyScene, "EventSystem");
            if (eventSystem.GetComponent<XRUIInputModule>() == null
                || eventSystem.GetComponents<BaseInputModule>()
                    .Any(module => module is not XRUIInputModule))
            {
                throw new InvalidOperationException(
                    "The lobby EventSystem must use XRUIInputModule without a competing input module.");
            }

            Debug.Log("Lobby dual-mode validation passed: desktop controller, XR Origin, controller rays and hologram raycaster are present.");
        }

        private static bool HasComponentNamed(GameObject root, string typeName)
        {
            return root.GetComponentsInChildren<Component>(true)
                .Any(component => component != null && component.GetType().Name == typeName);
        }

        private static Transform RequireChild(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == name)
                ?? throw new InvalidOperationException($"Required child is missing: {name}");
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(item => item.name == name);
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return FindRoot(scene, name)
                ?? throw new InvalidOperationException($"Required root is missing: {name}");
        }
    }
}
