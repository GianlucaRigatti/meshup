using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    public static class LobbyBedroomPolishPass
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PassName = "Bedroom Polish Pass";
        private const string MaterialFolder = "Assets/Materials/Lobby/Bedroom";

        [MenuItem("Meshup/Lobby/Apply Bedroom Polish Pass")]
        public static void ApplyFromMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var environment = RequireRoot(scene, "Bedroom Environment");

            var previous = environment.transform.Find(PassName);
            if (previous != null)
            {
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }

            var pass = new GameObject(PassName);
            pass.transform.SetParent(environment.transform, false);

            TuneLighting(scene);
            BuildCeilingFixture(pass.transform);
            BuildFairyBulbLights(environment.transform, pass.transform);
            EncloseBookcase(environment.transform, pass.transform);
            BuildCornerWardrobe(pass.transform);
            DressCoatRack(environment.transform, pass.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Bedroom polish pass applied without rebuilding the manually edited room.");
        }

        public static void ApplyFromCommandLine()
        {
            ApplyFromMenu();
            ValidatePolishPass();
            LobbyBedroomBuilder.ValidateLobby();
        }

        [MenuItem("Meshup/Lobby/Validate Bedroom Polish Pass")]
        public static void ValidatePolishPass()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var environment = RequireRoot(scene, "Bedroom Environment");
            var pass = environment.transform.Find(PassName);
            if (pass == null
                || pass.Find("Corner Wardrobe") == null
                || pass.Find("Bookcase Enclosure") == null
                || pass.Find("Coat Rack Accessories/Jacket") == null
                || pass.Find("Coat Rack Accessories/Shoes") == null)
            {
                throw new InvalidOperationException("One or more bedroom polish objects are missing.");
            }

            var extraBooks = pass.GetComponentsInChildren<Transform>(true)
                .Count(item => item.name.StartsWith("Extra Book ", StringComparison.Ordinal));
            if (extraBooks < 14)
            {
                throw new InvalidOperationException("The closed bookcase does not contain enough extra books.");
            }

            var ceiling = FindInScene<Light>(scene, "Soft Ceiling Fill");
            if (ceiling == null || ceiling.range < 6f || ceiling.intensity < 1f
                || ceiling.shadows != LightShadows.None)
            {
                throw new InvalidOperationException("The room-wide ceiling light is not configured correctly.");
            }

            var fairyLightsRoot = pass.Find("Fairy Bulb Lights");
            var fairyLights = fairyLightsRoot != null
                ? fairyLightsRoot.GetComponentsInChildren<Light>(true)
                : Array.Empty<Light>();
            if (fairyLights.Length != 9 || fairyLights.Any(light =>
                    light.intensity > 0.05f || light.range > 0.65f || light.shadows != LightShadows.None))
            {
                throw new InvalidOperationException("Each fairy bulb must have one very dim, shadowless local light.");
            }

            foreach (var name in new[]
                     {
                         "Warm Bedside Light", "Reading Lamp Fill", "Storybook Glow Light", "Fairy Lights Fill"
                     })
            {
                var light = FindInScene<Light>(scene, name);
                // Some optional decorative fills may have been removed by hand.
                if (light != null && (light.intensity > 0.6f || light.range > 2.2f))
                {
                    throw new InvalidOperationException($"{name} is not a small, dim local light.");
                }
            }

            Debug.Log($"Bedroom polish validation passed with {extraBooks} additional books.");
        }

        public static void CapturePolishPreviews()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var camera = RequireRoot(scene, "Lobby Player").GetComponentInChildren<Camera>(true)
                ?? throw new InvalidOperationException("Lobby camera is missing.");
            var oldPosition = camera.transform.position;
            var oldRotation = camera.transform.rotation;
            var views = new[]
            {
                ("/tmp/meshup-bookcase-preview.png", new Vector3(1.25f, 1.35f, -0.35f), new Vector3(2.95f, 1.02f, -0.41f)),
                ("/tmp/meshup-wardrobe-preview.png", new Vector3(0.35f, 1.35f, -0.45f), new Vector3(2.38f, 1.14f, -2.28f)),
                ("/tmp/meshup-coatrack-preview.png", new Vector3(-0.90f, 1.25f, -0.55f), new Vector3(-1.25f, 1.00f, -2.35f)),
                ("/tmp/meshup-ceiling-preview.png", new Vector3(0f, 1.55f, 0f), new Vector3(0f, 2.92f, 0f)),
                ("/tmp/meshup-fairy-preview.png", new Vector3(1f, 1.45f, 0.75f), new Vector3(1f, 2.30f, 2.48f))
            };

            try
            {
                foreach (var view in views)
                {
                    camera.transform.position = view.Item2;
                    camera.transform.rotation = Quaternion.LookRotation(view.Item3 - view.Item2, Vector3.up);
                    Capture(camera, view.Item1);
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
            }
        }

        private static void TuneLighting(Scene scene)
        {
            // Keep the user's hand-adjusted lamp positions; only change their light falloff.
            TuneLocalLight(scene, "Warm Bedside Light", 0.42f, 1.75f, "#FFC18B");
            TuneLocalLight(scene, "Reading Lamp Fill", 0.38f, 1.8f, "#FFC08A");
            TuneLocalLight(scene, "Storybook Glow Light", 0.32f, 1.45f, "#74C8DC");
            var oldFairyFill = FindInScene<Light>(scene, "Fairy Lights Fill");
            if (oldFairyFill != null)
            {
                oldFairyFill.enabled = false;
            }

            var ceiling = FindInScene<Light>(scene, "Soft Ceiling Fill");
            if (ceiling == null)
            {
                var environment = RequireRoot(scene, "Bedroom Environment");
                var lighting = environment.Find("Lighting") ?? environment;
                var ceilingObject = new GameObject("Soft Ceiling Fill");
                ceilingObject.transform.SetParent(lighting, false);
                ceiling = ceilingObject.AddComponent<Light>();
            }
            ceiling.gameObject.name = "Soft Ceiling Fill";
            ceiling.transform.position = new Vector3(0f, 2.82f, 0f);
            ceiling.type = LightType.Point;
            ceiling.color = Html("#FFD4AB");
            ceiling.intensity = 1.45f;
            ceiling.range = 7.4f;
            // This light sits just below the fixture. Shadow mapping here creates
            // a conspicuous square projection on the nearby ceiling, so the broad
            // ambient room light is intentionally shadowless.
            ceiling.shadows = LightShadows.None;
            ceiling.shadowStrength = 0f;
            ceiling.bounceIntensity = 1f;
        }

        private static void TuneLocalLight(Scene scene, string name, float intensity, float range, string color)
        {
            var light = FindInScene<Light>(scene, name);
            if (light == null)
            {
                return;
            }
            light.type = LightType.Point;
            light.color = Html(color);
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        private static void BuildCeilingFixture(Transform parent)
        {
            var fixture = Child(parent, "Ceiling Light Fixture");
            var wood = Material("Dark Wood Trim");
            var shade = Material("Painted Door");
            var glow = Material("Fairy Light");

            CreateCylinder("Ceiling Rose", fixture, new Vector3(0f, 3.10f, 0f), 0.22f, 0.10f, wood, false);
            CreateCylinder("Wide Shade", fixture, new Vector3(0f, 2.98f, 0f), 0.34f, 0.16f, shade, false);
            CreateCylinder("Shade Rim", fixture, new Vector3(0f, 2.88f, 0f), 0.38f, 0.055f, wood, false);
            CreateSphere("Warm Ceiling Bulb", fixture, new Vector3(0f, 2.82f, 0f),
                new Vector3(0.22f, 0.18f, 0.22f), glow, false);
        }

        private static void BuildFairyBulbLights(Transform environment, Transform parent)
        {
            var lightsRoot = Child(parent, "Fairy Bulb Lights");
            for (var i = 1; i <= 9; i++)
            {
                var bulb = FindTransform(environment, $"Fairy Bulb {i}");
                var lightObject = new GameObject($"Fairy Bulb Light {i:00}");
                lightObject.transform.SetParent(lightsRoot, false);
                lightObject.transform.position = bulb.position;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = Html("#FFD1A0");
                light.intensity = 0.028f;
                light.range = 0.52f;
                light.shadows = LightShadows.None;
                light.bounceIntensity = 0f;
            }
        }

        private static void EncloseBookcase(Transform environment, Transform parent)
        {
            var bookcase = FindTransform(environment, "Kenney Bookcase");
            var bounds = CalculateBounds(bookcase.gameObject);
            var enclosure = Child(parent, "Bookcase Enclosure");
            var wood = bookcase.GetComponentsInChildren<MeshRenderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .FirstOrDefault(material => material != null) ?? Material("Kenney_woodDark_D7B495");

            const float panel = 0.055f;
            CreateCube("Closed Back", enclosure,
                new Vector3(bounds.max.x - panel * 0.5f, bounds.center.y, bounds.center.z),
                new Vector3(panel, bounds.size.y - 0.05f, bounds.size.z - 0.06f), wood, false);
            CreateCube("Closed Left Side", enclosure,
                new Vector3(bounds.center.x, bounds.center.y, bounds.min.z + panel * 0.5f),
                new Vector3(bounds.size.x - 0.04f, bounds.size.y - 0.05f, panel), wood, false);
            CreateCube("Closed Right Side", enclosure,
                new Vector3(bounds.center.x, bounds.center.y, bounds.max.z - panel * 0.5f),
                new Vector3(bounds.size.x - 0.04f, bounds.size.y - 0.05f, panel), wood, false);

            var colors = new[]
            {
                Material("Toy Coral"), Material("Toy Blue"), Material("Toy Mustard"),
                Material("Toy Sage"), Material("Storybook Cover")
            };
            var shelfY = new[]
            {
                bounds.min.y + 0.10f,
                bounds.min.y + bounds.size.y * 0.43f,
                bounds.min.y + bounds.size.y * 0.70f
            };
            var counts = new[] { 5, 5, 6 };
            var starts = new[] { -0.29f, -0.06f, -0.31f };
            var bookNumber = 1;
            for (var shelf = 0; shelf < shelfY.Length; shelf++)
            {
                var z = bounds.center.z + starts[shelf];
                for (var i = 0; i < counts[shelf]; i++)
                {
                    var width = 0.075f + (i % 3) * 0.012f;
                    var height = 0.27f + ((i + shelf) % 4) * 0.035f;
                    CreateCube($"Extra Book {bookNumber++:00}", enclosure,
                        new Vector3(bounds.min.x + 0.16f, shelfY[shelf] + height * 0.5f, z),
                        new Vector3(0.25f, height, width), colors[(i + shelf) % colors.Length], false,
                        Quaternion.Euler(0f, 0f, i == counts[shelf] - 1 && shelf != 0 ? -7f : 0f));
                    z += width + 0.018f;
                }
            }
        }

        private static void BuildCornerWardrobe(Transform parent)
        {
            var wardrobe = Child(parent, "Corner Wardrobe");
            var wood = Material("Window Wood");
            var door = Material("Painted Door");
            var trim = Material("Dark Wood Trim");
            var brass = Material("Aged Brass");

            const float centerX = 2.38f;
            const float backZ = -2.61f;
            const float frontZ = -1.98f;
            const float height = 2.30f;
            const float left = 1.67f;
            const float right = 3.09f;

            CreateCube("Wardrobe Back", wardrobe, new Vector3(centerX, height * 0.5f, backZ),
                new Vector3(right - left, height, 0.08f), wood, false);
            CreateCube("Wardrobe Left Side", wardrobe, new Vector3(left, height * 0.5f, (backZ + frontZ) * 0.5f),
                new Vector3(0.10f, height, frontZ - backZ), wood, false);
            CreateCube("Wardrobe Right Side", wardrobe, new Vector3(right, height * 0.5f, (backZ + frontZ) * 0.5f),
                new Vector3(0.10f, height, frontZ - backZ), wood, false);
            CreateCube("Wardrobe Top", wardrobe, new Vector3(centerX, height, (backZ + frontZ) * 0.5f),
                new Vector3(right - left, 0.11f, frontZ - backZ), trim, false);
            CreateCube("Wardrobe Plinth", wardrobe, new Vector3(centerX, 0.10f, (backZ + frontZ) * 0.5f),
                new Vector3(right - left, 0.20f, frontZ - backZ), trim, false);

            CreateCube("Left Door", wardrobe, new Vector3(2.03f, 1.18f, frontZ + 0.015f),
                new Vector3(0.66f, 2.12f, 0.08f), door, false);
            CreateCube("Right Door", wardrobe, new Vector3(2.73f, 1.18f, frontZ + 0.015f),
                new Vector3(0.66f, 2.12f, 0.08f), door, false);
            CreateCube("Door Center Trim", wardrobe, new Vector3(centerX, 1.18f, frontZ - 0.035f),
                new Vector3(0.055f, 2.14f, 0.05f), trim, false);
            CreateSphere("Left Wardrobe Knob", wardrobe, new Vector3(2.30f, 1.15f, frontZ + 0.085f),
                Vector3.one * 0.075f, brass, false);
            CreateSphere("Right Wardrobe Knob", wardrobe, new Vector3(2.46f, 1.15f, frontZ + 0.085f),
                Vector3.one * 0.075f, brass, false);

            var collision = new GameObject("Wardrobe Collision");
            collision.transform.SetParent(wardrobe, false);
            collision.transform.position = new Vector3(centerX, height * 0.5f, (backZ + frontZ) * 0.5f);
            var collider = collision.AddComponent<BoxCollider>();
            collider.size = new Vector3(right - left, height, frontZ - backZ);
        }

        private static void DressCoatRack(Transform environment, Transform parent)
        {
            var coatRack = FindTransform(environment, "Kenney Coat Rack");
            var bounds = CalculateBounds(coatRack.gameObject);
            var accessories = Child(parent, "Coat Rack Accessories");
            var jacket = Child(accessories, "Jacket");
            var shoes = Child(accessories, "Shoes");
            var cloth = Material("Storybook Cover");
            var clothDark = Material("Dark Wood Trim");
            var button = Material("Aged Brass");
            var floor = Material("Floor Walnut");

            var jacketCenter = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.67f, bounds.center.z + 0.10f);
            CreateCube("Jacket Body", jacket, jacketCenter, new Vector3(0.43f, 0.55f, 0.13f), cloth, false);
            CreateCube("Left Sleeve", jacket, jacketCenter + new Vector3(-0.25f, 0.03f, 0f),
                new Vector3(0.18f, 0.48f, 0.12f), cloth, false, Quaternion.Euler(0f, 0f, -18f));
            CreateCube("Right Sleeve", jacket, jacketCenter + new Vector3(0.25f, 0.03f, 0f),
                new Vector3(0.18f, 0.48f, 0.12f), cloth, false, Quaternion.Euler(0f, 0f, 18f));
            CreateCube("Jacket Collar Left", jacket, jacketCenter + new Vector3(-0.09f, 0.22f, -0.075f),
                new Vector3(0.16f, 0.16f, 0.045f), clothDark, false, Quaternion.Euler(0f, 0f, -32f));
            CreateCube("Jacket Collar Right", jacket, jacketCenter + new Vector3(0.09f, 0.22f, -0.075f),
                new Vector3(0.16f, 0.16f, 0.045f), clothDark, false, Quaternion.Euler(0f, 0f, 32f));
            for (var i = 0; i < 3; i++)
            {
                CreateSphere($"Jacket Button {i + 1}", jacket,
                    jacketCenter + new Vector3(0f, 0.11f - i * 0.14f, -0.085f),
                    Vector3.one * 0.035f, button, false);
            }

            var shoeCenterX = bounds.max.x + 0.23f;
            for (var i = 0; i < 2; i++)
            {
                var x = shoeCenterX + i * 0.23f;
                var z = bounds.center.z + 0.18f + (i == 0 ? -0.025f : 0.025f);
                var rotation = Quaternion.Euler(0f, i == 0 ? -7f : 7f, 0f);
                CreateCube($"Shoe {i + 1} Sole", shoes, new Vector3(x, 0.055f, z),
                    new Vector3(0.18f, 0.07f, 0.36f), floor, false, rotation);
                CreateCube($"Shoe {i + 1} Upper", shoes, new Vector3(x, 0.12f, z + 0.025f),
                    new Vector3(0.16f, 0.11f, 0.29f), clothDark, false, rotation);
                CreateSphere($"Shoe {i + 1} Toe", shoes, new Vector3(x, 0.12f, z + 0.16f),
                    new Vector3(0.17f, 0.12f, 0.18f), clothDark, false);
            }
        }

        private static Transform RequireRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(item => item.name == name)?.transform
                ?? throw new InvalidOperationException($"Required scene root is missing: {name}");
        }

        private static Transform FindTransform(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name)
                ?? throw new InvalidOperationException($"Required bedroom object is missing: {name}");
        }

        private static T RequireInScene<T>(Scene scene, string name) where T : Component
        {
            return FindInScene<T>(scene, name)
                ?? throw new InvalidOperationException($"Required scene component is missing: {name}");
        }

        private static T FindInScene<T>(Scene scene, string name) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault(component => component.gameObject.name == name);
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static GameObject CreateCube(string name, Transform parent, Vector3 position, Vector3 scale,
            Material material, bool collider, Quaternion? rotation = null)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
            item.transform.localScale = scale;
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            item.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            if (!collider)
            {
                UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            }
            return item;
        }

        private static GameObject CreateSphere(string name, Transform parent, Vector3 position, Vector3 scale,
            Material material, bool collider)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider)
            {
                UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            }
            return item;
        }

        private static GameObject CreateCylinder(string name, Transform parent, Vector3 position, float radius,
            float height, Material material, bool collider)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider)
            {
                UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            }
            return item;
        }

        private static Bounds CalculateBounds(GameObject item)
        {
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException($"Bedroom object has no renderer: {item.name}");
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        private static Material Material(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat")
                ?? throw new InvalidOperationException($"Bedroom material is missing: {name}");
        }

        private static Color Html(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out var color))
            {
                throw new ArgumentException($"Invalid color: {value}", nameof(value));
            }
            return color;
        }

        private static void Capture(Camera camera, string outputPath)
        {
            const int width = 960;
            const int height = 720;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
