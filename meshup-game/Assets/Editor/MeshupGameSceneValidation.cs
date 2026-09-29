using System;
using System.Linq;
using Meshup.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

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
                "gameStart", "localPlayer", "invisibleWall", "mimeZoneDivider",
                "guesserMonitor", "view",
                "monitorUiFrontMount", "monitorUiBackMount", "mimeTerminal",
                "terminalUiMount", "generatorAnchor", "generatorButton", "generatorParticles",
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
            var divider = (Transform)serialized.FindProperty("mimeZoneDivider")
                .objectReferenceValue;
            if (divider.GetComponents<Collider>().Length != 0)
            {
                throw new InvalidOperationException(
                    "The visual mime zone divider must not have a collider.");
            }
            var monitor = (Transform)serialized.FindProperty("guesserMonitor")
                .objectReferenceValue;
            var terminal = (Transform)serialized.FindProperty("mimeTerminal")
                .objectReferenceValue;
            foreach (var field in new[] { "monitorUiFrontMount", "monitorUiBackMount" })
            {
                var mount = (Transform)serialized.FindProperty(field)
                    .objectReferenceValue;
                if (!mount.IsChildOf(monitor) || mount.lossyScale.sqrMagnitude < 0.000001f)
                {
                    throw new InvalidOperationException($"{field} must be an authored mount on the monitor.");
                }
            }
            var terminalMount = (Transform)serialized.FindProperty("terminalUiMount")
                .objectReferenceValue;
            if (!terminalMount.IsChildOf(terminal)
                || terminalMount.lossyScale.sqrMagnitude < 0.000001f)
            {
                throw new InvalidOperationException(
                    "terminalUiMount must be an authored mount on the terminal.");
            }
            var view = (MeshupGameView)serialized.FindProperty("view").objectReferenceValue;
            var viewData = new SerializedObject(view);
            foreach (var field in new[] { "monitorCanvas", "terminalCanvas", "leaderboard",
                "status", "listeningIndicator", "terminalTitle", "terminalStatus",
                "firstChoice", "secondChoice", "startButton", "firstChoiceLabel",
                "secondChoiceLabel", "startButtonLabel" })
            {
                var component = viewData.FindProperty(field).objectReferenceValue as Component;
                if (component == null || component.gameObject.scene != scene)
                    throw new InvalidOperationException($"Assign the game view's {field} in this scene.");
            }
            var monitorUi = (Transform)viewData.FindProperty("monitorCanvas").objectReferenceValue;
            var terminalUi = (Canvas)viewData.FindProperty("terminalCanvas").objectReferenceValue;
            if (monitorUi.parent != (Transform)serialized.FindProperty("monitorUiFrontMount").objectReferenceValue
                || terminalUi.transform.parent != terminalMount)
                throw new InvalidOperationException("The authored game UI must remain on its display mounts.");
            foreach (var canvas in new[] { monitorUi.GetComponent<Canvas>(), terminalUi })
            {
                if (canvas.renderMode != RenderMode.WorldSpace
                    || canvas.GetComponent<GraphicRaycaster>() == null
                    || canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                    throw new InvalidOperationException("Game UI requires desktop and tracked-device raycasters.");
            }
            foreach (var field in new[] { "firstChoice", "secondChoice", "startButton" })
            {
                var button = (Button)viewData.FindProperty(field).objectReferenceValue;
                if (button.GetComponent<BoxCollider>() == null
                    || button.GetComponent<XRSimpleInteractable>() == null)
                    throw new InvalidOperationException($"{field} needs its authored XR interaction and collider.");
            }
            var sizeLabels = new[]
            {
                "smallSizeButton", "mediumSizeButton", "extraLargeSizeButton"
            }.Select(field => (GameObject)serialized.FindProperty(field)
                .objectReferenceValue).ToArray();
            if (sizeLabels.Any(label => label.GetComponent<ConsoleButtonFeedback>() == null
                    || label.GetComponent<Collider>() == null)
                || GeneratedObjectSizeSelector.FindPhysicalButtons(sizeLabels).Length != 3)
            {
                throw new InvalidOperationException(
                    "The size references must resolve to three integrated physical selector buttons.");
            }
            var generate = (GameObject)serialized.FindProperty("generatorButton").objectReferenceValue;
            if (generate.GetComponent<ConsoleButtonFeedback>() == null
                || generate.GetComponent<Collider>() == null)
                throw new InvalidOperationException("Generate must reference the physical console button.");
            Debug.Log("Authored game runtime validation passed.");
        }
    }
}
