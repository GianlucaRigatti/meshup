using UnityEngine;

namespace Meshup.Lobby
{
    public sealed class HologramProjectorEffect : MonoBehaviour
    {
        [SerializeField] private Transform innerRing;
        [SerializeField] private Transform outerRing;
        [SerializeField] private Renderer beamRenderer;
        [SerializeField] private float rotationSpeed = 42f;

        private Material beamMaterial;
        private Color baseColor;

        private void Awake()
        {
            beamMaterial = beamRenderer.material;
            baseColor = beamMaterial.HasProperty("_BaseColor")
                ? beamMaterial.GetColor("_BaseColor")
                : Color.cyan;
        }

        private void Update()
        {
            innerRing.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
            outerRing.Rotate(Vector3.up, -rotationSpeed * 0.65f * Time.deltaTime, Space.Self);
            var pulse = 0.65f + Mathf.Sin(Time.time * 3.2f) * 0.12f;
            var scale = 1f + Mathf.Sin(Time.time * 2.1f) * 0.045f;
            outerRing.localScale = new Vector3(scale, 1f, scale);
            if (beamMaterial.HasProperty("_BaseColor"))
            {
                beamMaterial.SetColor("_BaseColor", new Color(baseColor.r, baseColor.g, baseColor.b,
                    baseColor.a * pulse));
            }
        }
    }
}
