using System;
using System.Collections.Generic;
using System.Linq;
using Meshup.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Meshup.Editor
{
    public static class GameStartSequenceBuilder
    {
        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        private const string RootName = "Game Start Sequence";
        private const string ConsoleName = "Game starter";
        private const string FinishWallName = "Invisible_wall_game_start";

        // These points follow the staircase corridor from the spawn deck into
        // the glass room. They remain ordinary scene Transforms so designers
        // can tune them without touching runtime code.
        private static readonly Vector3[] DefaultRoute =
        {
            new(20.20f, 0.69f, 1.37f),
            new(16.95f, 0.539f, 1.37f),
            new(16.45f, 0.877f, 1.37f),
            new(15.80f, 1.439f, 1.37f),
            new(14.85f, 2.282f, 1.37f),
            new(12.70f, 2.412f, 1.37f),
            new(8.50f, 2.402f, 1.37f),
            new(4.50f, 2.412f, 1.37f),
            new(0.00f, 2.412f, 1.37f),
            new(-5.50f, 2.412f, 1.37f),
            new(-13.00f, 2.385f, 1.37f)
        };

        // Inset from the stepped waiting-room walls and its fixed props. Point
        // zero is the exit into the lineup; the loop continues around the room
        // in clockwise order.
        private static readonly Vector3[] DefaultRegroupRing =
        {
            new(20.20f, 0.69f, 1.37f),
            new(20.20f, 0.69f, 1.50f),
            new(35.00f, 0.69f, 1.50f),
            new(35.00f, 0.69f, 10.00f),
            new(32.00f, 0.69f, 10.00f),
            new(32.00f, 0.69f, 14.00f),
            new(22.70f, 0.69f, 14.00f),
            new(22.70f, 0.69f, 6.00f),
            new(22.70f, 0.69f, 1.50f),
            new(18.80f, 0.69f, 1.50f)
        };

        [MenuItem("Meshup/Game/Install Game Start Formation Walk")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath,
                OpenSceneMode.Single);
            var console = FindRequired(scene, ConsoleName);
            var playerObject = FindRequired(scene, "Ubiq Demo Player");
            var sessionMenuObject = FindRequired(scene, "Game Session UI");
            var finishWall = FindRequired(scene, FinishWallName);

            var root = FindRoot(scene, RootName) ?? new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetPositionAndRotation(Vector3.zero,
                Quaternion.identity);

            var route = GetOrAdd<GameStartRoute>(root);
            var waypoints = BuildRoute(root.transform);
            route.Configure(waypoints, 0.9f, 1.25f);
            var regroupRing = GetOrAdd<GameStartRegroupRing>(root);
            regroupRing.Configure(BuildRegroupRing(root.transform));

            var authority = GetOrAdd<PlayerMovementAuthority>(playerObject);
            authority.Configure(FindTranslationProviders(playerObject));

            var doors = BuildDoorControllers(scene, route);
            var coordinator = GetOrAdd<GameStartCoordinator>(root);
            coordinator.Configure(route, regroupRing, authority, doors);
            var finishTrigger = GetOrAdd<GameStartFinishTrigger>(finishWall);
            ConfigureFinishTrigger(finishWall, finishTrigger, coordinator,
                authority);

            var screen = BuildTokenScreen(console.transform);
            var oldInteractable = console.GetComponent<
                UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            if (oldInteractable != null)
            {
                UnityEngine.Object.DestroyImmediate(oldInteractable);
            }
            var interaction = GetOrAdd<GameStartInteractable>(console);
            interaction.Configure(coordinator, authority, screen.Root,
                screen.Button, screen.Label);
            var eventSystem = EnsureEventSystem(scene);

            var sessionMenu = sessionMenuObject.GetComponent<GameSessionMenu>()
                ?? throw new InvalidOperationException(
                    "Game Session UI has no GameSessionMenu.");
            sessionMenu.SetMovementAuthority(authority);

            MarkDirty(route, regroupRing, authority, coordinator, finishTrigger,
                interaction, sessionMenu, eventSystem, root, console,
                finishWall);
            MarkDirty(doors);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Game start formation walk installed.");
        }

        [MenuItem("Meshup/Game/Validate Game Start Formation Walk")]
        public static void Validate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath,
                OpenSceneMode.Single);
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
                var found = Physics.Raycast(marker.position + Vector3.up * 0.5f,
                    Vector3.down, out var hit, 2f, ~0,
                    QueryTriggerInteraction.Ignore);
                if (!found || Mathf.Abs(marker.position.y
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
                    .Where(item => !doors.Any(door =>
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

        private static void ConfigureFinishTrigger(GameObject finishWall,
            GameStartFinishTrigger trigger, GameStartCoordinator coordinator,
            PlayerMovementAuthority authority)
        {
            foreach (var collider in finishWall.GetComponents<Collider>())
            {
                collider.isTrigger = true;
                if (collider is BoxCollider box)
                {
                    var size = box.size;
                    // A zero-thickness BoxCollider can miss fast
                    // CharacterController crossings. Keep the authored plane
                    // but give it a small, invisible trigger depth.
                    size.z = Mathf.Max(0.25f, size.z);
                    box.size = size;
                }
                MarkDirty(collider);
            }
            trigger.Configure(coordinator, authority);
        }

        [MenuItem("Meshup/Game/Capture Game Start Route Preview")]
        public static void CaptureRoutePreview()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath,
                OpenSceneMode.Single);
            var root = FindRequired(scene, RootName);
            var markers = root.transform.Find("Route")
                ?.Cast<Transform>().ToArray() ?? Array.Empty<Transform>();
            if (markers.Length < 2)
            {
                throw new InvalidOperationException("Route markers are missing.");
            }

            var temporary = new GameObject("__Route Preview");
            var material = new Material(Shader.Find(
                "Universal Render Pipeline/Unlit"));
            material.color = Color.magenta;
            var line = temporary.AddComponent<LineRenderer>();
            line.material = material;
            line.startWidth = 0.18f;
            line.endWidth = 0.18f;
            line.positionCount = markers.Length;
            line.SetPositions(markers.Select(item => item.position).ToArray());

            foreach (var marker in markers)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(temporary.transform, true);
                sphere.transform.position = marker.position + Vector3.up * 0.18f;
                sphere.transform.localScale = Vector3.one * 0.42f;
                sphere.GetComponent<Renderer>().sharedMaterial = material;
            }

            var cameraObject = new GameObject("__Route Preview Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var center = markers.Aggregate(Vector3.zero,
                (sum, item) => sum + item.position) / markers.Length;
            camera.transform.position = center + new Vector3(13f, 16f, 13f);
            camera.transform.LookAt(center + Vector3.up * 1.5f);
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            var texture = new RenderTexture(1400, 900, 24);
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height,
                TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height),
                0, 0);
            image.Apply();
            var path = "/tmp/meshup-game-start-route.png";
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());

            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(temporary);
            UnityEngine.Object.DestroyImmediate(material);
            Debug.Log($"Game start route preview saved to {path}");
        }

        [MenuItem("Meshup/Game/Capture Starter Token Preview")]
        public static void CaptureStarterTokenPreview()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath,
                OpenSceneMode.Single);
            var console = FindRequired(scene, ConsoleName).transform;
            var cameraObject = new GameObject("__Starter Token Preview Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = console.TransformPoint(
                new Vector3(0f, 1.55f, 3f));
            camera.transform.LookAt(console.TransformPoint(
                new Vector3(0f, 1.45f, 0f)), console.up);
            camera.fieldOfView = 32f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 30f;

            var texture = new RenderTexture(900, 900, 24);
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height,
                TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height),
                0, 0);
            image.Apply();
            var path = "/tmp/meshup-starter-token.png";
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());

            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            Debug.Log($"Starter token preview saved to {path}");
        }

        public static void DumpRouteGeometry()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath,
                OpenSceneMode.Single);
            var sequence = FindRequired(scene, RootName);
            var routePoints = sequence.transform.Find("Route")
                ?.Cast<Transform>().Select(item => item.position).ToArray()
                ?? Array.Empty<Vector3>();
            Debug.Log("ROUTE POINTS\n" + string.Join("\n",
                routePoints.Select((point, index) => $"{index}: {point}")));
            Physics.SyncTransforms();
            Debug.Log("ROUTE GROUND\n" + string.Join("\n",
                routePoints.Select((point, index) =>
                {
                    var found = Physics.Raycast(point + Vector3.up * 0.5f,
                        Vector3.down, out var hit, 2f, ~0,
                        QueryTriggerInteraction.Ignore);
                    return found
                        ? $"{index}: y={hit.point.y:0.000} hit={hit.collider.name}"
                        : $"{index}: no ground";
                })));
            Debug.Log($"PLAYER {FindRequired(scene, "Ubiq Demo Player").transform.position}");
            Debug.Log($"STARTER {FindRequired(scene, ConsoleName).transform.position}");

            var colliders = scene.GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<Collider>(true))
                .Where(item => item.bounds.size.sqrMagnitude > 0.1f)
                .OrderBy(item => Vector3.Distance(item.bounds.center,
                    routePoints.Length > 0 ? routePoints[0] : Vector3.zero))
                .Take(120)
                .Select(item => $"{item.name}|center={item.bounds.center}|size={item.bounds.size}");
            Debug.Log("NEARBY COLLIDERS\n" + string.Join("\n", colliders));
        }

        [MenuItem("Meshup/Game/Dump Corridor Floor Map")]
        public static void DumpCorridorFloorMap()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();

            const float minimumFloor = 1.8f;
            const float maximumFloor = 2.5f;
            var rows = new List<string>();
            for (var z = 16; z >= -16; z--)
            {
                var cells = new char[66];
                for (var column = 0; column < cells.Length; column++)
                {
                    var x = 20 - column;
                    var origin = new Vector3(x, maximumFloor + 0.5f, z);
                    cells[column] = Physics.Raycast(origin, Vector3.down,
                            out var hit, 1.5f, ~0,
                            QueryTriggerInteraction.Ignore)
                        && hit.point.y >= minimumFloor
                        && hit.point.y <= maximumFloor
                        ? '#'
                        : '.';
                }
                rows.Add($"z={z,3} {new string(cells)}");
            }

            Debug.Log("CORRIDOR FLOOR MAP (x 20 to -45, # = walkable near y 2.1)\n"
                + string.Join("\n", rows));
        }

        private static Transform[] BuildRoute(Transform root)
        {
            var container = root.Find("Route");
            if (container == null)
            {
                container = new GameObject("Route").transform;
                container.SetParent(root, false);
            }

            var names = new[]
            {
                "00 Lineup",
                "01 Stair Foot",
                "02 Lower Stairs",
                "03 Upper Stairs",
                "04 Stair Landing",
                "05 Upper Hall",
                "06 Connector Approach",
                "07 Connector Entry",
                "08 Connector Middle",
                "09 Glass Room Entry",
                "10 Glass Room Destination"
            };
            var result = new Transform[DefaultRoute.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var prefix = $"{i:00} ";
                var marker = container.Cast<Transform>().FirstOrDefault(
                    item => item.name.StartsWith(prefix,
                        StringComparison.Ordinal));
                if (marker == null)
                {
                    marker = new GameObject(names[i]).transform;
                    marker.SetParent(container, true);
                }
                marker.name = names[i];
                marker.position = DefaultRoute[i];
                result[i] = marker;
                EditorUtility.SetDirty(marker);
            }
            return result;
        }

        private static Transform[] BuildRegroupRing(Transform root)
        {
            var container = root.Find("Regroup Ring");
            if (container == null)
            {
                container = new GameObject("Regroup Ring").transform;
                container.SetParent(root, false);
            }

            var result = new Transform[DefaultRegroupRing.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var prefix = $"{i:00} ";
                var marker = container.Cast<Transform>().FirstOrDefault(
                    item => item.name.StartsWith(prefix,
                        StringComparison.Ordinal));
                if (marker == null)
                {
                    marker = new GameObject().transform;
                    marker.SetParent(container, true);
                }
                marker.name = i == 0
                    ? "00 Lineup Exit"
                    : $"{i:00} Perimeter";
                marker.position = DefaultRegroupRing[i];
                result[i] = marker;
                EditorUtility.SetDirty(marker);
            }
            return result;
        }

        private static (GameObject Root, Button Button, Text Label)
            BuildTokenScreen(Transform console)
        {
            var screen = console.Find("Game Start Screen")?.gameObject
                ?? console.Find("Game Start Prompt")?.gameObject;
            if (screen == null)
            {
                screen = new GameObject("Game Start Screen",
                    typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster),
                    typeof(TrackedDeviceGraphicRaycaster));
                screen.transform.SetParent(console, false);
            }
            screen.name = "Game Start Screen";

            if (screen.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                screen.AddComponent<TrackedDeviceGraphicRaycaster>();
            }

            var canvas = screen.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            var rect = screen.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = new Vector3(0f, 1.96f, 0.08f);
            rect.localRotation = Quaternion.Euler(66f, 180f, 0f);
            rect.localScale = Vector3.one * 0.003f;
            rect.sizeDelta = new Vector2(360f, 140f);

            while (screen.transform.childCount > 0)
            {
                UnityEngine.Object.DestroyImmediate(
                    screen.transform.GetChild(0).gameObject);
            }

            var buttonObject = new GameObject("Start Button", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(screen.transform, false);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = Vector2.zero;
            buttonRect.anchorMax = Vector2.one;
            buttonRect.offsetMin = new Vector2(12f, 12f);
            buttonRect.offsetMax = new Vector2(-12f, -12f);
            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.02f, 0.42f, 0.52f, 0.98f);
            var button = buttonObject.GetComponent<Button>();
            button.interactable = false;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.65f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.3f, 0.8f, 0.9f, 1f);
            colors.disabledColor = new Color(0.24f, 0.34f, 0.38f, 0.9f);
            button.colors = colors;

            var labelObject = new GameObject("Label", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(20f, 10f);
            labelRect.offsetMax = new Vector2(-20f, -10f);
            var label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 38;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = "WAITING FOR HOST";

            screen.SetActive(true);
            EditorUtility.SetDirty(screen);
            return (screen, button, label);
        }

        private static GameObject EnsureEventSystem(Scene scene)
        {
            var eventSystem = FindRoot(scene, "EventSystem");
            if (eventSystem == null)
            {
                eventSystem = new GameObject("EventSystem",
                    typeof(EventSystem), typeof(XRUIInputModule));
                SceneManager.MoveGameObjectToScene(eventSystem, scene);
            }
            else if (eventSystem.GetComponent<XRUIInputModule>() == null)
            {
                foreach (var module in eventSystem.GetComponents<BaseInputModule>())
                {
                    UnityEngine.Object.DestroyImmediate(module);
                }
                eventSystem.AddComponent<XRUIInputModule>();
            }
            return eventSystem;
        }

        private static Behaviour[] FindTranslationProviders(GameObject player)
        {
            var providerNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "ContinuousMoveProvider",
                "ContinuousMoveProviderBase",
                "DynamicMoveProvider",
                "TeleportationProvider",
                "GrabMoveProvider",
                "TwoHandedGrabMoveProvider",
                "ClimbProvider"
            };
            return player.GetComponentsInChildren<Behaviour>(true)
                .Where(component => providerNames.Contains(
                    component.GetType().Name))
                .Distinct()
                .ToArray();
        }

        private static GameStartDoorController[] BuildDoorControllers(
            Scene scene, GameStartRoute route)
        {
            var result = new List<GameStartDoorController>();
            foreach (var animator in scene.GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<Animator>(true)))
            {
                if (animator.runtimeAnimatorController == null)
                {
                    continue;
                }

                var hasDoorParameter = animator.parameters.Any(parameter =>
                    parameter.type == AnimatorControllerParameterType.Bool
                    && parameter.name == "character_nearby");
                if (!hasDoorParameter)
                {
                    continue;
                }

                var routeDistance = route.GetClosestDistance(
                    animator.transform.position, out var closestPoint);
                var offset = animator.transform.position - closestPoint;
                offset.y = 0f;
                if (offset.magnitude > 2.5f)
                {
                    continue;
                }

                var controller = GetOrAdd<GameStartDoorController>(
                    animator.gameObject);
                controller.Configure(animator, route, 4f);
                result.Add(controller);
                Debug.Log($"Game start door '{animator.name}' at route "
                    + $"distance {routeDistance:0.0} m.");
            }

            return result.OrderBy(item => item.RouteDistance).ToArray();
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            return gameObject.GetComponent<T>()
                ?? gameObject.AddComponent<T>();
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

        private static void MarkDirty(params UnityEngine.Object[] objects)
        {
            foreach (var item in objects)
            {
                if (item != null)
                {
                    EditorUtility.SetDirty(item);
                }
            }
        }
    }
}
