using System;
using System.Linq;
using Meta.WitAi.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.Game
{
    public static class MeshupGameBootstrap
    {
        private const string GameSceneName = "GameScene";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryInstall(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryInstall(scene);
        }

        private static void TryInstall(Scene scene)
        {
            if (!scene.IsValid() || scene.name != GameSceneName)
            {
                return;
            }

            PreserveVoiceAudioBuffer();

            if (UnityEngine.Object.FindAnyObjectByType<
                    MeshupGameCoordinator>() != null)
            {
                return;
            }

            var gameStart = UnityEngine.Object.FindAnyObjectByType<
                GameStartCoordinator>();
            var player = UnityEngine.Object.FindAnyObjectByType<
                PlayerMovementAuthority>();
            var wallObject = Find(scene, "Invisible_wall_game_area");
            var monitor = Find(scene, "guesser_monitor");
            var terminal = Find(scene, "mime_terminal");
            var button = Find(scene, "geneartor_button");
            var particlesObject = Find(scene, "generator_particle_system");
            var generator = Find(scene, "3D_Model_Generator");
            var wall = wallObject?.GetComponent<Collider>();
            var particles = particlesObject?.GetComponent<ParticleSystem>();
            var anchor = particlesObject != null
                ? particlesObject.transform
                : generator?.transform;
            if (gameStart == null || player == null || wall == null
                || monitor == null || terminal == null || button == null
                || anchor == null)
            {
                Debug.LogError("[MeshUp] Runtime installation failed because "
                    + "one or more authored GameScene props are missing.");
                return;
            }

            var root = new GameObject("MeshUp Game Runtime");
            SceneManager.MoveGameObjectToScene(root, scene);
            var coordinator = root.AddComponent<MeshupGameCoordinator>();
            coordinator.Configure(gameStart, player, wall, monitor.transform,
                terminal.transform, anchor, button, particles);
        }

        private static void PreserveVoiceAudioBuffer()
        {
            // Meta's Wit component looks up AudioBuffer again from OnDisable if
            // its cached reference was destroyed first. During a single-scene
            // load that lookup creates a new root object while Unity is tearing
            // the scene down, producing the "objects were not cleaned up"
            // error. AudioBuffer is an SDK-wide singleton, so keep it for the
            // application lifetime and reuse it on subsequent game visits.
            var audioBuffer = AudioBuffer.Instance;
            if (audioBuffer != null)
            {
                UnityEngine.Object.DontDestroyOnLoad(audioBuffer.gameObject);
            }
        }

        private static GameObject Find(Scene scene, string objectName)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(item => string.Equals(item.name, objectName,
                    StringComparison.Ordinal))?.gameObject;
        }
    }
}
