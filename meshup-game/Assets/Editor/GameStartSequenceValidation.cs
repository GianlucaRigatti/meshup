using System;
using System.Linq;
using Meshup.Game;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    /// <summary>Checks the authored scene without rebuilding or saving it.</summary>
    public static class GameStartSequenceValidation
    {
        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        private const string RootName = "Game Start Sequence";
        private const string ConsoleName = "Game starter";
        private const string FinishWallName = "Invisible_wall_game_start";

        [MenuItem("Meshup/Game/Validate Game Start Formation Walk")]
        public static void Validate()
        {
            using var validation = new SceneValidationScope(ScenePath);
            var scene = validation.Scene;
            var console = FindRequired(scene, ConsoleName);
            var root = FindRequired(scene, RootName);
            var player = FindRequired(scene, "Ubiq Demo Player");
            var route = root.GetComponent<GameStartRoute>();
            var regroupRing = root.GetComponent<GameStartRegroupRing>();
            var coordinator = root.GetComponent<GameStartCoordinator>();
            var authority = player.GetComponent<PlayerMovementAuthority>();
            var interaction = console.GetComponent<GameStartInteractable>();
            var screen = console.transform.Find("Game Start Screen");
            var eventSystem = FindRoot(scene, "EventSystem");
            var finishWall = FindRequired(scene, FinishWallName);
            var finishTrigger = finishWall.GetComponent<
                GameStartFinishTrigger>();
            var finishColliders = finishWall.GetComponents<Collider>();
            var doors = scene.GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<
                    GameStartDoorController>(true)).ToArray();

            if (route == null || route.WaypointCount < 5
                || regroupRing == null || regroupRing.WaypointCount < 6
                || regroupRing.Length < 20f
                || route.Length < 8f || coordinator == null
                || authority == null || interaction == null || screen == null
                || eventSystem == null
                || eventSystem.GetComponent<XRUIInputModule>() == null
                || finishTrigger == null || !finishTrigger.IsConfigured
                || finishColliders.Length == 0
                || finishColliders.Any(item => !item.isTrigger)
                || !console.GetComponentsInChildren<Collider>(true)
                    .Any(item => !item.isTrigger)
                || doors.Length == 0
                || doors.Any(item => !item.IsConfigured
                    || item.PathOffset > 2.5f)
                || screen.GetComponentInChildren<Button>(true) == null
                || screen.GetComponentInChildren<Text>(true) == null
                || screen.GetComponentInChildren<BoxCollider>(true) == null
                || screen.GetComponentInChildren<XRSimpleInteractable>(true)
                    == null
                || screen.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                throw new InvalidOperationException(
                    "The game start formation walk is incomplete.");
            }

            var first = root.transform.Find("Route/00 Lineup");
            var last = root.transform.Find("Route/10 Glass Room Destination");
            var ringContainer = root.transform.Find("Regroup Ring");
            if (first == null || last == null || ringContainer == null)
            {
                throw new InvalidOperationException(
                    "The staircase route endpoints are missing.");
            }

            Physics.SyncTransforms();
            foreach (var marker in ringContainer.Cast<Transform>())
            {
                // Validation can run alongside other open scenes. Only the
                // authored game scene's colliders determine its walkable floor.
                var hit = Physics.RaycastAll(marker.position + Vector3.up * 0.5f,
                        Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(item => item.collider.gameObject.scene == scene)
                    .OrderBy(item => item.distance).FirstOrDefault();
                if (hit.collider == null || Mathf.Abs(marker.position.y
                    - (hit.point.y - 0.08f)) > 0.12f)
                {
                    throw new InvalidOperationException(
                        $"Regroup ring marker is not on the waiting-room floor: "
                        + marker.name);
                }
            }

            foreach (var marker in root.transform.Find("Route")
                .Cast<Transform>())
            {
                var hits = Physics.RaycastAll(
                        marker.position + Vector3.up * 0.5f,
                        Vector3.down, 2f, ~0,
                        QueryTriggerInteraction.Ignore);
                var hit = hits
                    .Where(item => item.collider.gameObject.scene == scene
                        && !doors.Any(door =>
                        item.collider.transform.IsChildOf(door.transform)))
                    .OrderBy(item => Mathf.Abs(item.point.y
                        - (marker.position.y + 0.08f)))
                    .FirstOrDefault();
                if (hit.collider == null
                    || Mathf.Abs(marker.position.y - (hit.point.y - 0.08f))
                        > 0.12f)
                {
                    var hitDescriptions = string.Join(", ", hits.Select(
                        item => $"{item.collider.name}@{item.point.y:0.000}"));
                    throw new InvalidOperationException(
                        $"Route marker is not fitted to the walkable surface: "
                        + $"{marker.name}. Hits: {hitDescriptions}");
                }
            }

            Debug.Log($"Game start validation passed: {route.WaypointCount} "
                + $"waypoints over {route.Length:0.0} metres.");
        }

        private static GameObject FindRequired(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                var match = transforms.FirstOrDefault(item => item.name == name);
                if (match != null)
                {
                    return match.gameObject;
                }
            }
            throw new InvalidOperationException($"Required object not found: {name}");
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects()
                .FirstOrDefault(item => item.name == name);
        }
    }
}
