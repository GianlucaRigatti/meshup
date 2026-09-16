#if UNITY_EDITOR && XRI_3_0_7_OR_NEWER
using Ubiq.XRI.TraditionalControls;
using UnityEngine;

namespace Meshup.Lobby
{
    /// <summary>
    /// Keeps Ubiq's desktop controls operational in the Unity Editor while
    /// removing its large tutorial overlay from the Game view.
    /// </summary>
    internal static class EditorTraditionalControlsHintHider
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HideHints()
        {
            var controllers = Object.FindObjectsByType<
                TraditionalControlsHintController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var controller in controllers)
            {
                controller.enabled = false;
                SetActive(controller.prev, false);
                SetActive(controller.next, false);
                SetActive(controller.hide, false);

                foreach (var hint in controller.defaultHints)
                {
                    SetActive(hint, false);
                }
                foreach (var group in controller.hintsByPlatforms)
                {
                    foreach (var hint in group.hints)
                    {
                        SetActive(hint, false);
                    }
                }
            }
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null)
            {
                component.gameObject.SetActive(active);
            }
        }
    }
}
#endif
