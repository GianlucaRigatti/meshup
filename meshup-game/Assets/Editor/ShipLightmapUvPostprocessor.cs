using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Meshup.Editor
{
    /// <summary>
    /// The replacement wreck mesh has almost fully split topology, which makes
    /// Unity's automatic UV chart generation pathologically slow. Its authored
    /// atlas is copied to UV2 so the stationary wreck can still receive lightmaps.
    /// </summary>
    public sealed class ShipLightmapUvPostprocessor : AssetPostprocessor
    {
        private const string ShipAssetPath =
            "Assets/EXTRA_Resources/underwater-wrecks/LowerPoly/HibbatAllah_Full.obj";

        private void OnPostprocessModel(GameObject importedRoot)
        {
            if (assetPath != ShipAssetPath)
            {
                return;
            }

            var processed = new HashSet<Mesh>();
            foreach (MeshFilter meshFilter in importedRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                CopyAuthoredUvToLightmapUv(meshFilter.sharedMesh, processed);
            }

            foreach (SkinnedMeshRenderer renderer in importedRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                CopyAuthoredUvToLightmapUv(renderer.sharedMesh, processed);
            }
        }

        private static void CopyAuthoredUvToLightmapUv(Mesh mesh, HashSet<Mesh> processed)
        {
            if (mesh == null || !processed.Add(mesh))
            {
                return;
            }

            Vector2[] authoredUv = mesh.uv;
            if (authoredUv.Length != mesh.vertexCount)
            {
                Debug.LogError(
                    $"Cannot create lightmap UV2 for {ShipAssetPath}/{mesh.name}: " +
                    $"UV0 has {authoredUv.Length} entries for {mesh.vertexCount} vertices.");
                return;
            }

            mesh.uv2 = authoredUv;
        }
    }
}
