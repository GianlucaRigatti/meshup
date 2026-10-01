using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Meshup.Lobby
{
    /// <summary>Places the tracked body, rather than just the XR Origin, at the authored spawn.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XROrigin))]
    public sealed class LobbyTrackedSpawn : MonoBehaviour
    {
        private XROrigin xrOrigin;
        private Vector3 spawnPosition;
        private bool trackingWasValid;
        private bool placed;

        private void Awake()
        {
            xrOrigin = GetComponent<XROrigin>();
            spawnPosition = xrOrigin.Origin.transform.position;
        }

        private void LateUpdate()
        {
            if (placed)
            {
                return;
            }

            var headset = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            var positionTracked = headset.isValid
                && headset.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked)
                && isTracked
                && headset.TryGetFeatureValue(CommonUsages.trackingState, out var state)
                && (state & InputTrackingState.Position) != 0;
            TryPlaceAtSpawn(positionTracked);
        }

        private void TryPlaceAtSpawn(bool positionTracked)
        {
            if (placed)
            {
                return;
            }

            // Give the pose driver a full frame to apply the first tracked pose.
            // No timeout: the headset can become available well after scene load.
            if (!positionTracked || !trackingWasValid || xrOrigin.Camera == null)
            {
                trackingWasValid = positionTracked;
                return;
            }

            var origin = xrOrigin.Origin.transform;
            var cameraOffset = origin.InverseTransformPoint(xrOrigin.Camera.transform.position);
            cameraOffset.y = 0f;
            var bodyPosition = origin.TransformPoint(cameraOffset);
            var controller = origin.GetComponent<CharacterController>();
            var controllerWasEnabled = controller != null && controller.enabled;
            if (controllerWasEnabled)
            {
                controller.enabled = false;
            }

            // This is a spawn teleport. Sweeping through walls from an out-of-bounds
            // tracked position would stop short of the intended spawn.
            origin.position += spawnPosition - bodyPosition;
            if (controller != null)
            {
                // Keep the capsule under the headset until XRI next moves the body.
                var center = controller.center;
                center.x = cameraOffset.x;
                center.z = cameraOffset.z;
                controller.center = center;
                controller.enabled = controllerWasEnabled;
            }

            placed = true;
        }
    }
}
