using UnityEditor;
using UnityEngine;

namespace Ubiq.SceneSwitcher.Editor
{
    [CustomEditor(typeof(RoomSceneSwitcher))]
    public sealed class RoomSceneSwitcherEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            var switcher = (RoomSceneSwitcher)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Setup Validation", EditorStyles.boldLabel);
            foreach (var message in ConfigurationValidator.Validate(switcher))
                EditorGUILayout.HelpBox(message.Text, message.Type);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Missing Scenes"))
                    ConfigurationValidator.AddConfiguredScenesToBuildSettings(switcher);
                if (GUILayout.Button("Validate Scene Contents"))
                    ValidateContents(switcher);
            }
        }

        private static void ValidateContents(RoomSceneSwitcher switcher)
        {
            var messages = new System.Collections.Generic.List<ConfigurationValidator.Message>();
            messages.AddRange(ConfigurationValidator.ValidateSceneContents(
                switcher.PrivateScene, RoomSceneRole.Private));
            messages.AddRange(ConfigurationValidator.ValidateSceneContents(
                switcher.MultiplayerScene, RoomSceneRole.Multiplayer));
            var text = string.Join("\n", messages.ConvertAll(message => message.Text));
            var hasErrors = messages.Exists(message => message.Type == MessageType.Error);
            EditorUtility.DisplayDialog("Room Scene Validation", text,
                hasErrors ? "Fix Issues" : "OK");
        }
    }
}
