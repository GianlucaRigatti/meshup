using Ubiq.Messaging;
using Ubiq.Rooms;
using UnityEditor;
using UnityEngine;

namespace Ubiq.SceneSwitcher.Editor
{
    public static class RoomSceneSetupMenu
    {
        [MenuItem("GameObject/Ubiq/Room Scene Session", false, 10)]
        public static void CreateSessionInScene()
        {
            var root = new GameObject("Room Scene Session");
            Undo.RegisterCreatedObjectUndo(root, "Create Room Scene Session");
            root.AddComponent<NetworkScene>();
            var client = root.AddComponent<RoomClient>();
            var switcher = root.AddComponent<RoomSceneSwitcher>();
            var rig = new GameObject("Player Rig Root");
            Undo.RegisterCreatedObjectUndo(rig, "Create Player Rig Root");
            rig.transform.SetParent(root.transform, false);
            var serialized = new SerializedObject(switcher);
            serialized.FindProperty("roomClient").objectReferenceValue = client;
            serialized.FindProperty("playerRigRoot").objectReferenceValue = rig.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeGameObject = root;
        }

        [MenuItem("GameObject/Ubiq/Room Scene Anchor", false, 11)]
        public static void CreateAnchorInScene()
        {
            var anchor = new GameObject("Room Scene Anchor");
            Undo.RegisterCreatedObjectUndo(anchor, "Create Room Scene Anchor");
            anchor.AddComponent<RoomSceneAnchor>();
            Selection.activeGameObject = anchor;
        }
    }
}
