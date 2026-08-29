using System;
using System.Collections.Generic;
using System.Linq;
using Meshup.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meshup.EditorTools
{
    public static class LobbyPortalTransitionBuilder
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string RootName = "Lobby Portal Transition";
        private const string MaterialFolder = "Assets/Materials/Lobby/Portal";

        [MenuItem("Meshup/Lobby/Install Magic Portal Transition")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var oldRoot = FindRoot(scene, RootName);
            if (oldRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(oldRoot);
            }

            EnsureFolder(MaterialFolder);
            var cyan = GetOrCreateGlowMaterial("Portal Cyan",
                new Color(0.16f, 0.88f, 1f), 3.2f);
            var gold = GetOrCreateGlowMaterial("Portal Gold",
                new Color(1f, 0.68f, 0.2f), 2.6f);
            var veil = GetOrCreateVeilMaterial();

            var rigRoot = RequireRoot(scene, "Lobby Dual Mode Controls");
            var player = RequireChild(rigRoot.transform, "Ubiq Demo Player");
            var camera = player.GetComponentInChildren<Camera>(true)
                ?? throw new InvalidOperationException("The lobby Ubiq player has no camera.");
            var traditionalController = RequireChild(player.transform,
                "Traditional Controller").gameObject;
            var lobbyUi = RequireRoot(scene, "Lobby UI");
            var roomCanvasGroup = lobbyUi.GetComponent<CanvasGroup>()
                ?? lobbyUi.AddComponent<CanvasGroup>();

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var transition = root.AddComponent<LobbyPortalTransition>();

            var visualRoot = NewChild("Portal Visuals", root.transform);

            var overlayObject = new GameObject("Portal Glow Overlay",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            overlayObject.transform.SetParent(root.transform, false);
            var overlayCanvas = overlayObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            overlayCanvas.worldCamera = camera;
            overlayCanvas.planeDistance = 0.08f;
            overlayCanvas.sortingOrder = 32760;
            overlayObject.GetComponent<RectTransform>().localScale = Vector3.one;
            var overlayGroup = overlayObject.GetComponent<CanvasGroup>();
            overlayGroup.alpha = 0f;
            overlayGroup.interactable = false;
            overlayGroup.blocksRaycasts = false;

            var imageObject = new GameObject("Portal Glow",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(overlayObject.transform, false);
            var imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            var overlayImage = imageObject.GetComponent<Image>();
            overlayImage.color = new Color(0.53f, 0.97f, 1f, 1f);
            overlayImage.raycastTarget = true;

            var locomotion = player.GetComponentsInChildren<Behaviour>(true)
                .Where(IsLocomotionBehaviour)
                .Distinct()
                .ToArray();

            var serialized = new SerializedObject(transition);
            Set(serialized, "visualRoot", visualRoot);
            Set(serialized, "cyanMaterial", cyan);
            Set(serialized, "goldMaterial", gold);
            Set(serialized, "veilMaterial", veil);
            Set(serialized, "overlayCanvas", overlayCanvas);
            Set(serialized, "overlayGroup", overlayGroup);
            Set(serialized, "overlayImage", overlayImage);
            Set(serialized, "traditionalController", traditionalController);
            SetArray(serialized, "locomotionBehaviours",
                locomotion.Cast<UnityEngine.Object>().ToArray());
            Set(serialized, "roomUiCanvasGroup", roomCanvasGroup);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            visualRoot.gameObject.SetActive(false);
            overlayImage.enabled = false;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Validate();
            Debug.Log($"Magic portal transition installed with {locomotion.Length} locomotion controls gated.");
        }

        public static void BuildFromCommandLine()
        {
            Build();
        }

        [MenuItem("Meshup/Lobby/Validate Magic Portal Transition")]
        public static void Validate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = RequireRoot(scene, RootName);
            var transition = root.GetComponent<LobbyPortalTransition>()
                ?? throw new InvalidOperationException("Portal transition component is missing.");
            var serialized = new SerializedObject(transition);

            var requiredReferences = new[]
            {
                "visualRoot", "cyanMaterial", "goldMaterial", "veilMaterial",
                "overlayCanvas", "overlayGroup", "overlayImage",
                "traditionalController", "roomUiCanvasGroup"
            };
            foreach (var propertyName in requiredReferences)
            {
                if (serialized.FindProperty(propertyName).objectReferenceValue == null)
                {
                    throw new InvalidOperationException(
                        $"Portal transition reference is missing: {propertyName}");
                }
            }

            var locomotion = serialized.FindProperty("locomotionBehaviours");
            if (locomotion.arraySize == 0)
            {
                throw new InvalidOperationException("No XR locomotion controls are gated.");
            }
            if (root.transform.parent != null || !root.activeSelf)
            {
                throw new InvalidOperationException("Portal transition must be an active scene root.");
            }

            Debug.Log("Magic portal validation passed: visuals, overlay, desktop input and XR locomotion are wired.");
        }

        private static bool IsLocomotionBehaviour(Behaviour behaviour)
        {
            var typeName = behaviour.GetType().Name;
            return typeName.Contains("MoveProvider", StringComparison.Ordinal)
                || typeName.Contains("TurnProvider", StringComparison.Ordinal)
                || typeName.Contains("TeleportationProvider", StringComparison.Ordinal)
                || typeName.Contains("ClimbProvider", StringComparison.Ordinal);
        }

        private static Material GetOrCreateGlowMaterial(string name, Color color,
            float emissionMultiplier)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = name;
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", color * emissionMultiplier);
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_Smoothness", 0.7f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateVeilMaterial()
        {
            var path = $"{MaterialFolder}/Portal Veil.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = "Portal Veil";
            material.SetColor("_BaseColor", new Color(0.2f, 0.86f, 1f, 0.42f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform NewChild(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void Set(SerializedObject target, string propertyName,
            UnityEngine.Object value)
        {
            target.FindProperty(propertyName).objectReferenceValue = value;
        }

        private static void SetArray(SerializedObject target, string propertyName,
            IReadOnlyList<UnityEngine.Object> values)
        {
            var property = target.FindProperty(propertyName);
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(item => item.name == name);
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return FindRoot(scene, name)
                ?? throw new InvalidOperationException($"Required root is missing: {name}");
        }

        private static Transform RequireChild(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == name)
                ?? throw new InvalidOperationException($"Required child is missing: {name}");
        }
    }
}
