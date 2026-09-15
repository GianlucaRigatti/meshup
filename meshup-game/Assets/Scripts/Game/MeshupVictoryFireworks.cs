using System.Collections;
using OccaSoftware.Fireworks.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
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
        private const float QuestRocketSeconds = 0.9f;
        private const float QuestRocketHeight = 12f;

        private static readonly Color[][] QuestPalettes =
        {
            new[] { new Color(0.1f, 0.8f, 1f), new Color(0.2f, 0.35f, 1f) },
            new[] { new Color(0.45f, 1f, 0.2f), new Color(1f, 0.15f, 0.7f) },
            new[] { new Color(1f, 0.25f, 0.08f), new Color(1f, 0.8f, 0.15f) },
            new[] { Color.white, new Color(0.35f, 0.75f, 1f) }
        };

        private static Material questParticleMaterial;

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

            if (Application.platform == RuntimePlatform.Android)
            {
                StartCoroutine(PlayQuestFirework(prefab, launchPosition));
                return;
            }

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
            }
            Destroy(firework, FireworkLifetime);
        }

        private IEnumerator PlayQuestFirework(GameObject prefab,
            Vector3 launchPosition)
        {
            var firework = Instantiate(prefab, launchPosition,
                Quaternion.identity);
            foreach (var effect in firework.GetComponentsInChildren<VisualEffect>())
            {
                effect.enabled = false;
            }
            foreach (var audioSource in firework.GetComponentsInChildren<AudioSource>())
            {
                audioSource.volume *= VolumeMultiplier;
            }

            var palette = GetQuestPalette(prefab.name);
            var rocket = CreateQuestParticles(firework.transform, "Rocket Trail",
                palette[0], true);
            var elapsed = 0f;
            while (elapsed < QuestRocketSeconds)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / QuestRocketSeconds);
                firework.transform.position = launchPosition
                    + Vector3.up * (QuestRocketHeight * progress);
                yield return null;
            }

            rocket.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            var burst = CreateQuestParticles(firework.transform, "Burst",
                palette[1], false);
            burst.Play(true);
            foreach (var audioEvent in
                firework.GetComponentsInChildren<PlayVFXAudioEvent>())
            {
                audioEvent.PlayRandomClip();
            }
            Destroy(firework, FireworkLifetime - QuestRocketSeconds);
        }

        private static Color[] GetQuestPalette(string prefabName)
        {
            if (prefabName.Contains("Slime"))
            {
                return QuestPalettes[1];
            }
            if (prefabName.Contains("Warm"))
            {
                return QuestPalettes[2];
            }
            if (prefabName.Contains("Snow"))
            {
                return QuestPalettes[3];
            }
            return QuestPalettes[0];
        }

        private static ParticleSystem CreateQuestParticles(Transform parent,
            string name, Color color, bool rocket)
        {
            var particleObject = new GameObject(name);
            particleObject.transform.SetParent(parent, false);
            var particles = particleObject.AddComponent<ParticleSystem>();
            particles.Stop(true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = rocket;
            main.playOnAwake = rocket;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = color;
            main.startLifetime = rocket
                ? new ParticleSystem.MinMaxCurve(0.35f, 0.65f)
                : new ParticleSystem.MinMaxCurve(1.2f, 2.1f);
            main.startSpeed = rocket
                ? 0f : new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = rocket
                ? new ParticleSystem.MinMaxCurve(0.08f, 0.16f)
                : new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            main.gravityModifier = rocket ? 0f : 0.35f;
            main.maxParticles = rocket ? 80 : 180;

            var emission = particles.emission;
            emission.rateOverTime = rocket ? 55f : 0f;
            if (!rocket)
            {
                emission.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f, 110),
                    new ParticleSystem.Burst(0.12f, 35)
                });
            }

            var shape = particles.shape;
            shape.enabled = !rocket;
            if (!rocket)
            {
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.12f;
            }

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = fade;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GetQuestParticleMaterial();
            if (!rocket)
            {
                var trails = particles.trails;
                trails.enabled = true;
                trails.ratio = 0.8f;
                trails.lifetime = 0.45f;
                trails.dieWithParticles = true;
                renderer.trailMaterial = questParticleMaterial;
            }
            if (rocket)
            {
                particles.Play(true);
            }
            return particles;
        }

        private static Material GetQuestParticleMaterial()
        {
            if (questParticleMaterial != null)
            {
                return questParticleMaterial;
            }
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }
            questParticleMaterial = new Material(shader)
            {
                name = "Quest Fireworks (Runtime)"
            };
            questParticleMaterial.SetFloat("_Surface", 1f);
            questParticleMaterial.SetFloat("_Blend", 2f);
            questParticleMaterial.SetFloat("_SrcBlend",
                (float)BlendMode.SrcAlpha);
            questParticleMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            questParticleMaterial.SetFloat("_ZWrite", 0f);
            questParticleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            questParticleMaterial.renderQueue = (int)RenderQueue.Transparent;
            return questParticleMaterial;
        }
    }
}
