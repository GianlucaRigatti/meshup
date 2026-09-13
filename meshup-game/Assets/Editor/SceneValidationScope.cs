using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTools
{
    /// <summary>
    /// Validates the current in-memory scene when open, or temporarily opens it
    /// additively. Never saves, reloads, or closes the user's existing scenes.
    /// </summary>
    internal sealed class SceneValidationScope : IDisposable
    {
        private readonly bool openedScene;
        private readonly Scene previousActiveScene;

        public Scene Scene { get; }

        public SceneValidationScope(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Validate authored scenes outside Play Mode.");
            }

            previousActiveScene = SceneManager.GetActiveScene();
            Scene = SceneManager.GetSceneByPath(path);
            if (!Scene.IsValid() || !Scene.isLoaded)
            {
                Scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedScene = true;
            }
        }

        public void Dispose()
        {
            if (!openedScene)
            {
                return;
            }
            EditorSceneManager.CloseScene(Scene, true);
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
        }
    }
}
