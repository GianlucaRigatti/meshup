using System;
using System.Collections.Generic;
using Meshup.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Meshup.Editor
{
    public static class GameSceneLightingBake
    {
        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        [MenuItem("Meshup/Lighting/Validate Game Scene Baked Lights")]
        public static void ValidateGameSceneBakedLights()
        {
            using var validation = new SceneValidationScope(ScenePath);
            Scene scene = validation.Scene;
            if (!scene.IsValid())
            {
                throw new InvalidOperationException($"Could not open {ScenePath}.");
            }

            int bakedLights = 0;
            var failures = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    if (light.lightmapBakeType != LightmapBakeType.Baked)
                    {
                        continue;
                    }

                    bakedLights++;
                    if (light.shadows != LightShadows.Soft)
                    {
                        failures.Add($"{GetHierarchyPath(light.transform)} does not use soft shadows.");
                    }

                    float minimumRange = light.type == LightType.Point ? 2.5f
                        : light.type == LightType.Spot ? 8f
                        : 0f;
                    if (minimumRange > 0f && light.range < minimumRange)
                    {
                        failures.Add(
                            $"{GetHierarchyPath(light.transform)} range {light.range} is below {minimumRange}.");
                    }
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(string.Join("\n", failures));
            }

            Debug.Log($"Validated {bakedLights} baked GameScene lights: soft shadows and ranges are correct.");
        }

        [MenuItem("Meshup/Lighting/Audit Game Scene Meshes")]
        public static void AuditGameSceneMeshes()
        {
            using var validation = new SceneValidationScope(ScenePath);
            Scene scene = validation.Scene;
            if (!scene.IsValid())
            {
                throw new InvalidOperationException($"Could not open {ScenePath}.");
            }

            var records = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        mesh = skinned.sharedMesh;
                    }
                    else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter meshFilter))
                    {
                        mesh = meshFilter.sharedMesh;
                    }
                    else
                    {
                        continue;
                    }
                    if (mesh == null)
                    {
                        continue;
                    }

                    string assetPath = AssetDatabase.GetAssetPath(mesh);
                    string key = string.IsNullOrEmpty(assetPath) ? $"<built-in>/{mesh.name}" : assetPath;
                    if (!records.TryGetValue(key, out List<string> usages))
                    {
                        usages = new List<string>();
                        records.Add(key, usages);
                    }

                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                    bool contributesGi = (flags & StaticEditorFlags.ContributeGI) != 0;
                    string receiveGi = renderer is MeshRenderer meshRenderer
                        ? meshRenderer.receiveGI.ToString()
                        : "NotApplicable";
                    usages.Add(
                        $"path={GetHierarchyPath(renderer.transform)}; active={renderer.gameObject.activeInHierarchy}; " +
                        $"enabled={renderer.enabled}; contributeGI={contributesGi}; receiveGI={receiveGi}; " +
                        $"uv2={mesh.uv2.Length}; vertices={mesh.vertexCount}");
                }
            }

            foreach (KeyValuePair<string, List<string>> record in records)
            {
                Debug.Log($"LIGHTING_AUDIT_ASSET|{record.Key}");
                foreach (string usage in record.Value)
                {
                    Debug.Log($"LIGHTING_AUDIT_USAGE|{usage}");
                }
            }

            Debug.Log($"GameScene mesh audit completed: {records.Count} resolved mesh assets.");
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = $"{transform.name}/{path}";
            }

            return path;
        }

        [MenuItem("Meshup/Lighting/Bake Game Scene")]
        public static void BakeGameScene()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            using var validation = new SceneValidationScope(ScenePath);
            Scene scene = validation.Scene;
            if (!scene.IsValid())
            {
                throw new InvalidOperationException($"Could not open {ScenePath}.");
            }

            EditorSceneManager.SaveScene(scene);
            if (!Lightmapping.Bake())
            {
                throw new InvalidOperationException("Unity lighting bake failed.");
            }

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("GameScene lighting bake completed successfully.");
        }
    }
}
