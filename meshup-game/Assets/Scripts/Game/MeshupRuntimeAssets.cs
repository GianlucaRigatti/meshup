using UnityEngine;

namespace Meshup.Game
{
    /// <summary>
    /// Keeps runtime-loaded assets included in player builds regardless of their folders.
    /// </summary>
    public sealed class MeshupRuntimeAssets : ScriptableObject
    {
        [SerializeField] private TextAsset mimeWords;
        [SerializeField] private TextAsset gameConfiguration;
        [SerializeField] private AudioClip generateButtonPress;
        [SerializeField] private AudioClip generateButtonRelease;
        [SerializeField] private AudioClip underwaterAmbience;

        public TextAsset MimeWords => mimeWords;
        public TextAsset GameConfiguration => gameConfiguration;
        public AudioClip GenerateButtonPress => generateButtonPress;
        public AudioClip GenerateButtonRelease => generateButtonRelease;
        public AudioClip UnderwaterAmbience => underwaterAmbience;

        public static MeshupRuntimeAssets LoadDefault()
        {
            return Resources.Load<MeshupRuntimeAssets>("MeshupRuntimeAssets");
        }
    }
}
