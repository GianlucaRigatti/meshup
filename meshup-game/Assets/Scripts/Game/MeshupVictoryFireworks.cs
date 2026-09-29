using System.Collections;
using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupVictoryFireworks : MonoBehaviour
    {
        private const float CelebrationSeconds = 12f;
        private const float SpawnHalfWidth = 7f;
        private const float MinDistanceBehindScreen = 18f;
        private const float MaxDistanceBehindScreen = 25f;
        private const float LaunchHeight = 0.25f;
        private const float FireworkLifetime = 10f;
        private const float RocketSeconds = 0.9f;
        private const float RocketHeight = 12f;

        [SerializeField] private Transform guesserScreen;
        // Each prefab has a burst on its root and a rocket trail beneath it.
        [SerializeField] private ParticleSystem[] fireworks;
        [SerializeField] private AudioClip[] explosionClips;
        private Coroutine celebration;
        private bool played;

        private bool HasPrefabs => fireworks != null && fireworks.Length > 0;

        public void Play()
        {
            if (played || !HasPrefabs) return;
            played = true;
            StartCelebration();
        }

        public void PlayForTesting()
        {
            if (HasPrefabs) StartCelebration();
        }

        private void StartCelebration()
        {
            if (celebration != null) StopCoroutine(celebration);
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
            var screen = guesserScreen != null ? guesserScreen : transform;
            var screenForward = Vector3.ProjectOnPlane(screen.forward, Vector3.up);
            if (screenForward.sqrMagnitude < 0.001f) screenForward = Vector3.forward;
            screenForward.Normalize();
            var screenRight = Vector3.Cross(Vector3.up, screenForward);
            var launchPosition = screen.position
                - screenForward * Random.Range(MinDistanceBehindScreen, MaxDistanceBehindScreen)
                + screenRight * Random.Range(-SpawnHalfWidth, SpawnHalfWidth);
            launchPosition.y = LaunchHeight;

            var prefab = fireworks[Random.Range(0, fireworks.Length)];
            var firework = Instantiate(prefab, launchPosition, Quaternion.identity);
            StartCoroutine(PlayFirework(firework, launchPosition));
        }

        private IEnumerator PlayFirework(ParticleSystem firework, Vector3 launchPosition)
        {
            // The burst stays stopped until the rocket reaches its apex.
            foreach (var particles in firework.GetComponentsInChildren<ParticleSystem>())
            {
                if (particles != firework) particles.Play(false);
            }
            var elapsed = 0f;
            while (elapsed < RocketSeconds)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / RocketSeconds);
                firework.transform.position = launchPosition
                    + Vector3.up * (RocketHeight * progress);
                yield return null;
            }

            firework.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            firework.Play(false);
            var audio = firework.GetComponent<AudioSource>();
            if (audio != null && explosionClips != null && explosionClips.Length > 0)
            {
                audio.clip = explosionClips[Random.Range(0, explosionClips.Length)];
                audio.Play();
            }
            Destroy(firework.gameObject, FireworkLifetime - RocketSeconds);
        }
    }
}
