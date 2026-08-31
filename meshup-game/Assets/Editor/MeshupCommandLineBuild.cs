using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Meshup.Game.Editor
{
    public static class MeshupCommandLineBuild
    {
        public static void BuildAndroid()
        {
            Build(BuildTarget.Android, "MeshUp.apk");
        }

        public static void BuildMacOS()
        {
            Build(BuildTarget.StandaloneOSX, "MeshUp.app");
        }

        private static void Build(BuildTarget target, string defaultName)
        {
            var output = Environment.GetEnvironmentVariable(
                "MESHUP_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = System.IO.Path.Combine("Builds", defaultName);
            }
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException(
                    "No enabled scenes are configured for the player build.");
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"{target} build failed: "
                    + report.summary.result);
            }
        }
    }
}
