using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    /// <summary>Checks the authored scene without rebuilding or saving it.</summary>
    public static class LobbyBedroomPolishValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PassName = "Bedroom Polish Pass";

        [MenuItem("Meshup/Lobby/Validate Bedroom Polish Pass")]
        public static void ValidatePolishPass()
        {
            using var validation = new SceneValidationScope(ScenePath);
            var scene = validation.Scene;
            var environment = RequireRoot(scene, "Bedroom Environment");
            var pass = environment.transform.Find(PassName);
            if (pass == null
                || pass.Find("Corner Wardrobe") == null
                || pass.Find("Bookcase Enclosure") == null
                || pass.Find("Coat Rack Accessories/Jacket") == null
                || pass.Find("Coat Rack Accessories/Shoes") == null)
            {
                throw new InvalidOperationException("One or more bedroom polish objects are missing.");
            }

            var extraBooks = pass.GetComponentsInChildren<Transform>(true)
                .Count(item => item.name.StartsWith("Extra Book ", StringComparison.Ordinal));
            if (extraBooks < 14)
            {
                throw new InvalidOperationException("The closed bookcase does not contain enough extra books.");
            }

            var ceiling = FindInScene<Light>(scene, "Soft Ceiling Fill");
            if (ceiling == null || ceiling.range < 6f || ceiling.intensity < 1f
                || ceiling.shadows != LightShadows.None)
            {
                throw new InvalidOperationException("The room-wide ceiling light is not configured correctly.");
            }

            var fairyLightsRoot = pass.Find("Fairy Bulb Lights");
            var fairyLights = fairyLightsRoot != null
                ? fairyLightsRoot.GetComponentsInChildren<Light>(true)
                : Array.Empty<Light>();
            if (fairyLights.Length != 9 || fairyLights.Any(light =>
                    light.intensity > 0.05f || light.range > 0.65f || light.shadows != LightShadows.None))
            {
                throw new InvalidOperationException("Each fairy bulb must have one very dim, shadowless local light.");
            }

            foreach (var name in new[]
                     {
                         "Warm Bedside Light", "Reading Lamp Fill", "Storybook Glow Light", "Fairy Lights Fill"
                     })
            {
                var light = FindInScene<Light>(scene, name);
                // Some optional decorative fills may have been removed by hand.
                if (light != null && (light.intensity > 0.6f || light.range > 2.2f))
                {
                    throw new InvalidOperationException($"{name} is not a small, dim local light.");
                }
            }

            Debug.Log($"Bedroom polish validation passed with {extraBooks} additional books.");
        }

        private static Transform RequireRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(item => item.name == name)?.transform
                ?? throw new InvalidOperationException($"Required scene root is missing: {name}");
        }

        private static T FindInScene<T>(Scene scene, string name) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault(component => component.gameObject.name == name);
        }
    }
}
