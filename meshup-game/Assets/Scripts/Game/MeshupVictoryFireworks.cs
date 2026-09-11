using System.Collections;
using OccaSoftware.Fireworks.Runtime;
using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupVictoryFireworks : MonoBehaviour
    {
        private const string FireworksResource = "Game/FireworkSpawner";
        private const float CelebrationSeconds = 12f;
        private const float SpawnRadius = 7f;
        private const float FireworkLifetime = 10f;

        private Transform focus;
        private FireworkSpawner template;
        private bool played;

        public void Configure(Transform celebrationFocus)
        {
            focus = celebrationFocus;
            var prefab = Resources.Load<GameObject>(FireworksResource);
            template = prefab != null ? prefab.GetComponent<FireworkSpawner>() : null;
            if (template == null)
            {
                Debug.LogWarning("[MeshUp] Victory fireworks prefab is missing.");
            }
        }

        public void Play()
        {
            if (played || template == null || template.visualEffects.Count == 0)
            {
                return;
            }

            played = true;
            StartCoroutine(PlayCelebration());
        }

        private IEnumerator PlayCelebration()
        {
            var elapsed = 0f;
            while (elapsed < CelebrationSeconds)
            {
                SpawnFirework();
                var delay = Random.Range(0.45f, 0.85f);
                elapsed += delay;
                yield return new WaitForSeconds(delay);
            }
        }

        private void SpawnFirework()
        {
            var prefab = template.visualEffects[
                Random.Range(0, template.visualEffects.Count)];
            var center = focus != null ? focus.position : transform.position;
            var offset = Random.insideUnitCircle * SpawnRadius;
            var launchPosition = new Vector3(center.x + offset.x,
                Mathf.Max(0f, center.y - 1.5f), center.z + offset.y);
            var firework = Instantiate(prefab, launchPosition,
                Quaternion.identity);
            Destroy(firework, FireworkLifetime);
        }
    }
}
