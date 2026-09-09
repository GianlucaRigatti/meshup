using System;
using System.Collections.Generic;
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
        private static readonly HashSet<string> DynamicRootNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Creature_back",
            "Creature_front",
            "Ubiq Demo Player"
        };

        [MenuItem("Meshup/Lighting/Validate Game Scene Baked Lights")]
        public static void ValidateGameSceneBakedLights()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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

        [MenuItem("Meshup/Lighting/Configure Stationary Game Scene Receivers")]
        public static void ConfigureStationaryGameSceneReceivers()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException($"Could not open {ScenePath}.");
            }

            int configured = 0;
            int excludedSkinned = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                    if ((flags & StaticEditorFlags.ContributeGI) == 0)
                    {
                        continue;
                    }

                    GameObjectUtility.SetStaticEditorFlags(
                        renderer.gameObject,
                        flags & ~StaticEditorFlags.ContributeGI);
                    excludedSkinned++;
                }

                if (DynamicRootNames.Contains(root.name))
                {
                    continue;
                }

                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags | StaticEditorFlags.ContributeGI);
                    renderer.receiveGI = ReceiveGI.Lightmaps;
                    EditorUtility.SetDirty(renderer);
                    configured++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Configured {configured} stationary GameScene MeshRenderers for baked GI; " +
                $"excluded {excludedSkinned} skinned renderers.");
        }

        [MenuItem("Meshup/Lighting/Audit Game Scene Meshes")]
        public static void AuditGameSceneMeshes()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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
