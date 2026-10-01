using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace Meshup.Game
{
    /// <summary>Enables fixed foveation after the Android XR display starts.</summary>
    public sealed class QuestFoveatedRendering : MonoBehaviour
    {
        private const float FoveationLevel = 0.5f;
        private static readonly List<XRDisplaySubsystem> Displays = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            var controller = new GameObject(nameof(QuestFoveatedRendering));
            DontDestroyOnLoad(controller);
            controller.AddComponent<QuestFoveatedRendering>();
        }

        private IEnumerator Start()
        {
            while (true)
            {
                Displays.Clear();
                SubsystemManager.GetSubsystems(Displays);
                foreach (var display in Displays)
                {
                    if (!display.running)
                    {
                        continue;
                    }

                    display.foveatedRenderingFlags =
                        XRDisplaySubsystem.FoveatedRenderingFlags.None;
                    display.foveatedRenderingLevel = FoveationLevel;
                    OpenXRSettings.Instance?.GetFeature<QuestFoveationOffsetFeature>()
                        ?.ApplyVerticalOffset();
                    yield break;
                }

                yield return null;
            }
        }
    }
}
