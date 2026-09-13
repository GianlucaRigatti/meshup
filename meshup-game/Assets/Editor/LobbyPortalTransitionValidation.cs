using System;
using System.Linq;
using Meshup.Lobby;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    /// <summary>Checks the authored scene without rebuilding or saving it.</summary>
    public static class LobbyPortalTransitionValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string RootName = "Lobby Portal Transition";

        [MenuItem("Meshup/Lobby/Validate Magic Portal Transition")]
        public static void Validate()
        {
            using var validation = new SceneValidationScope(ScenePath);
            var scene = validation.Scene;
            var root = RequireRoot(scene, RootName);
            var transition = root.GetComponent<LobbyPortalTransition>()
                ?? throw new InvalidOperationException("Portal transition component is missing.");
            var serialized = new SerializedObject(transition);

            var requiredReferences = new[]
            {
                "visualRoot", "cyanMaterial", "goldMaterial", "veilMaterial",
                "overlayCanvas", "overlayGroup", "overlayImage",
                "traditionalController", "roomUiCanvasGroup"
            };
            foreach (var propertyName in requiredReferences)
            {
                if (serialized.FindProperty(propertyName).objectReferenceValue == null)
                {
                    throw new InvalidOperationException(
                        $"Portal transition reference is missing: {propertyName}");
                }
            }

            var locomotion = serialized.FindProperty("locomotionBehaviours");
            if (locomotion.arraySize == 0)
            {
                throw new InvalidOperationException("No XR locomotion controls are gated.");
            }
            if (root.transform.parent != null || !root.activeSelf)
            {
                throw new InvalidOperationException("Portal transition must be an active scene root.");
            }

            Debug.Log("Magic portal validation passed: visuals, overlay, desktop input and XR locomotion are wired.");
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
