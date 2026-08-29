using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meshup.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    public static class LobbyBedroomBuilder
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string EnvironmentName = "Bedroom Environment";
        private const string MaterialFolder = "Assets/Materials/Lobby/Bedroom";
        private const string ModelFolder = "Assets/ThirdParty/KenneyFurnitureKit/Models";

        private static readonly Dictionary<string, Material> MaterialCache = new();
        private static readonly (string Collider, string Model)[] FurnitureColliderPairs =
        {
            ("Bed Collision", "Kenney Child Bed"),
            ("Bedside Collision", "Kenney Bedside Table"),
            ("Desk Collision", "Kenney Drawing Desk"),
            ("Bookcase Collision", "Kenney Bookcase"),
            ("Toy Box Collision", "Kenney Toy Box")
        };

        [MenuItem("Meshup/Lobby/Build Cozy Bedroom")]
        public static void BuildLobby()
        {
            EnsureFolder("Assets/Materials/Lobby", "Bedroom");
            MaterialCache.Clear();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var player = RequireRoot(scene, "Lobby Player");
            var totem = RequireRoot(scene, "Room Totem");
            RequireRoot(scene, "Lobby UI");
            RequireRoot(scene, "EventSystem");
            var networkScene = RequireRoot(scene, "Ubiq Network Scene");
            ConfigureNetworkSceneForReload(networkScene);

            DeleteRoot(scene, EnvironmentName);
            foreach (var oldRoot in new[]
                     {
                         "Floor", "North Wall", "South Wall", "East Wall", "West Wall"
                     })
            {
                DeleteRoot(scene, oldRoot);
            }

            var environment = new GameObject(EnvironmentName);
            SceneManager.MoveGameObjectToScene(environment, scene);

            ConfigureRenderSettings();
            BuildArchitecture(environment.transform);
            BuildWindow(environment.transform);
            BuildFurniture(environment.transform);
            BuildChildDetails(environment.transform);
            ConfigureLighting(scene, environment.transform);

            // Preserve the existing controller, camera hierarchy, serialized values, and scripts.
            player.transform.SetPositionAndRotation(
                new Vector3(0f, 0.02f, -2.03f),
                Quaternion.identity);

            // Preserve the current RoomTotemInteraction, trigger, rigidbody, and menu references.
            totem.transform.SetPositionAndRotation(
                new Vector3(2.05f, 0.79f, 1.42f),
                Quaternion.identity);
            BuildMenuObject(totem.transform);

            var controller = player.GetComponent<LobbyFirstPersonController>();
            if (controller == null)
            {
                throw new InvalidOperationException("Lobby Player lost its first-person controller.");
            }

            var interaction = totem.GetComponent<RoomTotemInteraction>();
            if (interaction == null)
            {
                throw new InvalidOperationException("Room Totem lost its interaction component.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Cozy lobby bedroom built successfully.");
        }

        public static void BuildFromCommandLine()
        {
            BuildLobby();
            ValidateLobby();
        }

        [MenuItem("Meshup/Lobby/Configure Reload-Safe Networking")]
        public static void ConfigureReloadSafeNetworking()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var networkScene = RequireRoot(scene, "Ubiq Network Scene");
            ConfigureNetworkSceneForReload(networkScene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            ValidateNetworkSceneForReload(networkScene);
            Debug.Log("Lobby networking configured for safe scene reloads.");
        }

        [MenuItem("Meshup/Lobby/Validate Cozy Bedroom")]
        public static void ValidateLobby()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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

        private static void ConfigureNetworkSceneForReload(GameObject networkScene)
        {
            // Ubiq keeps the first root NetworkScene alive between scene loads. A newly
            // loaded lobby therefore destroys its duplicate before child Start methods
            // run. NetworkSpawnManager.OnDestroy assumes Start initialized its spawner,
            // so keep this unused sample component inactive in every lobby instance.
            FindTransform(networkScene.transform, "Spawn Manager").gameObject.SetActive(false);
        }

        private static void ValidateNetworkSceneForReload(GameObject networkScene)
        {
            if (FindTransform(networkScene.transform, "Spawn Manager").gameObject.activeSelf)
            {
                throw new InvalidOperationException(
                    "The lobby Spawn Manager must remain inactive for safe scene reloads.");
            }
        }

        public static void CapturePreview()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var player = RequireRoot(scene, "Lobby Player");
            var camera = player.GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                throw new InvalidOperationException("Main lobby camera was not found.");
            }

            const int width = 1280;
            const int height = 720;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                var outputPath = "/tmp/meshup-lobby-bedroom-preview.png";
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
                Debug.Log($"Lobby preview saved to {outputPath}");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void ConfigureRenderSettings()
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#101927");
            RenderSettings.fogStartDistance = 8f;
            RenderSettings.fogEndDistance = 24f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#26364B");
            RenderSettings.ambientEquatorColor = Hex("#2F2A32");
            RenderSettings.ambientGroundColor = Hex("#17141A");
            RenderSettings.ambientIntensity = 0.58f;
            RenderSettings.reflectionIntensity = 0.55f;
        }

        private static void BuildArchitecture(Transform root)
        {
            var architecture = Child(root, "Architecture");
            var floorA = Lit("Floor Honey", "#79503D", 0.35f);
            var floorB = Lit("Floor Walnut", "#694234", 0.32f);
            var wall = Lit("Warm Plaster", "#BFA99A", 0.18f);
            var ceiling = Lit("Ceiling", "#CFC5BC", 0.12f);
            var trim = Lit("Dark Wood Trim", "#4C302A", 0.28f);
            var door = Lit("Painted Door", "#6D4B45", 0.25f);
            var brass = Lit("Aged Brass", "#9C7343", 0.5f, 0.35f);

            const float width = 6.6f;
            const float depth = 5.5f;
            const float height = 3.2f;
            const float wallThickness = 0.14f;

            CreateCube("Floor Collider", architecture, new Vector3(0f, -0.11f, 0f),
                new Vector3(width, 0.22f, depth), floorA, true);

            const int plankCount = 16;
            var plankWidth = width / plankCount;
            for (var i = 0; i < plankCount; i++)
            {
                var x = -width * 0.5f + plankWidth * (i + 0.5f);
                var zOffset = i % 3 == 0 ? 0.018f : 0f;
                CreateCube($"Floor Plank {i + 1:00}", architecture,
                    new Vector3(x, 0.012f, zOffset),
                    new Vector3(plankWidth - 0.018f, 0.025f, depth - 0.03f),
                    i % 2 == 0 ? floorA : floorB, false);
            }

            CreateCube("West Wall", architecture, new Vector3(-width * 0.5f, height * 0.5f, 0f),
                new Vector3(wallThickness, height, depth), wall, true);
            CreateCube("East Wall", architecture, new Vector3(width * 0.5f, height * 0.5f, 0f),
                new Vector3(wallThickness, height, depth), wall, true);
            CreateCube("South Wall", architecture, new Vector3(0f, height * 0.5f, -depth * 0.5f),
                new Vector3(width, height, wallThickness), wall, true);

            const float windowCenterX = 1.0f;
            const float windowWidth = 1.78f;
            const float windowBottom = 0.84f;
            const float windowHeight = 1.47f;
            var windowLeft = windowCenterX - windowWidth * 0.5f;
            var windowRight = windowCenterX + windowWidth * 0.5f;
            var northZ = depth * 0.5f;

            CreateCube("North Wall Left", architecture,
                new Vector3((-width * 0.5f + windowLeft) * 0.5f, height * 0.5f, northZ),
                new Vector3(windowLeft + width * 0.5f, height, wallThickness), wall, true);
            CreateCube("North Wall Right", architecture,
                new Vector3((windowRight + width * 0.5f) * 0.5f, height * 0.5f, northZ),
                new Vector3(width * 0.5f - windowRight, height, wallThickness), wall, true);
            CreateCube("North Wall Below Window", architecture,
                new Vector3(windowCenterX, windowBottom * 0.5f, northZ),
                new Vector3(windowWidth, windowBottom, wallThickness), wall, true);
            var topHeight = height - windowBottom - windowHeight;
            CreateCube("North Wall Above Window", architecture,
                new Vector3(windowCenterX, height - topHeight * 0.5f, northZ),
                new Vector3(windowWidth, topHeight, wallThickness), wall, true);

            CreateCube("Ceiling", architecture, new Vector3(0f, height + 0.08f, 0f),
                new Vector3(width, 0.16f, depth), ceiling, true);

            CreateCube("West Baseboard", architecture, new Vector3(-3.21f, 0.105f, 0f),
                new Vector3(0.12f, 0.21f, depth - 0.12f), trim, false);
            CreateCube("East Baseboard", architecture, new Vector3(3.21f, 0.105f, 0f),
                new Vector3(0.12f, 0.21f, depth - 0.12f), trim, false);
            CreateCube("South Baseboard", architecture, new Vector3(0f, 0.105f, -2.66f),
                new Vector3(width - 0.12f, 0.21f, 0.12f), trim, false);
            CreateCube("North Baseboard", architecture, new Vector3(0f, 0.105f, 2.66f),
                new Vector3(width - 0.12f, 0.21f, 0.12f), trim, false);

            // A closed decorative door anchors the player's spawn point without adding functionality.
            CreateCube("Bedroom Door", architecture, new Vector3(-2.25f, 1.1f, -2.665f),
                new Vector3(1.08f, 2.2f, 0.09f), door, false);
            CreateCube("Door Header", architecture, new Vector3(-2.25f, 2.28f, -2.61f),
                new Vector3(1.28f, 0.12f, 0.12f), trim, false);
            CreateCube("Door Left Trim", architecture, new Vector3(-2.85f, 1.12f, -2.61f),
                new Vector3(0.12f, 2.42f, 0.12f), trim, false);
            CreateCube("Door Right Trim", architecture, new Vector3(-1.65f, 1.12f, -2.61f),
                new Vector3(0.12f, 2.42f, 0.12f), trim, false);
            CreateSphere("Door Knob", architecture, new Vector3(-1.86f, 1.05f, -2.59f),
                new Vector3(0.1f, 0.1f, 0.1f), brass, false);
        }

        private static void BuildWindow(Transform root)
        {
            var window = Child(root, "Rainy Window");
            var trim = Lit("Window Wood", "#4B302B", 0.3f);
            var curtain = Lit("Dusty Plum Curtain", "#714F64", 0.22f);
            var night = Unlit("Night Sky", "#071221");
            var glass = Transparent("Rain Glass", "#38627A", 0.28f);
            var moon = Emissive("Moon Glow", "#DCEBEE", 1.7f);
            var rain = EmissiveTransparent("Rain Streak", "#83BBD2", 1.1f, 0.58f);
            var wire = Lit("Fairy Light Wire", "#302522", 0.18f);
            var fairy = Emissive("Fairy Light", "#FFC47D", 2.3f);

            const float centerX = 1.0f;
            const float z = 2.665f;
            const float width = 1.78f;
            const float height = 1.47f;
            const float bottom = 0.84f;
            var centerY = bottom + height * 0.5f;

            CreateCube("Night Beyond", window, new Vector3(centerX, centerY, 2.86f),
                new Vector3(width - 0.08f, height - 0.08f, 0.035f), night, false);
            CreateSphere("Moon", window, new Vector3(1.56f, 1.98f, 2.82f),
                new Vector3(0.33f, 0.33f, 0.06f), moon, false);

            for (var i = 0; i < 7; i++)
            {
                var buildingWidth = 0.18f + (i % 3) * 0.06f;
                var buildingHeight = 0.25f + (i % 4) * 0.11f;
                var x = centerX - 0.73f + i * 0.235f;
                CreateCube($"Distant Rooftop {i + 1}", window,
                    new Vector3(x, bottom + buildingHeight * 0.5f, 2.79f),
                    new Vector3(buildingWidth, buildingHeight, 0.035f),
                    Lit("Distant Rooftops", "#101A27", 0.08f), false);
            }

            CreateCube("Window Glass", window, new Vector3(centerX, centerY, z - 0.018f),
                new Vector3(width - 0.08f, height - 0.08f, 0.028f), glass, false);
            CreateCube("Window Sill", window, new Vector3(centerX, bottom - 0.035f, z - 0.11f),
                new Vector3(width + 0.18f, 0.13f, 0.28f), trim, false);
            CreateCube("Window Top", window, new Vector3(centerX, bottom + height + 0.035f, z - 0.035f),
                new Vector3(width + 0.16f, 0.12f, 0.16f), trim, false);
            CreateCube("Window Left", window, new Vector3(centerX - width * 0.5f, centerY, z - 0.035f),
                new Vector3(0.12f, height + 0.12f, 0.16f), trim, false);
            CreateCube("Window Right", window, new Vector3(centerX + width * 0.5f, centerY, z - 0.035f),
                new Vector3(0.12f, height + 0.12f, 0.16f), trim, false);
            CreateCube("Window Mullion Vertical", window, new Vector3(centerX, centerY, z - 0.095f),
                new Vector3(0.065f, height, 0.09f), trim, false);
            CreateCube("Window Mullion Horizontal", window, new Vector3(centerX, centerY, z - 0.095f),
                new Vector3(width, 0.065f, 0.09f), trim, false);

            CreateCube("Left Curtain", window, new Vector3(0.02f, 1.55f, 2.52f),
                new Vector3(0.38f, 1.86f, 0.16f), curtain, false,
                Quaternion.Euler(0f, -4f, 2f));
            CreateCube("Right Curtain", window, new Vector3(1.98f, 1.55f, 2.52f),
                new Vector3(0.38f, 1.86f, 0.16f), curtain, false,
                Quaternion.Euler(0f, 4f, -2f));
            CreateBeam("Curtain Rod", window, new Vector3(-0.22f, 2.52f, 2.48f),
                new Vector3(2.22f, 2.52f, 2.48f), 0.035f, trim);

            var rainObject = new GameObject("Rain Particles");
            rainObject.transform.SetParent(window, false);
            rainObject.transform.position = new Vector3(centerX, 2.35f, 2.73f);
            var particles = rainObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.05f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.78f, 0.9f, 0.62f));
            main.maxParticles = 220;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 115f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.58f, 0.05f, 0.04f);
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            // Unity requires all velocity axes to use the same MinMaxCurve mode.
            velocity.x = new ParticleSystem.MinMaxCurve(-0.14f);
            velocity.y = new ParticleSystem.MinMaxCurve(-3.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);
            var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            particleRenderer.lengthScale = 2.6f;
            particleRenderer.velocityScale = 0.08f;
            particleRenderer.material = rain;

            var fairyPositions = new[]
            {
                new Vector3(-0.12f, 2.46f, 2.42f), new Vector3(0.16f, 2.39f, 2.42f),
                new Vector3(0.45f, 2.34f, 2.42f), new Vector3(0.75f, 2.31f, 2.42f),
                new Vector3(1.05f, 2.30f, 2.42f), new Vector3(1.35f, 2.31f, 2.42f),
                new Vector3(1.65f, 2.35f, 2.42f), new Vector3(1.94f, 2.40f, 2.42f),
                new Vector3(2.18f, 2.47f, 2.42f)
            };
            for (var i = 0; i < fairyPositions.Length - 1; i++)
            {
                CreateBeam($"Fairy Wire {i + 1}", window, fairyPositions[i], fairyPositions[i + 1], 0.009f, wire);
            }
            for (var i = 0; i < fairyPositions.Length; i++)
            {
                CreateSphere($"Fairy Bulb {i + 1}", window, fairyPositions[i],
                    new Vector3(0.055f, 0.055f, 0.055f), fairy, false);
            }
        }

        private static void BuildFurniture(Transform root)
        {
            var furniture = Child(root, "Furniture");

            PlaceModel("bedSingle", "Kenney Child Bed", furniture, new Vector3(-2.22f, 0f, 0.72f), 0f, 0.72f);
            PlaceModel("sideTableDrawers", "Kenney Bedside Table", furniture, new Vector3(-1.38f, 0f, 1.79f), 0f, 0.64f);
            PlaceModel("lampRoundTable", "Kenney Bedside Lamp", furniture, new Vector3(-1.38f, 0.63f, 1.79f), 0f, 0.62f);
            PlaceModel("pillowLong", "Kenney Bed Pillow", furniture, new Vector3(-2.22f, 0.62f, 1.56f), 0f, 0.18f);
            PlaceModel("pillow", "Kenney Floor Pillow", furniture, new Vector3(-1.35f, 0.02f, -0.6f), 18f, 0.2f);

            PlaceModel("desk", "Kenney Drawing Desk", furniture, new Vector3(2.34f, 0f, 1.43f), 90f, 0.79f);
            PlaceModel("chairDesk", "Kenney Desk Chair", furniture, new Vector3(1.68f, 0f, 1.33f), 90f, 0.92f);
            PlaceModel("books", "Kenney Desk Books", furniture, new Vector3(2.45f, 0.77f, 1.63f), 12f, 0.22f);
            PlaceModel("radio", "Kenney Desk Radio", furniture, new Vector3(2.43f, 0.77f, 1.17f), 270f, 0.23f);

            PlaceModel("bookcaseOpen", "Kenney Bookcase", furniture, new Vector3(2.98f, 0f, -0.63f), 270f, 1.88f);
            // The upper-middle shelf surface is at roughly 1.30m. Keep the books
            // just above it instead of letting them intersect and hang below it.
            PlaceModel("books", "Kenney Shelf Books", furniture, new Vector3(2.74f, 1.315f, -0.66f), 270f, 0.23f);
            PlaceModel("lampRoundFloor", "Kenney Reading Lamp", furniture, new Vector3(2.73f, 0f, 0.31f), 0f, 1.62f);
            PlaceModel("pottedPlant", "Kenney Window Plant", furniture, new Vector3(0.3f, 0.86f, 2.49f), 0f, 0.55f);
            PlaceModel("coatRackStanding", "Kenney Coat Rack", furniture, new Vector3(-2.88f, 0f, -1.78f), 0f, 1.83f);
            PlaceModel("cardboardBoxOpen", "Kenney Toy Box", furniture, new Vector3(2.72f, 0f, -1.75f), 20f, 0.5f);
            PlaceModel("cardboardBoxClosed", "Kenney Storage Box", furniture, new Vector3(2.28f, 0f, -2.1f), -8f, 0.38f);
            PlaceModel("plantSmall2", "Kenney Small Plant", furniture, new Vector3(-1.57f, 0.63f, 1.77f), 0f, 0.26f);
            PlaceModel("rugRectangle", "Kenney Bedroom Rug", furniture, new Vector3(-0.18f, 0.025f, -0.32f), 0f, 2.85f, true);

            var collisions = Child(root, "Furniture Collisions");
            CreateCollider("Bed Collision", collisions, new Vector3(-2.25f, 0.4f, 0.76f), new Vector3(1.35f, 0.8f, 2.25f));
            CreateCollider("Bedside Collision", collisions, new Vector3(-1.38f, 0.34f, 1.79f), new Vector3(0.68f, 0.68f, 0.62f));
            CreateCollider("Desk Collision", collisions, new Vector3(2.6f, 0.42f, 1.43f), new Vector3(0.75f, 0.84f, 1.65f));
            CreateCollider("Bookcase Collision", collisions, new Vector3(3.02f, 0.94f, -0.63f), new Vector3(0.5f, 1.88f, 1.05f));
            CreateCollider("Toy Box Collision", collisions, new Vector3(2.72f, 0.25f, -1.75f), new Vector3(0.7f, 0.5f, 0.7f));

            SyncFurnitureColliderPositions(root);
        }

        [MenuItem("Meshup/Lobby/Sync Furniture Colliders")]
        public static void SyncFurnitureColliders()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var environment = RequireRoot(scene, EnvironmentName);
            SyncFurnitureColliderPositions(environment.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Lobby furniture colliders aligned with their rendered objects.");
        }

        private static void SyncFurnitureColliderPositions(Transform environment)
        {
            foreach (var pair in FurnitureColliderPairs)
            {
                var collider = FindTransform(environment, pair.Collider);
                var model = FindTransform(environment, pair.Model);
                collider.position = CalculateBounds(model.gameObject).center;
            }
        }

        private static void BuildChildDetails(Transform root)
        {
            var details = Child(root, "Child Details");
            var red = Lit("Toy Coral", "#C66A5A", 0.22f);
            var blue = Lit("Toy Blue", "#557A91", 0.22f);
            var yellow = Lit("Toy Mustard", "#D2A852", 0.22f);
            var green = Lit("Toy Sage", "#70866D", 0.22f);
            var paper = Lit("Drawing Paper", "#E4D8BC", 0.12f);
            var frame = Lit("Picture Frame", "#56372F", 0.28f);

            BuildPlushRabbit(details);

            CreateCube("Red Block", details, new Vector3(-1.02f, 0.085f, -1.42f), new Vector3(0.34f, 0.17f, 0.26f), red, true,
                Quaternion.Euler(0f, 18f, 0f));
            CreateCube("Blue Block", details, new Vector3(-0.7f, 0.1f, -1.5f), new Vector3(0.32f, 0.2f, 0.24f), blue, true,
                Quaternion.Euler(0f, -12f, 0f));
            CreateCylinder("Yellow Block", details, new Vector3(-0.82f, 0.13f, -1.18f), 0.13f, 0.26f, yellow, true);
            CreateCube("Green Block", details, new Vector3(-0.48f, 0.11f, -1.28f), new Vector3(0.28f, 0.22f, 0.24f), green, true,
                Quaternion.Euler(0f, 26f, 0f));

            // Layered paper rectangles read as handmade drawings in the low-poly style.
            CreateCube("Drawing One Frame", details, new Vector3(-3.205f, 1.77f, -0.52f), new Vector3(0.035f, 0.76f, 0.62f), frame, false);
            CreateCube("Drawing One Paper", details, new Vector3(-3.18f, 1.77f, -0.52f), new Vector3(0.02f, 0.64f, 0.5f), paper, false);
            CreateCube("Drawing One Sun", details, new Vector3(-3.165f, 1.9f, -0.66f), new Vector3(0.015f, 0.18f, 0.18f), yellow, false);
            CreateCube("Drawing One Hill", details, new Vector3(-3.16f, 1.62f, -0.42f), new Vector3(0.015f, 0.18f, 0.32f), green, false,
                Quaternion.Euler(45f, 0f, 45f));

            CreateCube("Drawing Two Frame", details, new Vector3(-3.205f, 1.6f, -1.45f), new Vector3(0.035f, 0.58f, 0.48f), frame, false);
            CreateCube("Drawing Two Paper", details, new Vector3(-3.18f, 1.6f, -1.45f), new Vector3(0.02f, 0.48f, 0.38f), paper, false);
            CreateCube("Drawing Two Mark", details, new Vector3(-3.165f, 1.6f, -1.45f), new Vector3(0.015f, 0.2f, 0.2f), red, false,
                Quaternion.Euler(45f, 0f, 45f));

            var mobile = Child(root, "Ceiling Mobile");
            var stringMaterial = Lit("Mobile String", "#6B625C", 0.15f);
            CreateBeam("Mobile Hanger", mobile, new Vector3(-1.55f, 3.08f, -0.2f), new Vector3(-1.55f, 2.72f, -0.2f), 0.01f, stringMaterial);
            CreateBeam("Mobile Bar", mobile, new Vector3(-1.92f, 2.72f, -0.2f), new Vector3(-1.18f, 2.72f, -0.2f), 0.018f, frame);
            var mobilePoints = new[]
            {
                new Vector3(-1.88f, 2.42f, -0.2f), new Vector3(-1.55f, 2.31f, -0.2f), new Vector3(-1.22f, 2.46f, -0.2f)
            };
            var mobileMaterials = new[] { yellow, blue, red };
            for (var i = 0; i < mobilePoints.Length; i++)
            {
                CreateBeam($"Mobile String {i + 1}", mobile,
                    new Vector3(mobilePoints[i].x, 2.71f, mobilePoints[i].z), mobilePoints[i], 0.008f, stringMaterial);
                CreateSphere($"Mobile Shape {i + 1}", mobile, mobilePoints[i],
                    new Vector3(0.13f, 0.13f, 0.07f), mobileMaterials[i], false);
            }
        }

        private static void BuildPlushRabbit(Transform parent)
        {
            // A simple, unmistakably soft toy assembled in the same low-poly
            // language as the room. It sits on the mattress rather than using
            // Kenney's bear wall decoration as a pretend teddy bear.
            var plush = Child(parent, "Plush Rabbit");
            var fur = Lit("Plush Rabbit Fur", "#B98C78", 0.12f);
            var innerEar = Lit("Plush Rabbit Inner Ear", "#D7A69D", 0.1f);
            var muzzle = Lit("Plush Rabbit Muzzle", "#D8B9A3", 0.1f);
            var stitch = Lit("Plush Rabbit Stitch", "#3E3335", 0.08f);

            const float x = -2.18f;
            const float z = 1.12f;
            CreateSphere("Body", plush, new Vector3(x, 0.81f, z),
                new Vector3(0.34f, 0.42f, 0.27f), fur, false);
            CreateSphere("Head", plush, new Vector3(x, 1.10f, z - 0.015f),
                new Vector3(0.32f, 0.30f, 0.28f), fur, false);
            CreateSphere("Left Ear", plush, new Vector3(x - 0.105f, 1.36f, z),
                new Vector3(0.12f, 0.34f, 0.105f), fur, false);
            CreateSphere("Right Ear", plush, new Vector3(x + 0.105f, 1.36f, z),
                new Vector3(0.12f, 0.34f, 0.105f), fur, false);
            CreateSphere("Left Inner Ear", plush, new Vector3(x - 0.105f, 1.36f, z - 0.055f),
                new Vector3(0.055f, 0.23f, 0.035f), innerEar, false);
            CreateSphere("Right Inner Ear", plush, new Vector3(x + 0.105f, 1.36f, z - 0.055f),
                new Vector3(0.055f, 0.23f, 0.035f), innerEar, false);
            CreateSphere("Left Arm", plush, new Vector3(x - 0.21f, 0.82f, z - 0.02f),
                new Vector3(0.13f, 0.30f, 0.13f), fur, false);
            CreateSphere("Right Arm", plush, new Vector3(x + 0.21f, 0.82f, z - 0.02f),
                new Vector3(0.13f, 0.30f, 0.13f), fur, false);
            CreateSphere("Left Foot", plush, new Vector3(x - 0.13f, 0.64f, z - 0.18f),
                new Vector3(0.18f, 0.15f, 0.24f), fur, false);
            CreateSphere("Right Foot", plush, new Vector3(x + 0.13f, 0.64f, z - 0.18f),
                new Vector3(0.18f, 0.15f, 0.24f), fur, false);
            CreateSphere("Muzzle", plush, new Vector3(x, 1.05f, z - 0.16f),
                new Vector3(0.16f, 0.12f, 0.09f), muzzle, false);
            CreateSphere("Left Eye", plush, new Vector3(x - 0.075f, 1.15f, z - 0.145f),
                Vector3.one * 0.038f, stitch, false);
            CreateSphere("Right Eye", plush, new Vector3(x + 0.075f, 1.15f, z - 0.145f),
                Vector3.one * 0.038f, stitch, false);
            CreateSphere("Nose", plush, new Vector3(x, 1.075f, z - 0.215f),
                new Vector3(0.052f, 0.04f, 0.035f), stitch, false);
        }

        private static void ConfigureLighting(Scene scene, Transform root)
        {
            var directionalObject = FindRoot(scene, "Directional Light");
            if (directionalObject == null)
            {
                directionalObject = new GameObject("Directional Light");
                SceneManager.MoveGameObjectToScene(directionalObject, scene);
                directionalObject.AddComponent<Light>();
            }
            var directional = directionalObject.GetComponent<Light>();
            directional.type = LightType.Directional;
            directional.color = Hex("#8FA9C7");
            directional.intensity = 0.24f;
            directional.shadows = LightShadows.Soft;
            directional.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

            var oldFill = FindRoot(scene, "Totem Fill Light");
            if (oldFill != null)
            {
                UnityEngine.Object.DestroyImmediate(oldFill);
            }

            var lights = Child(root, "Lighting");
            CreatePointLight("Warm Bedside Light", lights, new Vector3(-1.36f, 1.25f, 1.78f),
                Hex("#FFAE68"), 2.6f, 4.1f, LightShadows.Soft);
            CreatePointLight("Window Moonlight", lights, new Vector3(1.0f, 1.85f, 2.5f),
                Hex("#7EACD5"), 1.45f, 5.0f, LightShadows.Soft);
            CreatePointLight("Soft Ceiling Fill", lights, new Vector3(-0.3f, 2.82f, -0.25f),
                Hex("#E7B58A"), 0.58f, 5.3f, LightShadows.None);
            CreatePointLight("Storybook Glow Light", lights, new Vector3(2.05f, 1.08f, 1.42f),
                Hex("#65CBE9"), 1.35f, 3.35f, LightShadows.None);
            CreatePointLight("Fairy Lights Fill", lights, new Vector3(1.0f, 2.32f, 2.32f),
                Hex("#FFC37C"), 0.45f, 2.5f, LightShadows.None);
            CreatePointLight("Reading Lamp Fill", lights, new Vector3(2.72f, 1.25f, 0.31f),
                Hex("#FFB978"), 1.15f, 3.0f, LightShadows.None);
        }

        private static void BuildMenuObject(Transform totem)
        {
            for (var i = totem.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(totem.GetChild(i).gameObject);
            }

            var storybook = Child(totem, "Magic Storybook");
            var center = totem.position;
            var cover = Lit("Storybook Cover", "#355369", 0.38f);
            var pages = Lit("Storybook Pages", "#E4D3AE", 0.2f);
            var pageGlow = Emissive("Storybook Ink Glow", "#FFD17C", 1.8f);
            var sparkle = Emissive("Storybook Sparkle", "#77D6EE", 2.1f);

            CreateCube("Left Book Cover", storybook, center + new Vector3(-0.19f, 0.025f, 0f),
                new Vector3(0.39f, 0.045f, 0.57f), cover, false,
                Quaternion.Euler(0f, 0f, -7f));
            CreateCube("Right Book Cover", storybook, center + new Vector3(0.19f, 0.025f, 0f),
                new Vector3(0.39f, 0.045f, 0.57f), cover, false,
                Quaternion.Euler(0f, 0f, 7f));
            CreateCube("Left Page", storybook, center + new Vector3(-0.18f, 0.065f, 0f),
                new Vector3(0.35f, 0.028f, 0.52f), pages, false,
                Quaternion.Euler(0f, 0f, -7f));
            CreateCube("Right Page", storybook, center + new Vector3(0.18f, 0.065f, 0f),
                new Vector3(0.35f, 0.028f, 0.52f), pages, false,
                Quaternion.Euler(0f, 0f, 7f));
            CreateBeam("Book Spine", storybook, center + new Vector3(0f, 0.05f, -0.28f),
                center + new Vector3(0f, 0.05f, 0.28f), 0.022f, cover);

            for (var i = 0; i < 3; i++)
            {
                var z = -0.13f + i * 0.12f;
                CreateBeam($"Left Glowing Line {i + 1}", storybook,
                    center + new Vector3(-0.29f, 0.105f, z),
                    center + new Vector3(-0.08f, 0.13f, z), 0.009f, pageGlow);
                CreateBeam($"Right Glowing Line {i + 1}", storybook,
                    center + new Vector3(0.08f, 0.13f, z),
                    center + new Vector3(0.29f, 0.105f, z), 0.009f, pageGlow);
            }

            CreateSphere("Floating Story Light", storybook, center + new Vector3(0f, 0.34f, 0f),
                Vector3.one * 0.14f, sparkle, false);
            CreateSphere("Left Story Spark", storybook, center + new Vector3(-0.22f, 0.27f, -0.11f),
                Vector3.one * 0.045f, pageGlow, false);
            CreateSphere("Right Story Spark", storybook, center + new Vector3(0.25f, 0.22f, 0.12f),
                Vector3.one * 0.035f, pageGlow, false);
        }

        private static GameObject PlaceModel(string assetName, string instanceName, Transform parent,
            Vector3 floorPosition, float yaw, float targetSize, bool sizeByHorizontalExtent = false)
        {
            var path = $"{ModelFolder}/{assetName}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new FileNotFoundException($"Furniture model was not imported: {path}");
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate {path}");
            }

            instance.name = instanceName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one;
            UpgradeImportedMaterials(instance);

            var bounds = CalculateBounds(instance);
            var sourceSize = sizeByHorizontalExtent
                ? Mathf.Max(bounds.size.x, bounds.size.z)
                : bounds.size.y;
            if (sourceSize <= 0.0001f)
            {
                throw new InvalidOperationException($"Furniture model has invalid bounds: {path}");
            }

            var scale = targetSize / sourceSize;
            instance.transform.localScale = Vector3.one * scale;
            bounds = CalculateBounds(instance);
            instance.transform.position += new Vector3(
                floorPosition.x - bounds.center.x,
                floorPosition.y - bounds.min.y,
                floorPosition.z - bounds.center.z);
            return instance;
        }

        private static void UpgradeImportedMaterials(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var upgraded = renderer.sharedMaterials.Select(UpgradeImportedMaterial).ToArray();
                renderer.sharedMaterials = upgraded;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static Material UpgradeImportedMaterial(Material source)
        {
            var color = source != null && source.HasProperty("_Color") ? source.color : Color.white;
            var sourceName = source != null ? source.name : "Default";
            var color32 = (Color32)color;
            var safeName = new string(sourceName.Select(character =>
                char.IsLetterOrDigit(character) ? character : '_').ToArray());
            var name = $"Kenney_{safeName}_{color32.r:X2}{color32.g:X2}{color32.b:X2}";
            return Lit(name, color, 0.28f);
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

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static GameObject CreateCube(string name, Transform parent, Vector3 position,
            Vector3 scale, Material material, bool collider, Quaternion? rotation = null)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider)
            {
                UnityEngine.Object.DestroyImmediate(gameObject.GetComponent<Collider>());
            }
            return gameObject;
        }

        private static GameObject CreateSphere(string name, Transform parent, Vector3 position,
            Vector3 scale, Material material, bool collider)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider)
            {
                UnityEngine.Object.DestroyImmediate(gameObject.GetComponent<Collider>());
            }
            return gameObject;
        }

        private static GameObject CreateCylinder(string name, Transform parent, Vector3 position,
            float radius, float height, Material material, bool collider)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            gameObject.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider)
            {
                UnityEngine.Object.DestroyImmediate(gameObject.GetComponent<Collider>());
            }
            return gameObject;
        }

        private static GameObject CreateBeam(string name, Transform parent, Vector3 start,
            Vector3 end, float radius, Material material)
        {
            var direction = end - start;
            var gameObject = CreateCylinder(name, parent, (start + end) * 0.5f,
                radius, direction.magnitude, material, false);
            gameObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            return gameObject;
        }

        private static void CreateCollider(string name, Transform parent, Vector3 center, Vector3 size)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = center;
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.size = size;
        }

        private static Light CreatePointLight(string name, Transform parent, Vector3 position,
            Color color, float intensity, float range, LightShadows shadows)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            var light = gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows;
            light.shadowStrength = 0.72f;
            return light;
        }

        private static Material Lit(string name, string html, float smoothness, float metallic = 0f)
        {
            return Lit(name, Hex(html), smoothness, metallic);
        }

        private static Material Lit(string name, Color color, float smoothness, float metallic = 0f)
        {
            return GetOrCreateMaterial(name, "Universal Render Pipeline/Lit", material =>
            {
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Metallic", metallic);
            });
        }

        private static Material Unlit(string name, string html)
        {
            var color = Hex(html);
            return GetOrCreateMaterial(name, "Universal Render Pipeline/Unlit",
                material => material.SetColor("_BaseColor", color));
        }

        private static Material Emissive(string name, string html, float emissionIntensity)
        {
            var color = Hex(html);
            return GetOrCreateMaterial(name, "Universal Render Pipeline/Lit", material =>
            {
                material.SetColor("_BaseColor", color);
                material.SetColor("_EmissionColor", color * emissionIntensity);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetFloat("_Smoothness", 0.42f);
            });
        }

        private static Material Transparent(string name, string html, float alpha)
        {
            var color = Hex(html);
            color.a = alpha;
            return GetOrCreateMaterial(name, "Universal Render Pipeline/Lit", material =>
            {
                ConfigureTransparent(material, color);
                material.SetFloat("_Smoothness", 0.82f);
            });
        }

        private static Material EmissiveTransparent(string name, string html, float emissionIntensity, float alpha)
        {
            var color = Hex(html);
            color.a = alpha;
            return GetOrCreateMaterial(name, "Universal Render Pipeline/Unlit", material =>
            {
                ConfigureTransparent(material, color);
                material.SetColor("_EmissionColor", color * emissionIntensity);
            });
        }

        private static void ConfigureTransparent(Material material, Color color)
        {
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Material GetOrCreateMaterial(string name, string shaderName, Action<Material> configure)
        {
            if (MaterialCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required shader was not found: {shaderName}");
            }

            if (material == null)
            {
                material = new Material(shader) { name = name };
                configure(material);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
                configure(material);
                EditorUtility.SetDirty(material);
            }

            MaterialCache[name] = material;
            return material;
        }

        private static Color Hex(string html)
        {
            if (!ColorUtility.TryParseHtmlString(html, out var color))
            {
                throw new ArgumentException($"Invalid HTML color: {html}", nameof(html));
            }
            return color;
        }

        private static void EnsureFolder(string parent, string child)
        {
            var fullPath = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(fullPath))
            {
                AssetDatabase.CreateFolder(parent, child);
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
                   ?? throw new InvalidOperationException($"Required bedroom object was not found: {name}");
        }

        private static void DeleteRoot(Scene scene, string name)
        {
            var root = FindRoot(scene, name);
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
