using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    /// <summary>
    /// Reuses the project's known-good Ubiq/XRI player from GameScene in the
    /// local lobby. The Ubiq demo rig selects motion controllers when an HMD is
    /// present and its Traditional Controller supplies the desktop fallback.
    /// </summary>
    public static class LobbyDualModePlayerBuilder
    {
        private const string LobbyScenePath = "Assets/Scenes/SampleScene.unity";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string RigRootName = "Lobby Dual Mode Controls";

        private static readonly string[] SourceRootNames =
        {
            "Ubiq Demo Player",
            "XR Interaction Manager",
            "Ubiq Avatar Input (XRI)"
        };

        [MenuItem("Meshup/Lobby/Install Ubiq Desktop + VR Controls")]
        public static void Build()
        {
            var lobbyScene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
            var legacyPlayer = RequireRoot(lobbyScene, "Lobby Player");
            var spawnPosition = legacyPlayer.transform.position;
            var spawnRotation = legacyPlayer.transform.rotation;

            var oldRig = FindRoot(lobbyScene, RigRootName);
            if (oldRig != null)
            {
                UnityEngine.Object.DestroyImmediate(oldRig);
            }

            var gameScene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
            var sourceRoots = SourceRootNames
                .Select(name => RequireRoot(gameScene, name))
                .ToArray();

            var sourceContainer = new GameObject("__Lobby Dual Mode Copy Source");
            SceneManager.MoveGameObjectToScene(sourceContainer, gameScene);
            foreach (var root in sourceRoots)
            {
                root.transform.SetParent(sourceContainer.transform, true);
            }

            var rigRoot = UnityEngine.Object.Instantiate(sourceContainer);
            rigRoot.name = RigRootName;
            SceneManager.MoveGameObjectToScene(rigRoot, lobbyScene);

            foreach (var root in sourceRoots)
            {
                root.transform.SetParent(null, true);
            }
            UnityEngine.Object.DestroyImmediate(sourceContainer);
            EditorSceneManager.CloseScene(gameScene, true);

            var ubiqPlayer = RequireChild(rigRoot.transform, "Ubiq Demo Player").gameObject;
            ubiqPlayer.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            ubiqPlayer.SetActive(true);

            var activeCamera = ubiqPlayer.GetComponentInChildren<Camera>(true)
                ?? throw new InvalidOperationException("The copied Ubiq player has no camera.");
            activeCamera.gameObject.tag = "MainCamera";
            activeCamera.gameObject.SetActive(true);

            // The Ubiq rig already contains its desktop and XR input paths. The
            // old controller must remain inactive to avoid two cameras and two
            // locomotion systems processing input at the same time.
            legacyPlayer.SetActive(false);

            var lobbyUi = RequireRoot(lobbyScene, "Lobby UI");
            AddTrackedDeviceRaycaster(lobbyUi);

            EditorSceneManager.MarkSceneDirty(lobbyScene);
            EditorSceneManager.SaveScene(lobbyScene, LobbyScenePath);
            AssetDatabase.SaveAssets();

            LobbyPortalTransitionBuilder.Build();
            Validate();
            Debug.Log("Lobby dual-mode controls installed: Ubiq desktop fallback plus XRI VR locomotion and UI rays.");
        }

        public static void BuildFromCommandLine()
        {
            Build();
        }

        [MenuItem("Meshup/Lobby/Validate Ubiq Desktop + VR Controls")]
        public static void Validate()
        {
            var lobbyScene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
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
            if (eventSystem.GetComponent<BaseInputModule>() == null)
            {
                throw new InvalidOperationException("The lobby EventSystem has no input module.");
            }

            Debug.Log("Lobby dual-mode validation passed: desktop controller, XR Origin, controller rays and hologram raycaster are present.");
        }

        private static void AddTrackedDeviceRaycaster(GameObject canvasObject)
        {
            if (canvasObject.GetComponents<Component>()
                .Any(component => component != null
                    && component.GetType().Name == "TrackedDeviceGraphicRaycaster"))
            {
                return;
            }

            var raycasterType = TypeCache.GetTypesDerivedFrom<BaseRaycaster>()
                .FirstOrDefault(type => type.Name == "TrackedDeviceGraphicRaycaster")
                ?? throw new InvalidOperationException(
                    "XR Interaction Toolkit's TrackedDeviceGraphicRaycaster is unavailable.");
            canvasObject.AddComponent(raycasterType);
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
