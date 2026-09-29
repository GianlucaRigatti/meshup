using System;
using System.Linq;
using Meshup.Lobby;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    /// <summary>Checks the authored scene without rebuilding or saving it.</summary>
    public static class LobbyBedroomValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        [MenuItem("Meshup/Lobby/Validate Lobby Interaction")]
        public static void ValidateLobby()
        {
            using var validation = new SceneValidationScope(ScenePath);
            var scene = validation.Scene;
            var player = RequireRoot(scene, "Lobby Player");
            var camera = player.GetComponentInChildren<Camera>(true);
            var totem = RequireRoot(scene, "Room Totem");
            var lobbyUi = RequireRoot(scene, "Lobby UI");
            var networkScene = RequireRoot(scene, "Ubiq Network Scene");
            ValidateNetworkSceneForReload(networkScene);

            var characterController = player.GetComponent<CharacterController>();
            var firstPersonController = player.GetComponent<LobbyFirstPersonController>();
            if (camera == null || characterController == null || firstPersonController == null)
            {
                throw new InvalidOperationException("The lobby player implementation is incomplete.");
            }

            var controllerData = new SerializedObject(firstPersonController);
            if (controllerData.FindProperty("viewCamera").objectReferenceValue != camera.transform)
            {
                throw new InvalidOperationException("The first-person controller lost its camera reference.");
            }

            var interaction = totem.GetComponent<RoomTotemInteraction>();
            var trigger = totem.GetComponent<SphereCollider>();
            var rigidbody = totem.GetComponent<Rigidbody>();
            if (interaction == null || trigger == null || !trigger.isTrigger
                || rigidbody == null || !rigidbody.isKinematic)
            {
                throw new InvalidOperationException("The existing token interaction is incomplete.");
            }

            var interactionData = new SerializedObject(interaction);
            if (interactionData.FindProperty("interactionPrompt").objectReferenceValue == null
                || interactionData.FindProperty("panel").objectReferenceValue == null
                || lobbyUi.GetComponent<RoomTotemPanel>() == null)
            {
                throw new InvalidOperationException("The token lost one or more existing menu references.");
            }

            Debug.Log("Lobby interaction validation passed.");
        }

        private static void ValidateNetworkSceneForReload(GameObject networkScene)
        {
            if (FindTransform(networkScene.transform, "Spawn Manager").gameObject.activeSelf)
            {
                throw new InvalidOperationException(
                    "The lobby Spawn Manager must remain inactive for safe scene reloads.");
            }
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return FindRoot(scene, name)
                   ?? throw new InvalidOperationException($"Required lobby root was not found: {name}");
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
        }

        private static Transform FindTransform(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                       .FirstOrDefault(item => item.name == name)
                   ?? throw new InvalidOperationException($"Required lobby object was not found: {name}");
        }
    }
}
