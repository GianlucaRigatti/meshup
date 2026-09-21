using System;
using Meshup.EditorTools;
using Meshup.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.Editor
{
    /// <summary>
    /// Copies the Ubiq-authored UI prefabs into the project and connects the
    /// lobby controller to the local Menu copy. Runtime room actions are
    /// intentionally supplied by RoomTotemPanel, not Ubiq's sample scripts.
    /// </summary>
    public static class UbiqSampleMenuImporter
    {
        private const string LobbyScene = "Assets/Scenes/SampleScene.unity";
        private const string SourceRoot = "Packages/com.ucl.ubiq/Runtime/UI/";
        private const string DestinationRoot =
            "Assets/Prefabs/Ubiq Sample UI";
        private const string MenuDestination = DestinationRoot + "/Menu.prefab";

        [MenuItem("Tools/MeshUp/Import And Wire Ubiq Sample Menu")]
        public static void ImportAndWire()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(DestinationRoot);
            CopyIfMissing("Menu.prefab");
            CopyIfMissing("Browse Menu Control.prefab");
            CopyIfMissing("Keyboard.prefab");
            CopyIfMissing("Key.prefab");
            CopyIfMissing("Packages/com.ucl.ubiq/LICENSE.txt",
                DestinationRoot + "/LICENSE-Ubiq.txt");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var menu = AssetDatabase.LoadAssetAtPath<GameObject>(MenuDestination);
            if (menu == null)
            {
                throw new InvalidOperationException(
                    $"Failed to copy the Ubiq menu to {MenuDestination}.");
            }

            using (var validation = new SceneValidationScope(LobbyScene))
            {
                Wire(validation.Scene, FindPanel(validation.Scene), menu);
            }
            Debug.Log("Copied Ubiq's Menu, Browse Menu Control, Keyboard and "
                + "Key prefabs into Assets/Prefabs/Ubiq Sample UI and wired "
                + "the lobby menu to MeshUp's adapter.");
        }

        public static void ImportAndWireFromCommandLine()
        {
            ImportAndWire();
        }

        public static void RunLobbyRuntimeVerificationFromCommandLine()
        {
            EditorSceneManager.OpenScene(LobbyScene, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static RoomTotemPanel FindPanel(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var panel = root.GetComponentInChildren<RoomTotemPanel>(true);
                if (panel != null)
                {
                    return panel;
                }
            }
            throw new InvalidOperationException(
                $"No {nameof(RoomTotemPanel)} was found in {scene.path}.");
        }

        private static void Wire(Scene scene, RoomTotemPanel controller,
            GameObject menu)
        {
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("ubiqMenuPrefab").objectReferenceValue = menu;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void CopyIfMissing(string fileName)
        {
            CopyIfMissing(SourceRoot + fileName,
                $"{DestinationRoot}/{fileName}");
        }

        private static void CopyIfMissing(string source, string destination)
        {
            if (AssetDatabase.LoadMainAssetAtPath(destination) != null)
            {
                return;
            }
            if (!AssetDatabase.CopyAsset(source, destination))
            {
                throw new InvalidOperationException(
                    $"Could not copy Ubiq's {source} to {destination}.");
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            var separator = path.LastIndexOf('/');
            var parent = path[..separator];
            var name = path[(separator + 1)..];
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
