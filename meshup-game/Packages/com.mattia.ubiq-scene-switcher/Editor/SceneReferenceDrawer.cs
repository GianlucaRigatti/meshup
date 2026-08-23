using UnityEditor;
using UnityEngine;

namespace Ubiq.SceneSwitcher.Editor
{
    [CustomPropertyDrawer(typeof(SerializableSceneReference))]
    public sealed class SceneReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property,
            GUIContent label)
        {
            var guid = property.FindPropertyRelative("assetGuid");
            var path = property.FindPropertyRelative("scenePath");
            var current = string.IsNullOrEmpty(path.stringValue)
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(path.stringValue);

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            var selected = (SceneAsset)EditorGUI.ObjectField(position, label,
                current, typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                var selectedPath = selected == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(selected);
                path.stringValue = selectedPath;
                guid.stringValue = string.IsNullOrEmpty(selectedPath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(selectedPath);
            }
            EditorGUI.EndProperty();
        }
    }
}
