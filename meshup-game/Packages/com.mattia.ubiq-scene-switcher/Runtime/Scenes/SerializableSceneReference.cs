using System;
using UnityEngine;

namespace Ubiq.SceneSwitcher
{
    [Serializable]
    public sealed class SerializableSceneReference
    {
        [SerializeField] private string assetGuid = string.Empty;
        [SerializeField] private string scenePath = string.Empty;

        public string AssetGuid => assetGuid;
        public string ScenePath => scenePath;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(assetGuid)
            && !string.IsNullOrWhiteSpace(scenePath);

#if UNITY_EDITOR
        public void SetEditorValues(string guid, string path)
        {
            assetGuid = guid ?? string.Empty;
            scenePath = path ?? string.Empty;
        }
#endif
    }
}
