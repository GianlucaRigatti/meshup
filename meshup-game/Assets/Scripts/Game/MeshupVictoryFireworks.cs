using System.Collections;
using OccaSoftware.Fireworks.Runtime;
using UnityEngine;
using UnityEngine.VFX;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupVictoryFireworks : MonoBehaviour
    {
        private const string FireworksResource = "Game/FireworkSpawner";
        private const string RocketVelocityProperty = "Initial Rocket Velocity";
        private const float CelebrationSeconds = 12f;
        private const float SpawnHalfWidth = 7f;
        private const float MinDistanceBehindScreen = 18f;
        private const float MaxDistanceBehindScreen = 25f;
        private const float LaunchHeight = 0.25f;
        private const float RocketSpeed = 24f;
        private const float VolumeMultiplier = 0.3f;
        private const float FireworkLifetime = 10f;

        private Transform guesserScreen;
        private FireworkSpawner template;
        private Coroutine celebration;
        private bool played;

        public void Configure(Transform screen)
        {
            guesserScreen = screen;
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
            StartCelebration();
        }

        public void PlayForTesting()
        {
            if (template == null || template.visualEffects.Count == 0)
            {
                return;
            }

            StartCelebration();
        }

        private void StartCelebration()
        {
            if (celebration != null)
            {
                StopCoroutine(celebration);
            }
            celebration = StartCoroutine(PlayCelebration());
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
            celebration = null;
        }

        private void SpawnFirework()
        {
            var prefab = template.visualEffects[
                Random.Range(0, template.visualEffects.Count)];
            var screen = guesserScreen != null ? guesserScreen : transform;
            var screenForward = Vector3.ProjectOnPlane(screen.forward, Vector3.up);
            if (screenForward.sqrMagnitude < 0.001f)
            {
                screenForward = Vector3.forward;
            }
            screenForward.Normalize();
            var screenRight = Vector3.Cross(Vector3.up, screenForward);
            var launchPosition = screen.position
                - screenForward * Random.Range(MinDistanceBehindScreen,
                    MaxDistanceBehindScreen)
                + screenRight * Random.Range(-SpawnHalfWidth, SpawnHalfWidth);
            launchPosition.y = LaunchHeight;
            var firework = Instantiate(prefab, launchPosition,
                Quaternion.identity);
            foreach (var audioSource in firework.GetComponentsInChildren<AudioSource>())
            {
                audioSource.volume *= VolumeMultiplier;
            }
            foreach (var effect in firework.GetComponentsInChildren<VisualEffect>())
            {
                if (effect.HasVector3(RocketVelocityProperty))
                {
                    effect.SetVector3(RocketVelocityProperty,
                        Vector3.up * RocketSpeed);
                }

                // Enabling the instantiated prefab sends its default event before
                // the Quest-specific property overrides above are applied. Restart
                // it explicitly so the GPU simulation and its audio output event
                // reliably begin with the final values on device.
                effect.Reinit();
            }
            Destroy(firework, FireworkLifetime);
        }
    }
}
