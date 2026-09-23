using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.Multiplayer
{
    /// <summary>Waits for the comfort transition and loads the requested scene.</summary>
    internal static class RoomSceneLoader
    {
        public static IEnumerator Load(string sceneName,
            Action<Action> transitionRequested, Action<bool> completed)
        {
            if (transitionRequested != null)
            {
                var transitionCompleted = false;
                void CompleteTransition() => transitionCompleted = true;
                try
                {
                    transitionRequested.Invoke(CompleteTransition);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    CompleteTransition();
                }
                while (!transitionCompleted)
                {
                    yield return null;
                }
                yield return new WaitForEndOfFrame();
            }

            var operation = SceneManager.LoadSceneAsync(sceneName,
                LoadSceneMode.Single);
            if (operation == null)
            {
                completed(false);
                yield break;
            }
            while (!operation.isDone)
            {
                yield return null;
            }
            completed(true);
        }
    }
}
