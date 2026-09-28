using UnityEngine;

namespace Meshup.Game
{
    /// <summary>
    /// Lets the scene-local Escape menu suspend Ubiq's unmodified demo
    /// controller without replacing any of its movement or look behaviour.
    /// </summary>
    public sealed class UbiqDemoPlayerInputGate : MonoBehaviour
    {
        [SerializeField] private GameObject traditionalController;

        private void OnEnable()
        {
            if (traditionalController != null)
            {
                traditionalController.SetActive(true);
            }
        }

        private void OnDisable()
        {
            if (traditionalController != null)
            {
                traditionalController.SetActive(false);
            }
        }
    }
}
