using System;
using System.Linq;
using Meshup.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.EditorTools
{
    public static class MeshupGameSceneValidation
    {
        [MenuItem("Meshup/Game/Validate Authored Game Runtime")]
        public static void Validate()
        {
            using var validation = new SceneValidationScope(
                "Assets/Scenes/GameScene.unity");
            Validate(validation.Scene);
        }

        internal static void Validate(Scene scene)
        {
            var coordinators = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    MeshupGameCoordinator>(true)).ToArray();
            if (coordinators.Length != 1 || !coordinators[0].isActiveAndEnabled)
            {
                throw new InvalidOperationException(
                    "The game scene must contain exactly one active game coordinator.");
            }

            var coordinator = coordinators[0];
            var fps = coordinator.GetComponent<FpsCounter>();
            if (fps == null || !fps.enabled)
            {
                throw new InvalidOperationException("The game runtime needs its FPS counter.");
            }
            var serialized = new SerializedObject(coordinator);
            foreach (var field in new[]
            {
                "gameStart", "localPlayer", "invisibleWall", "guesserMonitor",
                "mimeTerminal", "generatorAnchor", "generatorButton", "generatorParticles",
                "smallSizeButton", "mediumSizeButton", "extraLargeSizeButton"
            })
            {
                var reference = serialized.FindProperty(field).objectReferenceValue;
                var target = reference is Component component
                    ? component.gameObject : reference as GameObject;
                if (target == null || target.scene != scene)
                {
                    throw new InvalidOperationException(
                        $"Assign the game coordinator's {field} to an object in this scene.");
                }
            }
            foreach (var field in new[]
            {
                "smallSizeButton", "mediumSizeButton", "extraLargeSizeButton"
            })
            {
                var target = (GameObject)serialized.FindProperty(field)
                    .objectReferenceValue;
                if (target.GetComponent<Collider>() == null
                    || target.GetComponent<XRSimpleInteractable>() == null)
                {
                    throw new InvalidOperationException(
                        $"The game coordinator's {field} needs a collider and XR interactable.");
                }
            }
            Debug.Log("Authored game runtime validation passed.");
        }
    }
}
