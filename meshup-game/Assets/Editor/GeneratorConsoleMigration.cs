using System;
using System.Linq;
using Meshup.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Meshup.EditorTools
{
    public static class GeneratorConsoleMigration
    {
        public const string ModelPath = "Assets/Art/GeneratorConsole/GeneratorConsole.glb";
        public static void Inspect()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            var coordinator = UnityEngine.Object.FindAnyObjectByType<MeshupGameCoordinator>();
            var so = new SerializedObject(coordinator);
            foreach (var field in new[] { "generatorButton", "smallSizeButton", "mediumSizeButton", "extraLargeSizeButton" })
            {
                var go = (GameObject)so.FindProperty(field).objectReferenceValue;
                Debug.Log($"CONSOLE {field} {Path(go.transform)} world={go.transform.position:F4} rotation={go.transform.eulerAngles:F3} scale={go.transform.lossyScale:F4}");
                for (var t = go.transform; t != null; t = t.parent)
                    Debug.Log($"ANCESTOR {t.name} world={t.position:F4} rotation={t.eulerAngles:F3} scale={t.lossyScale:F4}");
                foreach (var r in go.GetComponentsInChildren<Renderer>()) Debug.Log($"OLD RENDERER {Path(r.transform)} bounds={r.bounds}");
            }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (asset == null) throw new Exception("Console model failed to import");
            foreach (var t in asset.GetComponentsInChildren<Transform>(true)) Debug.Log($"NEW {Path(t)} local={t.localPosition:F4} rot={t.localEulerAngles:F2} scale={t.localScale:F4} mesh={(t.GetComponent<MeshFilter>() != null ? t.GetComponent<MeshFilter>().sharedMesh.bounds.ToString() : "none")}");
        }
        [MenuItem("Meshup/Game/Install Integrated Generator Console")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            var coordinator = UnityEngine.Object.FindAnyObjectByType<MeshupGameCoordinator>();
            var so = new SerializedObject(coordinator);
            var oldButton = (GameObject)so.FindProperty("generatorButton").objectReferenceValue;
            if (oldButton.GetComponent<ConsoleButtonFeedback>() != null)
            {
                Debug.Log("Integrated console is already installed.");
                return;
            }
            var oldSmall = (GameObject)so.FindProperty("smallSizeButton").objectReferenceValue;
            var machine = oldButton.transform.parent;
            var oldSelector = oldSmall.transform;
            while (oldSelector.parent != machine)
            {
                oldSelector = oldSelector.parent;
                if (oldSelector == null) throw new InvalidOperationException("Old selector is outside the machine");
            }
            var position = oldButton.transform.position;
            var rotation = oldButton.transform.rotation;
            // The new shell is .8 m tall; preserve the old shell's 1.5 m height.
            const float worldScale = 1.875f;
            position -= rotation * new Vector3(-0.3042f * worldScale, 0f, 0f);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var wrapper = new GameObject("IntegratedGeneratorConsole");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            model.transform.SetParent(wrapper.transform, false);
            foreach (var animation in model.GetComponentsInChildren<Animation>(true))
            {
                animation.playAutomatically = false;
                animation.enabled = false;
            }
            foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var transforms = model.GetComponentsInChildren<Transform>(true);
            foreach (var name in new[] { "Small", "Medium", "ExtraLarge", "Generate" })
            {
                var button = transforms.Single(t => t.name == "Button_" + name);
                var feedback = button.gameObject.AddComponent<ConsoleButtonFeedback>();
                feedback.Configure(transforms.Single(t => t.name == "Lit_" + name),
                    new Vector3(0f, -0.009899495f, -0.009899495f));
                var collider = button.gameObject.AddComponent<BoxCollider>();
                var bounds = button.GetComponent<MeshFilter>().sharedMesh.bounds;
                collider.center = bounds.center;
                collider.size = bounds.size;
                var interactable = button.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
                interactable.colliders.Add(collider);
                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
            }
            var housing = transforms.Single(t => t.name == "Housing");
            var shellCollider = housing.gameObject.AddComponent<MeshCollider>();
            shellCollider.sharedMesh = housing.GetComponent<MeshFilter>().sharedMesh;
            foreach (var t in transforms) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            const string prefabPath = "Assets/Art/GeneratorConsole/IntegratedGeneratorConsole.prefab";
            PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
            UnityEngine.Object.DestroyImmediate(wrapper);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
            instance.transform.SetParent(machine, false);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = Vector3.one * (worldScale / machine.lossyScale.x);
            var buttons = instance.GetComponentsInChildren<ConsoleButtonFeedback>(true);
            foreach (var pair in new[] { ("generatorButton", "Generate"), ("smallSizeButton", "Small"), ("mediumSizeButton", "Medium"), ("extraLargeSizeButton", "ExtraLarge") })
                so.FindProperty(pair.Item1).objectReferenceValue = buttons.Single(b => b.name == "Button_" + pair.Item2).gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.Object.DestroyImmediate(oldButton);
            UnityEngine.Object.DestroyImmediate(oldSelector.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            MeshupGameSceneValidation.Validate(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"CONSOLE INSTALLED {position} rotation {rotation.eulerAngles}, world scale {worldScale}");
        }
        public static void PrepareMaterials()
        {
            const string directory = "Assets/Art/GeneratorConsole/Materials";
            if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder("Assets/Art/GeneratorConsole", "Materials");
            const string path = "Assets/Art/GeneratorConsole/IntegratedGeneratorConsole.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null) throw new InvalidOperationException("URP Simple Lit shader is missing");
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
                    {
                        var materialPath = directory + "/" + source.name + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        if (material != null) return material;
                        material = new Material(shader) { name = source.name };
                        material.SetColor("_BaseColor", source.GetColor("baseColorFactor"));
                        material.SetFloat("_Smoothness", .25f);
                        material.SetColor("_SpecColor", new Color(.035f, .035f, .035f, 1f));
                        material.SetFloat("_SpecularHighlights", 0f);
                        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                        var emission = source.name == "Console_Emission"
                            ? new Color(.02f, .8f, 1f) * 3f : source.GetColor("emissiveFactor");
                        material.SetColor("_EmissionColor", emission);
                        if (emission.maxColorComponent > 0f) material.EnableKeyword("_EMISSION");
                        AssetDatabase.CreateAsset(material, materialPath);
                        return material;
                    }).ToArray();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
            Debug.Log("Console uses native URP materials.");
        }

        public static void PrepareAndPreview()
        {
            PrepareMaterials();
            RenderPreview();
        }

        public static void RenderPreview()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            var console = GameObject.Find("IntegratedGeneratorConsole");
            if (console == null) throw new InvalidOperationException("Console is missing");
            foreach (var feedback in console.GetComponentsInChildren<ConsoleButtonFeedback>())
                feedback.SetSelected(feedback.name == "Button_Medium");
            foreach (var material in console.GetComponentsInChildren<Renderer>()
                .SelectMany(r => r.sharedMaterials).Distinct())
            {
                Debug.Log($"CONSOLE MATERIAL {material.name} shader={material.shader.name}");
                foreach (var property in new[] { "baseColorFactor", "_BaseColor", "_Color", "emissiveFactor", "_EmissionColor" })
                    if (material.HasProperty(property)) Debug.Log($"PROPERTY {property}={material.GetColor(property)}");
            }
            // Neutral inspection lighting; the saved scene lighting is not modified.
            foreach (var existing in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) existing.enabled = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.3f, .3f, .3f);
            var cameraObject = new GameObject("Console verification camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = console.transform.TransformPoint(new Vector3(.4f, 1.1f, 1.6f));
            camera.transform.LookAt(console.transform.TransformPoint(new Vector3(0f, .48f, 0f)));
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 100f;
            camera.fieldOfView = 48f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .1f, .12f);
            var light = cameraObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.range = 8f;
            light.intensity = .8f;
            var rt = new RenderTexture(1200, 1000, 24);
            camera.targetTexture = rt;
            camera.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(1200, 1000, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1200, 1000), 0, 0);
            texture.Apply();
            System.IO.File.WriteAllBytes("../art/generator_console/unity_scene_preview.png", texture.EncodeToPNG());
            RenderTexture.active = old;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            Debug.Log("Console scene preview rendered.");
        }
        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
