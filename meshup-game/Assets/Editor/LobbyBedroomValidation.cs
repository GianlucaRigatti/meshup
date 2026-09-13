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
        private const string EnvironmentName = "Bedroom Environment";
        private static readonly (string Collider, string Model)[] FurnitureColliderPairs =
        {
            ("Bed Collision", "Kenney Child Bed"),
            ("Bedside Collision", "Kenney Bedside Table"),
            ("Desk Collision", "Kenney Drawing Desk"),
            ("Bookcase Collision", "Kenney Bookcase"),
            ("Toy Box Collision", "Kenney Toy Box")
        };

        [MenuItem("Meshup/Lobby/Validate Cozy Bedroom")]
        public static void ValidateLobby()
        {
            using var validation = new SceneValidationScope(ScenePath);
            var scene = validation.Scene;
            var player = RequireRoot(scene, "Lobby Player");
            var camera = player.GetComponentInChildren<Camera>(true);
            var totem = RequireRoot(scene, "Room Totem");
            var environment = RequireRoot(scene, EnvironmentName);
            var lobbyUi = RequireRoot(scene, "Lobby UI");
            var networkScene = RequireRoot(scene, "Ubiq Network Scene");
            ValidateNetworkSceneForReload(networkScene);

            if (camera == null || camera.transform.localPosition != new Vector3(0f, 1.6f, 0f))
            {
                throw new InvalidOperationException("The existing lobby camera hierarchy or height changed.");
            }

            if (Mathf.Abs(camera.fieldOfView - 65f) > 0.001f)
            {
                throw new InvalidOperationException("The existing lobby camera FOV changed.");
            }

            var characterController = player.GetComponent<CharacterController>();
            var firstPersonController = player.GetComponent<LobbyFirstPersonController>();
            if (characterController == null || firstPersonController == null)
            {
                throw new InvalidOperationException("The lobby player implementation is incomplete.");
            }

            if (Mathf.Abs(characterController.height - 1.8f) > 0.001f
                || Mathf.Abs(characterController.radius - 0.35f) > 0.001f)
            {
                throw new InvalidOperationException("The existing CharacterController dimensions changed.");
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
                || rigidbody == null || !rigidbody.isKinematic
                || totem.transform.Find("Magic Storybook") == null)
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

            var importedModels = environment.GetComponentsInChildren<MeshRenderer>(true)
                .Count(renderer => renderer.gameObject.name.StartsWith("Kenney ", StringComparison.Ordinal));
            if (importedModels < 10)
            {
                throw new InvalidOperationException("Expected bedroom furniture was not created.");
            }

            foreach (var pair in FurnitureColliderPairs)
            {
                var colliderPosition = FindTransform(environment.transform, pair.Collider).position;
                var modelCenter = CalculateBounds(FindTransform(environment.transform, pair.Model).gameObject).center;
                if (Vector3.Distance(colliderPosition, modelCenter) > 0.001f)
                {
                    throw new InvalidOperationException($"{pair.Collider} is not aligned with {pair.Model}.");
                }
            }

            Debug.Log($"Lobby validation passed with {importedModels} imported model renderers.");
        }

        private static void ValidateNetworkSceneForReload(GameObject networkScene)
        {
            if (FindTransform(networkScene.transform, "Spawn Manager").gameObject.activeSelf)
            {
                throw new InvalidOperationException(
                    "The lobby Spawn Manager must remain inactive for safe scene reloads.");
            }
        }

        private static Bounds CalculateBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(instance.transform.position, Vector3.zero);
            }
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
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
                   ?? throw new InvalidOperationException($"Required bedroom object was not found: {name}");
        }
    }
}
