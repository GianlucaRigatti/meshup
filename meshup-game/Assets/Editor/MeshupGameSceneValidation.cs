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
                "view", "generatorAnchor", "generatorParticles",
                "victoryFireworks", "correctGuessAudio", "transcriber",
                "generatorClient", "sizeSelector", "generatorActivityAudio"
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
            var view = (MeshupGameView)serialized.FindProperty("view").objectReferenceValue;
            var viewData = new SerializedObject(view);
            foreach (var field in new[] { "interactionState", "monitorCanvas", "terminalCanvas",
                "monitorFrontMount", "monitorBackMount", "localViewer", "leaderboard",
                "status", "listeningIndicator", "terminalTitle", "terminalStatus",
                "firstChoice", "secondChoice", "startButton", "firstChoiceLabel",
                "secondChoiceLabel", "startButtonLabel" })
            {
                var component = viewData.FindProperty(field).objectReferenceValue as Component;
                if (component == null || component.gameObject.scene != scene)
                    throw new InvalidOperationException($"Assign the game view's {field} in this scene.");
            }
            var monitorFront = (Transform)viewData.FindProperty("monitorFrontMount").objectReferenceValue;
            var fireworks = (MeshupVictoryFireworks)serialized.FindProperty("victoryFireworks").objectReferenceValue;
            var fireworksData = new SerializedObject(fireworks);
            var monitor = (Transform)fireworksData.FindProperty("guesserScreen").objectReferenceValue;
            if (monitor == null || monitor.gameObject.scene != scene)
                throw new InvalidOperationException("Victory fireworks must reference the authored monitor.");
            foreach (var field in new[] { "monitorFrontMount", "monitorBackMount" })
            {
                var mount = (Transform)viewData.FindProperty(field).objectReferenceValue;
                if (!mount.IsChildOf(monitor) || mount.lossyScale.sqrMagnitude < 0.000001f)
                    throw new InvalidOperationException($"{field} must be an authored mount on the monitor.");
            }
            var localPlayer = (PlayerMovementAuthority)serialized.FindProperty("localPlayer").objectReferenceValue;
            if (viewData.FindProperty("localViewer").objectReferenceValue != localPlayer.transform)
                throw new InvalidOperationException("The game view must reference the local player.");
            var prefabs = fireworksData.FindProperty("fireworks");
            if (prefabs.arraySize == 0)
                throw new InvalidOperationException("Assign the authored firework prefabs.");
            for (var i = 0; i < prefabs.arraySize; i++)
            {
                var particles = prefabs.GetArrayElementAtIndex(i).objectReferenceValue as ParticleSystem;
                if (particles == null || !PrefabUtility.IsPartOfPrefabAsset(particles))
                    throw new InvalidOperationException("Victory fireworks must reference saved particle prefabs.");
            }
            var monitorUi = (Transform)viewData.FindProperty("monitorCanvas").objectReferenceValue;
            var interaction = (GameInteractionState)viewData.FindProperty("interactionState").objectReferenceValue;
            var menu = scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<GameSessionMenu>(true)).Single();
            var interactionData = new SerializedObject(interaction);
            if (!interaction.isActiveAndEnabled || interaction.gameObject != coordinator.gameObject
                || new SerializedObject(menu).FindProperty("interactionState").objectReferenceValue != interaction
                || interactionData.FindProperty("movementAuthority").objectReferenceValue
                    != serialized.FindProperty("localPlayer").objectReferenceValue
                || interactionData.FindProperty("desktopInput").objectReferenceValue
                    != ((Component)serialized.FindProperty("localPlayer").objectReferenceValue)
                        .GetComponent<UbiqDemoPlayerInputGate>())
                throw new InvalidOperationException("The terminal and menu must share the authored player interaction state.");
            var overlays = interactionData.FindProperty("desktopOverlays");
            for (var i = 0; i < overlays.arraySize; i++)
            {
                var raycaster = overlays.GetArrayElementAtIndex(i).objectReferenceValue as GraphicRaycaster;
                if (raycaster == null || raycaster.gameObject.scene != scene
                    || raycaster.GetComponent<Canvas>().renderMode != RenderMode.ScreenSpaceOverlay
                    || raycaster.GetComponent<GameSessionMenu>() != null)
                    throw new InvalidOperationException("Suppress only authored desktop overlays, keeping menu and world-space raycasters available.");
            }
            var terminalUi = (Canvas)viewData.FindProperty("terminalCanvas").objectReferenceValue;
            var terminalMount = terminalUi.transform.parent;
            if (monitorUi.parent != monitorFront || terminalMount == null
                || terminalMount.parent == null || terminalMount.lossyScale.sqrMagnitude < 0.000001f)
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
            var selector = (GeneratedObjectSizeSelector)serialized.FindProperty("sizeSelector").objectReferenceValue;
            var selectorData = new SerializedObject(selector);
            var sizeLabels = new[]
            {
                "smallButton", "mediumButton", "extraLargeButton"
            }.Select(field => (GameObject)selectorData.FindProperty(field)
                .objectReferenceValue).ToArray();
            if (sizeLabels.Any(label => label == null || label.scene != scene
                    || label.GetComponent<ConsoleButtonFeedback>() == null
                    || label.GetComponent<Collider>() == null)
                || GeneratedObjectSizeSelector.FindPhysicalButtons(sizeLabels).Length != 3)
            {
                throw new InvalidOperationException(
                    "The size references must resolve to three integrated physical selector buttons.");
            }
            var generate = ((MeshupAssetGeneratorClient)serialized.FindProperty("generatorClient")
                .objectReferenceValue).gameObject;
            if (generate.GetComponent<ConsoleButtonFeedback>() == null
                || generate.GetComponent<Collider>() == null)
                throw new InvalidOperationException("Generate must reference the physical console button.");
            foreach (var field in new[] { "victoryFireworks", "transcriber", "sizeSelector",
                "correctGuessAudio", "generatorClient", "generatorActivityAudio" })
            {
                var component = (Behaviour)serialized.FindProperty(field).objectReferenceValue;
                var expectedOwner = field switch
                {
                    "correctGuessAudio" => monitor.gameObject,
                    "generatorClient" => generate,
                    "generatorActivityAudio" => ((Transform)serialized.FindProperty("generatorAnchor")
                        .objectReferenceValue).gameObject,
                    _ => coordinator.gameObject
                };
                if (!component.isActiveAndEnabled || component.gameObject != expectedOwner)
                    throw new InvalidOperationException($"{field} must be an active authored component on its game object.");
            }
            Debug.Log("Authored game runtime validation passed.");
        }
    }
}
